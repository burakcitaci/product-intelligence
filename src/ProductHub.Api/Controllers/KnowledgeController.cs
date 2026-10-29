using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductHub.Application.Knowledge;

namespace ProductHub.Api.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api")]
public sealed class KnowledgeController(
    DocumentIngestionService ingestion,
    KnowledgeSearchService search
) : ControllerBase
{
    [HttpPost("documents"), Consumes("multipart/form-data"), RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<DocumentDto>> Upload(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var document = await ingestion.IngestAsync(
            new(file.FileName, file.ContentType, buffer.ToArray()),
            ct
        );
        return CreatedAtAction(nameof(GetDocument), new { id = document.Id }, document);
    }

    [HttpGet("documents/{id:guid}")]
    public Task<DocumentDto> GetDocument(Guid id, CancellationToken ct) =>
        ingestion.GetAsync(id, ct);

    [HttpPost("knowledge/search")]
    public Task<IReadOnlyList<KnowledgeSearchResult>> Search(
        SearchKnowledgeRequest request,
        CancellationToken ct
    ) => search.SearchAsync(new(request.Query, request.Limit), ct);
}

public sealed record SearchKnowledgeRequest(string Query, int Limit = 5);
