using System.Security.Cryptography;
using System.Text;
using ProductHub.Domain.Knowledge;

namespace ProductHub.Application.Knowledge;

public static class EvidenceRevision
{
    public static string ForChunk(DocumentChunk chunk) =>
        Hash(
            $"{chunk.Id}:{chunk.DocumentId}:{chunk.Ordinal}:{chunk.Document.Sha256}:{chunk.Document.ExtractionVersion}:{chunk.Text}"
        );

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
