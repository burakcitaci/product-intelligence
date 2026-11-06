using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductHub.Application.Abstractions;
using ProductHub.Application.Content;
using ProductHub.Application.Products;
using ProductHub.Domain.Knowledge;

namespace ProductHub.Application.Knowledge;

public sealed class DocumentIngestionService(
    IKnowledgeDocumentStore store,
    IDocumentTextExtractor extractor,
    ITextEmbeddingGenerator embeddings,
    IVectorStore vectors,
    IOptions<RagSettings> settings,
    ILogger<DocumentIngestionService> logger
)
{
    public async Task<DocumentDto> IngestAsync(
        IngestDocumentCommand command,
        CancellationToken cancellationToken
    )
    {
        Validate(command);
        var hash = Convert.ToHexString(SHA256.HashData(command.Content));
        if (await store.ExistsByHashAsync(command.ProductId, hash, cancellationToken))
            throw new ConflictException("This document is already attached to this product.");
        var document = KnowledgeDocument.Start(
            command.ProductId,
            command.FileName,
            command.ContentType,
            hash
        );
        await store.AddAsync(document, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        try
        {
            var extracted = await extractor.ExtractAsync(
                command.FileName,
                command.ContentType,
                command.Content,
                cancellationToken
            );
            var chunks = Chunk(
                extracted.Text,
                settings.Value.ChunkSize,
                settings.Value.ChunkOverlap
            );
            var generated = await embeddings.GenerateAsync(chunks, cancellationToken);
            if (generated.Count != chunks.Count)
                throw new DocumentIngestionException(
                    "embedding-count-mismatch",
                    "Embedding count did not match chunk count."
                );
            document.RecordExtractionVersion(
                $"local-extractor-v1/chunk-v1/{settings.Value.ChunkSize}/{settings.Value.ChunkOverlap}"
            );
            document.PrepareChunks(chunks);
            await store.AddChunksAsync(document.Chunks, cancellationToken);
            await vectors.UpsertAsync(
                document
                    .Chunks.Zip(
                        generated,
                        (chunk, vector) =>
                            new VectorDocument(
                                chunk.Id,
                                document.Id,
                                document.ProductId,
                                chunk.Ordinal,
                                chunk.Text,
                                vector
                            )
                    )
                    .ToArray(),
                cancellationToken
            );
            document.Complete();
            await store.SaveChangesAsync(cancellationToken);
            return Map(document);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await PersistFailureAsync(document, "cancelled");
            throw;
        }
        catch (DocumentIngestionException exception)
        {
            await PersistFailureAsync(document, exception.ErrorCode);
            throw;
        }
        catch (Exception exception)
        {
            await PersistFailureAsync(document, "ingestion-failed");
            throw new DocumentIngestionException(
                "ingestion-failed",
                "Document ingestion failed.",
                exception
            );
        }
    }

    public async Task<DocumentDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Map(
            await store.GetAsync(id, cancellationToken)
                ?? throw new NotFoundException($"Document '{id}' was not found.")
        );

    public async Task<IReadOnlyList<DocumentDto>> ListAsync(
        Guid productId,
        CancellationToken cancellationToken
    ) => (await store.ListAsync(productId, cancellationToken)).Select(Map).ToArray();

    public async Task<DocumentDto> SupersedeAsync(
        Guid productId,
        Guid documentId,
        Guid replacementId,
        CancellationToken ct
    )
    {
        var original =
            await store.GetAsync(documentId, ct)
            ?? throw new NotFoundException("Document was not found.");
        var replacement =
            await store.GetAsync(replacementId, ct)
            ?? throw new NotFoundException("Replacement document was not found.");
        if (original.ProductId != productId || replacement.ProductId != productId)
            throw new NotFoundException("Document was not found for this product.");
        try
        {
            original.SupersedeWith(replacement);
        }
        catch (InvalidOperationException error)
        {
            throw new ConflictException(error.Message);
        }
        await store.SaveChangesAsync(ct);
        return Map(original);
    }

    public async Task DeleteAsync(
        Guid productId,
        Guid documentId,
        CancellationToken cancellationToken
    )
    {
        var document =
            await store.GetAsync(documentId, cancellationToken)
            ?? throw new NotFoundException($"Document '{documentId}' was not found.");
        if (document.ProductId != productId)
            throw new NotFoundException($"Document '{documentId}' was not found for this product.");

        await vectors.DeleteDocumentAsync(document.Id, cancellationToken);
        store.Remove(document);
        await store.SaveChangesAsync(cancellationToken);
    }

    public static IReadOnlyList<string> Chunk(string text, int size, int overlap)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new DocumentIngestionException(
                "empty-document",
                "The document contains no extractable text."
            );
        var normalized = string.Join(
            ' ',
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        );
        var result = new List<string>();
        for (var start = 0; start < normalized.Length; start += size - overlap)
        {
            var length = Math.Min(size, normalized.Length - start);
            if (start + length < normalized.Length)
            {
                var boundary = normalized.LastIndexOf(' ', start + length - 1, length);
                if (boundary > start + size / 2)
                    length = boundary - start;
            }
            result.Add(normalized.Substring(start, length));
            if (start + length >= normalized.Length)
                break;
        }
        return result;
    }

    private void Validate(IngestDocumentCommand command)
    {
        var errors = new Dictionary<string, string[]>();
        if (command.ProductId == Guid.Empty)
            errors["productId"] = ["Product id is required."];
        if (string.IsNullOrWhiteSpace(command.FileName) || command.FileName.Length > 260)
            errors["fileName"] = ["A file name up to 260 characters is required."];
        if (string.IsNullOrWhiteSpace(command.ContentType) || command.ContentType.Length > 100)
            errors["contentType"] = ["A content type up to 100 characters is required."];
        if (command.Content.Length == 0 || command.Content.Length > settings.Value.MaxUploadBytes)
            errors["file"] =
            [
                $"File size must be between 1 and {settings.Value.MaxUploadBytes} bytes.",
            ];
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private async Task PersistFailureAsync(KnowledgeDocument document, string code)
    {
        document.Fail(code);
        try
        {
            await store.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to persist document ingestion failure {DocumentId}.",
                document.Id
            );
        }
    }

    private static DocumentDto Map(KnowledgeDocument x) =>
        new(
            x.Id,
            x.ProductId,
            x.FileName,
            x.ContentType,
            x.Status,
            x.Chunks.Count,
            x.CreatedAt,
            x.CompletedAt,
            x.ErrorCode,
            x.Sha256,
            x.ExtractionVersion,
            x.SupersededByDocumentId,
            x.SupersededAt
        );
}

public sealed class DocumentIngestionException(
    string errorCode,
    string message,
    Exception? inner = null
) : Exception(message, inner)
{
    public string ErrorCode { get; } = errorCode;
}
