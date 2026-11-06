using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProductHub.Application.Content;
using ProductHub.Domain.Content;
using ProductHub.Infrastructure.Persistence;

namespace ProductHub.Api.Services;

/// <summary>One bounded worker per host, with database leases across hosts. No retry of an ambiguous model call.</summary>
public sealed class GenerationJobWorker(
    IServiceScopeFactory scopes,
    ILogger<GenerationJobWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecoverAsync(stoppingToken);
                if (await RunNextAsync(stoppingToken))
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Generation worker iteration failed.");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<bool> RunNextAsync(CancellationToken stop)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductHubDbContext>();
        var job = await db
            .GenerationJobs.AsNoTracking()
            .Where(j => j.Status == "Queued" && !j.CancellationRequested)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(stop);
        if (job is null)
            return false;
        var lease = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var claimed = await db
            .GenerationJobs.Where(j =>
                j.Id == job.Id && j.Status == "Queued" && !j.CancellationRequested
            )
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(j => j.Status, "Running")
                        .SetProperty(j => j.Stage, "Retrieving")
                        .SetProperty(j => j.LeaseToken, lease)
                        .SetProperty(j => j.LeaseExpiresAt, now.AddMinutes(2))
                        .SetProperty(j => j.UpdatedAt, now),
                stop
            );
        if (claimed == 0)
            return true;
        using var run = CancellationTokenSource.CreateLinkedTokenSource(stop);
        using var monitorStop = new CancellationTokenSource();
        var monitor = MonitorAsync(job.Id, lease, run, monitorStop.Token);
        string status = "Failed";
        string? code = null;
        string? message = null;
        Guid? draftId = null;
        try
        {
            var command = JsonSerializer.Deserialize<GenerateDraftCommand>(job.CommandJson)!;
            var result = await scope
                .ServiceProvider.GetRequiredService<ContentGenerationService>()
                .GenerateAsync(
                    command,
                    run.Token,
                    async (stage, ct) =>
                    {
                        using var progressScope = scopes.CreateScope();
                        var progressDb =
                            progressScope.ServiceProvider.GetRequiredService<ProductHubDbContext>();
                        var updated = await progressDb
                            .GenerationJobs.Where(j =>
                                j.Id == job.Id
                                && j.LeaseToken == lease
                                && j.Status == "Running"
                                && !j.CancellationRequested
                            )
                            .ExecuteUpdateAsync(
                                s =>
                                    s.SetProperty(j => j.Stage, stage)
                                        .SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow),
                                ct
                            );
                        if (updated == 0)
                        {
                            run.Cancel();
                            ct.ThrowIfCancellationRequested();
                        }
                    }
                );
            draftId = result.Id;
            status = "Succeeded";
        }
        catch (OperationCanceledException)
        {
            status = "Cancelled";
            code = "cancelled";
            message = "Generation was cancelled. A fresh key is needed to retry.";
        }
        catch (GenerationUnavailableException error)
        {
            code = error.ErrorCode;
            message = error.Message;
        }
        catch (Exception error)
        {
            code = "generation-failed";
            message =
                "Generation could not complete. Inspect server logs and use a fresh key to retry.";
            logger.LogError(error, "Generation job {JobId} failed.", job.Id);
        }
        finally
        {
            monitorStop.Cancel();
            await monitor;
        }
        // A draft committed just before cancellation remains a success, and is never auto-published.
        using var finalScope = scopes.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<ProductHubDbContext>();
        await finalDb
            .GenerationJobs.Where(j =>
                j.Id == job.Id && j.LeaseToken == lease && j.Status == "Running"
            )
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(j => j.Status, status)
                        .SetProperty(j => j.Stage, status == "Succeeded" ? "Ready" : status)
                        .SetProperty(j => j.DraftId, draftId)
                        .SetProperty(j => j.ErrorCode, code)
                        .SetProperty(j => j.ErrorMessage, message)
                        .SetProperty(j => j.LeaseToken, (Guid?)null)
                        .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                        .SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow),
                CancellationToken.None
            );
        return true;
    }

    private async Task MonitorAsync(
        Guid id,
        Guid lease,
        CancellationTokenSource run,
        CancellationToken stop
    )
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ProductHubDbContext>();
                var now = DateTimeOffset.UtcNow;
                var updated = await db
                    .GenerationJobs.Where(j =>
                        j.Id == id
                        && j.LeaseToken == lease
                        && j.Status == "Running"
                        && !j.CancellationRequested
                    )
                    .ExecuteUpdateAsync(
                        s =>
                            s.SetProperty(j => j.LeaseExpiresAt, now.AddMinutes(2))
                                .SetProperty(j => j.UpdatedAt, now),
                        stop
                    );
                if (updated == 0)
                {
                    run.Cancel();
                    break;
                }
                await Task.Delay(TimeSpan.FromSeconds(1), stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error)
        {
            logger.LogError(error, "Generation lease heartbeat failed for {JobId}.", id);
            run.Cancel();
        }
    }

    public async Task RecoverAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductHubDbContext>();
        var now = DateTimeOffset.UtcNow;
        var expired = await db
            .GenerationJobs.AsNoTracking()
            .Where(j => j.Status == "Running" && j.LeaseExpiresAt < now)
            .Take(50)
            .ToArrayAsync(ct);
        foreach (var job in expired)
        {
            var draft = await db
                .ContentDrafts.AsNoTracking()
                .Join(
                    db.AiGenerations,
                    d => d.GenerationId,
                    g => g.Id,
                    (d, g) => new { Draft = d, Generation = g }
                )
                .Where(x =>
                    x.Generation.ProductId == job.ProductId
                    && x.Generation.IdempotencyKey == job.IdempotencyKey
                )
                .Select(x => x.Draft.Id)
                .FirstOrDefaultAsync(ct);
            var hasGeneration = await db.AiGenerations.AnyAsync(
                g => g.ProductId == job.ProductId && g.IdempotencyKey == job.IdempotencyKey,
                ct
            );
            var status =
                draft != Guid.Empty ? "Succeeded"
                : job.CancellationRequested ? "Cancelled"
                : hasGeneration ? "Failed"
                : "Queued";
            var code = status == "Failed" ? "interrupted" : null;
            var message = code is null
                ? null
                : "Worker interrupted; model execution is uncertain. Use a fresh idempotency key to retry.";
            await db
                .GenerationJobs.Where(j =>
                    j.Id == job.Id
                    && j.LeaseToken == job.LeaseToken
                    && j.LeaseExpiresAt < now
                    && j.Status == "Running"
                )
                .ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(j => j.Status, status)
                            .SetProperty(j => j.Stage, status == "Succeeded" ? "Ready" : status)
                            .SetProperty(j => j.DraftId, draft != Guid.Empty ? (Guid?)draft : null)
                            .SetProperty(j => j.ErrorCode, code)
                            .SetProperty(j => j.ErrorMessage, message)
                            .SetProperty(j => j.LeaseToken, (Guid?)null)
                            .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                            .SetProperty(j => j.UpdatedAt, now),
                    ct
                );
        }
    }
}
