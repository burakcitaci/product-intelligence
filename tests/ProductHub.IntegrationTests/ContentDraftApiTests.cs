using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductHub.Application.Content;
using ProductHub.Application.Knowledge;
using ProductHub.Application.Products;
using ProductHub.Domain.Content;
using ProductHub.Domain.Knowledge;
using ProductHub.Infrastructure.Persistence;

namespace ProductHub.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class ContentDraftApiTests(SqlServerFixture database)
{
    [Fact]
    public async Task GenerateThenRead_PersistsDraftWithoutOllama()
    {
        var fake = new FakeContentGenerator();
        await using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        var productResponse = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = $"AI-{Guid.NewGuid():N}",
                name = "Trail Bottle",
                brand = "Northwind",
                attributes = new[]
                {
                    new
                    {
                        name = "Volume",
                        value = "750",
                        unit = "ml",
                    },
                },
            }
        );
        var product = await productResponse.Content.ReadFromJsonAsync<ProductDto>();

        var idempotencyKey = $"test-{Guid.NewGuid():N}";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/products/{product!.Id}/drafts/generate"
        )
        {
            Content = JsonContent.Create(new { language = "de" }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = await response.Content.ReadFromJsonAsync<ContentDraftDto>();
        Assert.NotNull(draft);
        Assert.Equal("de", draft.Language);
        Assert.Equal("Faktischer Titel", draft.Title);
        Assert.Equal(1, fake.CallCount);

        using var replayRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/products/{product.Id}/drafts/generate"
        )
        {
            Content = JsonContent.Create(new { language = "de" }),
        };
        replayRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        var replay = await (
            await client.SendAsync(replayRequest)
        ).Content.ReadFromJsonAsync<ContentDraftDto>();
        Assert.Equal(draft.Id, replay!.Id);
        Assert.Equal(1, fake.CallCount);

        var stored = await client.GetFromJsonAsync<ContentDraftDto>($"/api/drafts/{draft.Id}");
        Assert.Equal(draft.Id, stored!.Id);
        var list = await client.GetFromJsonAsync<List<ContentDraftDto>>(
            $"/api/products/{product.Id}/drafts"
        );
        Assert.Contains(list!, x => x.Id == draft.Id);
    }

    [Fact]
    public async Task Generate_WithoutIdempotencyKey_ReturnsBadRequest()
    {
        await using var factory = CreateFactory(new FakeContentGenerator());
        using var client = factory.CreateClient();
        var productResponse = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = $"NO-KEY-{Guid.NewGuid():N}",
                name = "Item",
                brand = "Brand",
                attributes = Array.Empty<object>(),
            }
        );
        var product = await productResponse.Content.ReadFromJsonAsync<ProductDto>();
        var response = await client.PostAsJsonAsync(
            $"/api/products/{product!.Id}/drafts/generate",
            new { language = "de" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Review_ApprovesDraftAndExposesGenerationMetadata()
    {
        await using var factory = CreateFactory(new FakeContentGenerator());
        using var client = factory.CreateClient();
        var productResponse = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = $"REVIEW-{Guid.NewGuid():N}",
                name = "Review Item",
                brand = "Northwind",
                attributes = Array.Empty<object>(),
            }
        );
        var product = await productResponse.Content.ReadFromJsonAsync<ProductDto>();
        using var generate = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/products/{product!.Id}/drafts/generate"
        )
        {
            Content = JsonContent.Create(new { language = "en" }),
        };
        generate.Headers.Add("Idempotency-Key", $"review-{Guid.NewGuid():N}");
        var draft = await (
            await client.SendAsync(generate)
        ).Content.ReadFromJsonAsync<ContentDraftDto>();

        var editResponse = await client.PutAsJsonAsync(
            $"/api/drafts/{draft!.Id}",
            new
            {
                title = "Human-edited title",
                shortDescription = "Human-edited short copy",
                longDescription = "Human-edited long copy for the product.",
                highlights = new[] { "Edited fact one", "Edited fact two", "Edited fact three" },
            }
        );
        var edited = await editResponse.Content.ReadFromJsonAsync<ContentDraftDto>();
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        Assert.Equal("Human-edited title", edited!.Title);

        var unacknowledged = await client.PostAsync($"/api/drafts/{draft.Id}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, unacknowledged.StatusCode);
        var approvedResponse = await client.PostAsJsonAsync(
            $"/api/drafts/{draft.Id}/approve",
            new { acknowledgeWarnings = true }
        );
        var approved = await approvedResponse.Content.ReadFromJsonAsync<ContentDraftDto>();
        Assert.Equal(HttpStatusCode.OK, approvedResponse.StatusCode);
        Assert.Equal(ContentDraftStatus.Approved, approved!.Status);
        Assert.True(approved.Review!.WarningsAcknowledged);
        Assert.NotNull(approved.Review.AcknowledgedBy);
        Assert.NotNull(approved.Review.AcknowledgedAt);
        var reloaded = await client.GetFromJsonAsync<ContentDraftDto>($"/api/drafts/{draft.Id}");
        Assert.True(reloaded!.Review!.WarningsAcknowledged);
        Assert.Equal(edited.Review!.Context.Sku, reloaded.Review.Context.Sku);
        Assert.Equal(edited.Review.Context.Name, reloaded.Review.Context.Name);

        var editAfterApproval = await client.PutAsJsonAsync(
            $"/api/drafts/{draft.Id}",
            new
            {
                title = "Too late",
                shortDescription = "Short",
                longDescription = "Long",
                highlights = new[] { "One", "Two", "Three" },
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, editAfterApproval.StatusCode);

        var rejectedAfterApproval = await client.PostAsync($"/api/drafts/{draft.Id}/reject", null);
        Assert.Equal(HttpStatusCode.OK, rejectedAfterApproval.StatusCode);
        var rejected = await rejectedAfterApproval.Content.ReadFromJsonAsync<ContentDraftDto>();
        Assert.Equal(ContentDraftStatus.Rejected, rejected!.Status);
        Assert.False(rejected.IsCurrent);

        var metadata = await client.GetFromJsonAsync<AiGenerationMetadataDto>(
            $"/api/drafts/{draft.Id}/generation"
        );
        Assert.Equal("Ollama", metadata!.Provider);
        Assert.Equal(draft.GenerationId, metadata.Id);
    }

    [Theory]
    [InlineData(true, "None")]
    [InlineData(false, "None")]
    [InlineData(true, "Naive")]
    [InlineData(false, "Naive")]
    [InlineData(true, "Advanced")]
    [InlineData(false, "Advanced")]
    [InlineData(true, "Graph")]
    [InlineData(false, "Graph")]
    public async Task Generate_SelectedGroundingOptionsRoundTripThroughDatabase(
        bool includeFacts,
        string mode
    )
    {
        var generator = new FakeContentGenerator();
        var vectors = new FakeVectors();
        await using var factory = CreateFactory(generator, vectors);
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = $"RAG-{Guid.NewGuid():N}",
                name = "Bottle",
                brand = "Acme",
                attributes = new[]
                {
                    new
                    {
                        name = "Capacity",
                        value = "750",
                        unit = "ml",
                    },
                },
            }
        );
        var product = (await response.Content.ReadFromJsonAsync<ProductDto>())!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProductHubDbContext>();
            Assert.False(db.Database.HasPendingModelChanges());
            var document = KnowledgeDocument.Start(
                product.Id,
                "manual.md",
                "text/plain",
                Guid.NewGuid().ToString("N")
            );
            document.Complete(["Clean the bottle with warm water."]);
            db.KnowledgeDocuments.Add(document);
            await db.SaveChangesAsync();
            vectors.Hits = [new(document.Chunks.Single().Id, .9f)];
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/products/{product.Id}/drafts/generate"
        )
        {
            Content = JsonContent.Create(
                new
                {
                    language = "en",
                    includeFacts,
                    retrievalMode = mode,
                    topK = 3,
                }
            ),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var generated = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, generated.StatusCode);
        Assert.NotNull(generated.Headers.Location);
        var draft = (await generated.Content.ReadFromJsonAsync<ContentDraftDto>())!;
        var metadata = (
            await client.GetFromJsonAsync<AiGenerationMetadataDto>(
                $"/api/drafts/{draft.Id}/generation"
            )
        )!;
        Assert.Equal(includeFacts, metadata.IncludeFacts);
        Assert.Equal(mode, metadata.RetrievalMode);
        Assert.NotNull(metadata.Parameters);
        Assert.Equal(GeneratedContentContract.Version, metadata.Parameters.SchemaVersion);
        Assert.Equal(0, metadata.Parameters.Temperature);
        Assert.Equal(42, metadata.Parameters.Seed);
        Assert.Equal(2048, metadata.Parameters.MaxOutputTokens);
        Assert.Equal(mode == "None" ? "NotRequested" : "Succeeded", metadata.RetrievalStatus);
        Assert.Equal(mode == "None" ? null : (int?)3, metadata.RequestedTopK);
        Assert.Equal(includeFacts ? 1 : 0, generator.LastContext!.Attributes.Count);
        Assert.Equal(mode == "None" ? 0 : 1, generator.LastContext.Grounding.Count);
        if (mode == "Graph")
        {
            var source = Assert.Single(metadata.RetrievalSources);
            Assert.Equal(0, source.GraphHops);
            Assert.Equal(new[] { "manual.md#chunk-0" }, source.GraphPath);
        }
    }

    [Fact]
    public async Task Generate_GraphRagPersistsTwoHopSourcesMissingFromVectorResults()
    {
        var generator = new FakeContentGenerator();
        var vectors = new FakeVectors();
        await using var factory = CreateFactory(generator, vectors);
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = $"GRAPH-{Guid.NewGuid():N}",
                name = "Kettle",
                brand = "Acme",
                attributes = Array.Empty<object>(),
            }
        );
        var product = (await response.Content.ReadFromJsonAsync<ProductDto>())!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProductHubDbContext>();
            var root = KnowledgeDocument.Start(
                product.Id,
                "control.md",
                "text/plain",
                Guid.NewGuid().ToString("N")
            );
            root.Complete(["Thermostat controls kettle temperature."]);
            var bridge = KnowledgeDocument.Start(
                product.Id,
                "care.md",
                "text/plain",
                Guid.NewGuid().ToString("N")
            );
            bridge.Complete(["Thermostat needs regular descaling."]);
            var leaf = KnowledgeDocument.Start(
                product.Id,
                "cleaning.md",
                "text/plain",
                Guid.NewGuid().ToString("N")
            );
            leaf.Complete(["Descaling removes mineral residue."]);
            db.KnowledgeDocuments.AddRange(root, bridge, leaf);
            await db.SaveChangesAsync();
            vectors.Hits = [new(root.Chunks.Single().Id, .9f)];
        }
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/products/{product.Id}/drafts/generate"
        )
        {
            Content = JsonContent.Create(
                new
                {
                    language = "en",
                    includeFacts = false,
                    retrievalMode = "Graph",
                    topK = 3,
                }
            ),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var generated = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, generated.StatusCode);
        var draft = (await generated.Content.ReadFromJsonAsync<ContentDraftDto>())!;
        var metadata = (
            await client.GetFromJsonAsync<AiGenerationMetadataDto>(
                $"/api/drafts/{draft.Id}/generation"
            )
        )!;
        Assert.Equal("GraphRag", metadata.GroundingMode);
        Assert.Equal("Graph", metadata.RetrievalMode);
        Assert.False(metadata.IncludeFacts);
        Assert.Equal(3, metadata.RetrievedDocumentCount);
        var source = Assert.Single(metadata.RetrievalSources, x => x.FileName == "cleaning.md");
        Assert.Equal(2, source.GraphHops);
        Assert.Equal(
            new[]
            {
                "control.md#chunk-0",
                "shared-concept:thermostat",
                "care.md#chunk-0",
                "shared-concept:descaling",
                "cleaning.md#chunk-0",
            },
            source.GraphPath
        );
        Assert.Empty(generator.LastContext!.Attributes);
        Assert.Contains(generator.LastContext.Grounding, x => x.Citation == "cleaning.md#chunk-0");
    }

    [Fact]
    public async Task Generate_InvalidRetrievalModeReturnsValidationProblem()
    {
        await using var factory = CreateFactory(new FakeContentGenerator());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/products/{Guid.NewGuid()}/drafts/generate"
        )
        {
            Content = JsonContent.Create(new { retrievalMode = "Unknown" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("retrievalMode", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SqlLexicalSearch_RecoversIndependentCandidatesAndPersistsRevisions()
    {
        await using var factory = CreateFactory(new FakeContentGenerator());
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = $"LX-{Guid.NewGuid():N}",
                name = "AX-900",
                brand = "Test",
            }
        );
        var product = (await response.Content.ReadFromJsonAsync<ProductDto>())!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProductHubDbContext>();
            var doc = KnowledgeDocument.Start(product.Id, "exact.md", "text/plain", "file-sha-v1");
            doc.Complete(["AX-900 cleaning instructions. Never immerse the controls."]);
            db.KnowledgeDocuments.Add(doc);
            await db.SaveChangesAsync();
        }
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/products/{product.Id}/drafts/generate"
        )
        {
            Content = JsonContent.Create(new { language = "en", retrievalMode = "Advanced" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var generated = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, generated.StatusCode);
        var draft = (await generated.Content.ReadFromJsonAsync<ContentDraftDto>())!;
        var audit = (
            await client.GetFromJsonAsync<AiGenerationMetadataDto>(
                $"/api/drafts/{draft.Id}/generation"
            )
        )!;
        var source = Assert.Single(audit.RetrievalSources);
        Assert.Equal(0, source.DenseCandidateCount);
        Assert.Equal(1, source.LexicalCandidateCount);
        Assert.Equal("file-sha-v1", source.DocumentRevision);
        Assert.Equal(64, source.ChunkRevision!.Length);
        Assert.Equal(source.ChunkRevision, draft.Review!.Context.Grounding.Single().ChunkRevision);
    }

    [Fact]
    public async Task Jobs_DuplicateKeySurvivesReconnectAndCancelledQueueMakesNoModelCall()
    {
        var fake = new FakeContentGenerator();
        await using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        var productResponse = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = $"JB-{Guid.NewGuid():N}",
                name = "Bottle",
                brand = "Test",
            }
        );
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductDto>())!;
        var key = Guid.NewGuid().ToString();
        async Task<GenerationJobDto> Enqueue()
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/products/{product.Id}/generation-jobs"
            )
            {
                Content = JsonContent.Create(new { language = "en" }),
            };
            request.Headers.Add("Idempotency-Key", key);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<GenerationJobDto>())!;
        }
        var first = await Enqueue();
        Assert.Equal(first.Id, (await Enqueue()).Id);
        var reconnect = await client.GetFromJsonAsync<GenerationJobDto[]>(
            $"/api/products/{product.Id}/generation-jobs"
        );
        Assert.Equal(first.Id, Assert.Single(reconnect!).Id);
        var cancelled = await client.PostAsync($"/api/generation-jobs/{first.Id}/cancel", null);
        Assert.Equal(
            "Cancelled",
            (await cancelled.Content.ReadFromJsonAsync<GenerationJobDto>())!.Status
        );
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task Jobs_WorkerSavesDraftAndRecoveryDoesNotRepeatAmbiguousModelCall()
    {
        var fake = new FakeContentGenerator();
        await using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        var productResponse = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = $"WK-{Guid.NewGuid():N}",
                name = "Bottle",
                brand = "Test",
            }
        );
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductDto>())!;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/products/{product.Id}/generation-jobs"
        )
        {
            Content = JsonContent.Create(new { language = "en" }),
        };
        var key = Guid.NewGuid().ToString();
        request.Headers.Add("Idempotency-Key", key);
        var queued = (
            await (await client.SendAsync(request)).Content.ReadFromJsonAsync<GenerationJobDto>()
        )!;
        var worker = new ProductHub.Api.Services.GenerationJobWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft
                .Extensions
                .Logging
                .Abstractions
                .NullLogger<ProductHub.Api.Services.GenerationJobWorker>
                .Instance
        );
        Assert.True(await worker.RunNextAsync(default));
        var ready = (
            await client.GetFromJsonAsync<GenerationJobDto>($"/api/generation-jobs/{queued.Id}")
        )!;
        Assert.Equal("Succeeded", ready.Status);
        Assert.Equal("Ready", ready.Stage);
        Assert.NotNull(ready.DraftId);
        Assert.Equal(1, fake.CallCount);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProductHubDbContext>();
            await db
                .GenerationJobs.Where(j => j.Id == queued.Id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(j => j.Status, "Running")
                        .SetProperty(j => j.LeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-5))
                        .SetProperty(j => j.LeaseToken, Guid.NewGuid())
                );
        }
        await worker.RecoverAsync(default);
        Assert.Equal(
            "Succeeded",
            (
                await client.GetFromJsonAsync<GenerationJobDto>($"/api/generation-jobs/{queued.Id}")
            )!.Status
        );
        Assert.Equal(1, fake.CallCount);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProductHubDbContext>();
            await db.ContentDrafts.Where(d => d.Id == ready.DraftId).ExecuteDeleteAsync();
            await db
                .GenerationJobs.Where(j => j.Id == queued.Id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(j => j.Status, "Running")
                        .SetProperty(j => j.LeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-5))
                        .SetProperty(j => j.LeaseToken, Guid.NewGuid())
                        .SetProperty(j => j.DraftId, (Guid?)null)
                );
        }
        await worker.RecoverAsync(default);
        var interrupted = (
            await client.GetFromJsonAsync<GenerationJobDto>($"/api/generation-jobs/{queued.Id}")
        )!;
        Assert.Equal("Failed", interrupted.Status);
        Assert.Equal("interrupted", interrupted.ErrorCode);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task Jobs_RunningCancellationReachesTheModelAndPersistsTerminalState()
    {
        var fake = new WaitingContentGenerator();
        await using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        var productResponse = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = $"CN-{Guid.NewGuid():N}",
                name = "Bottle",
                brand = "Test",
            }
        );
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductDto>())!;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/products/{product.Id}/generation-jobs"
        )
        {
            Content = JsonContent.Create(new { language = "en" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var queued = (
            await (await client.SendAsync(request)).Content.ReadFromJsonAsync<GenerationJobDto>()
        )!;
        var worker = new ProductHub.Api.Services.GenerationJobWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft
                .Extensions
                .Logging
                .Abstractions
                .NullLogger<ProductHub.Api.Services.GenerationJobWorker>
                .Instance
        );
        var running = worker.RunNextAsync(default);
        await fake.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var cancelling = await client.PostAsync($"/api/generation-jobs/{queued.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelling.StatusCode);
        await running.WaitAsync(TimeSpan.FromSeconds(15));
        var cancelled = (
            await client.GetFromJsonAsync<GenerationJobDto>($"/api/generation-jobs/{queued.Id}")
        )!;
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Null(cancelled.DraftId);
    }

    private sealed class WaitingContentGenerator : IContentGenerator
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GeneratedProductContent> GenerateAsync(
            ProductContentContext context,
            GenerationOptions options,
            CancellationToken ct
        )
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Cancellation should interrupt generation.");
        }
    }

    [Fact]
    public async Task Audit_IncludesFailuresCancelledJobsAndSuccessWithoutDuplicatingJobGenerations()
    {
        var errorText = "Ollama could not complete the generation request.";
        await using var factory = CreateFactory(
            new FakeContentGenerator
            {
                Error = new ContentGenerationException("generation-failed", errorText),
            }
        );
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = $"AUD-{Guid.NewGuid():N}",
                name = "Audit product",
                brand = "Test",
            }
        );
        var product = (await response.Content.ReadFromJsonAsync<ProductDto>())!;
        using (
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/products/{product.Id}/drafts/generate"
            )
            {
                Content = JsonContent.Create(new { language = "de", includeFacts = false }),
            }
        )
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            Assert.Equal(
                HttpStatusCode.ServiceUnavailable,
                (await client.SendAsync(request)).StatusCode
            );
        }
        async Task<GenerationJobDto> Enqueue(string language, string mode, bool facts)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/products/{product.Id}/generation-jobs"
            )
            {
                Content = JsonContent.Create(
                    new
                    {
                        language,
                        retrievalMode = mode,
                        includeFacts = facts,
                        topK = 3,
                        model = "llama3.1:8b",
                    }
                ),
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            var queued = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
            return (await queued.Content.ReadFromJsonAsync<GenerationJobDto>())!;
        }
        var failedJob = await Enqueue("en", "None", true);
        var worker = new ProductHub.Api.Services.GenerationJobWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft
                .Extensions
                .Logging
                .Abstractions
                .NullLogger<ProductHub.Api.Services.GenerationJobWorker>
                .Instance
        );
        await worker.RunNextAsync(default);
        var cancelled = await Enqueue("de", " advanced ", false);
        await client.PostAsync($"/api/generation-jobs/{cancelled.Id}/cancel", null);
        Guid successId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProductHubDbContext>();
            var generation = AiGeneration.Start(
                product.Id,
                Guid.NewGuid().ToString(),
                "Test",
                "qwen3:8b",
                "test",
                "en",
                GroundingMode.GraphRag,
                "audit-test",
                2
            );
            generation.Complete(12, 24);
            var draft = ContentDraft.Create(
                product.Id,
                "en",
                "Success",
                "Short",
                "Long",
                [],
                generation.Id
            );
            db.AiGenerations.Add(generation);
            db.ContentDrafts.Add(draft);
            successId = draft.Id;
            var outsider = ProductHub.Domain.Products.Product.Create(
                $"OTHER-{Guid.NewGuid():N}",
                "Other",
                "Test"
            );
            db.Products.Add(outsider);
            var privateGeneration = AiGeneration.Start(
                outsider.Id,
                "other-key",
                "Test",
                "private",
                "test",
                "de",
                GroundingMode.FactsOnly,
                "other-trace"
            );
            privateGeneration.Fail("timeout");
            db.AiGenerations.Add(privateGeneration);
            await db.SaveChangesAsync();
        }
        var all = (
            await client.GetFromJsonAsync<PagedResult<GenerationAuditEntryDto>>(
                $"/api/products/{product.Id}/generations"
            )
        )!;
        Assert.Equal(4, all.TotalCount);
        Assert.Equal(4, all.Items.Count);
        var failed = Assert.Single(all.Items, entry => entry.JobId == failedJob.Id);
        Assert.Equal("Failed", failed.Status);
        Assert.Equal(errorText, failed.ErrorMessage);
        Assert.NotNull(failed.GenerationId);
        Assert.Null(failed.DraftId);
        Assert.NotNull(failed.Metadata);
        Assert.Equal("llama3.1:8b", failed.Model);
        Assert.Equal("en", failed.Language);
        Assert.True(failed.IncludeFacts);
        var beforeInference = Assert.Single(all.Items, entry => entry.JobId == cancelled.Id);
        Assert.Equal("Cancelled", beforeInference.Status);
        Assert.Null(beforeInference.GenerationId);
        Assert.Null(beforeInference.Metadata);
        Assert.Equal("Advanced", beforeInference.RetrievalMode);
        Assert.False(beforeInference.IncludeFacts);
        Assert.Equal(3, beforeInference.RequestedTopK);
        Assert.Equal(
            successId,
            Assert.Single(all.Items, entry => entry.Status == "Succeeded").DraftId
        );
        var synchronous = Assert.Single(
            all.Items,
            entry => entry.Status == "Failed" && entry.JobId is null
        );
        Assert.Equal(errorText, synchronous.ErrorMessage);
        Assert.False(synchronous.IncludeFacts);
        var failedOnly = (
            await client.GetFromJsonAsync<PagedResult<GenerationAuditEntryDto>>(
                $"/api/products/{product.Id}/generations?status=failed"
            )
        )!;
        Assert.Equal(2, failedOnly.TotalCount);
        Assert.All(failedOnly.Items, entry => Assert.Equal("Failed", entry.Status));
        var english = (
            await client.GetFromJsonAsync<PagedResult<GenerationAuditEntryDto>>(
                $"/api/products/{product.Id}/generations?language=en"
            )
        )!;
        Assert.Equal(2, english.TotalCount);
        Assert.All(english.Items, entry => Assert.Equal("en", entry.Language));
        var first = (
            await client.GetFromJsonAsync<PagedResult<GenerationAuditEntryDto>>(
                $"/api/products/{product.Id}/generations?pageSize=2"
            )
        )!;
        var second = (
            await client.GetFromJsonAsync<PagedResult<GenerationAuditEntryDto>>(
                $"/api/products/{product.Id}/generations?pageSize=2&page=2"
            )
        )!;
        Assert.Equal(
            4,
            first.Items.Concat(second.Items).Select(entry => entry.Id).Distinct().Count()
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await client.GetAsync($"/api/products/{product.Id}/generations?status=invalid")
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/products/{Guid.NewGuid()}/generations")).StatusCode
        );
    }

    private WebApplicationFactory<Program> CreateFactory(
        IContentGenerator generator,
        FakeVectors? vectors = null
    ) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder
                .UseEnvironment("Testing")
                .ConfigureServices(services =>
                {
                    services.RemoveAll<ProductHubDbContext>();
                    services.RemoveAll<DbContextOptions<ProductHubDbContext>>();
                    services.AddDbContext<ProductHubDbContext>(options =>
                        options.UseSqlServer(database.ConnectionString)
                    );
                    services.RemoveAll<IContentGenerator>();
                    services.AddSingleton(generator);
                    services.RemoveAll<ITextEmbeddingGenerator>();
                    services.AddSingleton<ITextEmbeddingGenerator, FakeEmbeddings>();
                    services.RemoveAll<IVectorStore>();
                    services.AddSingleton<IVectorStore>(vectors ?? new FakeVectors());
                })
        );

    private sealed class FakeEmbeddings : ITextEmbeddingGenerator
    {
        public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct
        ) =>
            Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(
                texts.Select(_ => new ReadOnlyMemory<float>(new float[] { 1 })).ToArray()
            );
    }

    private sealed class FakeVectors : IVectorStore
    {
        public IReadOnlyList<VectorSearchHit> Hits { get; set; } = [];

        public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
            ReadOnlyMemory<float> vector,
            int limit,
            Guid? productId,
            CancellationToken ct
        ) => Task.FromResult(Hits);

        public Task UpsertAsync(IReadOnlyList<VectorDocument> documents, CancellationToken ct) =>
            Task.CompletedTask;

        public Task DeleteDocumentAsync(Guid documentId, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class FakeContentGenerator : IContentGenerator
    {
        public Exception? Error { get; init; }
        public int CallCount { get; private set; }
        public ProductContentContext? LastContext { get; private set; }

        public Task<GeneratedProductContent> GenerateAsync(
            ProductContentContext context,
            GenerationOptions options,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            LastContext = context;
            if (Error is not null)
                throw Error;
            return Task.FromResult(
                new GeneratedProductContent(
                    "Faktischer Titel",
                    "Kurze Beschreibung",
                    "Eine lÃ¤ngere, ausschlieÃŸlich faktische Beschreibung.",
                    ["Fakt eins", "Fakt zwei", "Fakt drei"],
                    10,
                    20
                )
            );
        }
    }
}
