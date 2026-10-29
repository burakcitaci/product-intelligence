using System.Text;
using ProductHub.Application.Knowledge;
using UglyToad.PdfPig;

namespace ProductHub.Infrastructure.Knowledge;

internal sealed class LocalDocumentTextExtractor : IDocumentTextExtractor
{
    private static readonly HashSet<string> TextTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/markdown",
        "text/csv",
        "application/json",
        "application/xml",
        "text/xml",
    };

    public Task<ExtractedDocument> ExtractAsync(
        string fileName,
        string contentType,
        ReadOnlyMemory<byte> content,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (
            contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
        )
        {
            using var stream = new MemoryStream(content.ToArray());
            using var pdf = PdfDocument.Open(stream);
            return Task.FromResult(
                new ExtractedDocument(
                    string.Join(Environment.NewLine, pdf.GetPages().Select(x => x.Text))
                )
            );
        }
        if (
            TextTypes.Contains(contentType)
            || new[] { ".txt", ".md", ".csv", ".json", ".xml" }.Contains(
                Path.GetExtension(fileName),
                StringComparer.OrdinalIgnoreCase
            )
        )
            return Task.FromResult(new ExtractedDocument(Encoding.UTF8.GetString(content.Span)));
        throw new DocumentIngestionException(
            "unsupported-document-type",
            $"Document type '{contentType}' is not supported."
        );
    }
}
