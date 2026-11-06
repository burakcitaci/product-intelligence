using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ProductHub.Application.Content;

public sealed record EvidenceSnapshot(string Id, string Kind, string Label, string Text);

public sealed record ReviewedClaim(
    string Id,
    string Field,
    string Text,
    string Status,
    string Reason,
    IReadOnlyList<EvidenceSnapshot> Evidence
);

public sealed record ClaimReviewReport(
    string Version,
    ProductContentContext Context,
    IReadOnlyList<ReviewedClaim> Claims,
    bool WarningsAcknowledged = false,
    string? AcknowledgedBy = null,
    DateTimeOffset? AcknowledgedAt = null,
    IReadOnlyList<string>? Warnings = null
)
{
    public bool RequiresAcknowledgement =>
        Claims.Any(c => c.Status != "Supported") || Warnings?.Count > 0;
}

/// <summary>Conservative deterministic checks. Unverified is not a factuality verdict.</summary>
public static class ClaimReview
{
    public static ClaimReviewReport Assess(
        GeneratedProductContent content,
        ProductContentContext context
    )
    {
        var evidence = new List<EvidenceSnapshot>
        {
            Snapshot("Identity", "SKU", context.Sku),
            Snapshot("Identity", "Product name", context.Name),
            Snapshot("Identity", "Brand", context.Brand),
        };
        if (context.IncludeFacts)
            evidence.AddRange(
                context.Attributes.Select(f =>
                    Snapshot("Fact", f.Name, $"{f.Value} {f.Unit}".Trim())
                )
            );
        evidence.AddRange(context.Grounding.Select(g => Snapshot("Advisory", g.Citation, g.Text)));
        var fields = new List<(string Field, string Text)> { ("title", content.Title) };
        fields.AddRange(Sentences(content.ShortDescription).Select(s => ("shortDescription", s)));
        fields.AddRange(Sentences(content.LongDescription).Select(s => ("longDescription", s)));
        fields.AddRange(content.Highlights.Select((s, i) => ($"highlights.{i}", s)));
        return new(
            "claim-review-v2",
            context,
            fields
                .Select(field => Check(field.Field, field.Text, evidence))
                .DistinctBy(c => c.Id)
                .ToArray(),
            Warnings: EvidenceWarnings(content, context)
        );
    }

    private static IReadOnlyList<string> EvidenceWarnings(
        GeneratedProductContent content,
        ProductContentContext context
    )
    {
        var warnings = new List<string>();
        if (!context.IncludeFacts || context.Attributes.Count == 0)
            warnings.Add(
                "No authoritative product specifications were supplied. Descriptions must stay within supplied identity; reference guidance cannot establish capabilities."
            );
        if (content.Highlights.Count == 0)
            warnings.Add(
                "The draft contains no highlights. Evidence may be insufficient; add verified facts or accept a shorter description after review."
            );
        return warnings;
    }

    private static ReviewedClaim Check(
        string field,
        string text,
        IReadOnlyList<EvidenceSnapshot> evidence
    )
    {
        var normalized = Normalize(text);
        var linked = evidence
            .Where(e =>
                e.Kind != "Advisory"
                && (
                    normalized.Contains(Normalize(e.Text), StringComparison.Ordinal)
                    || normalized == Normalize($"{e.Label}: {e.Text}")
                )
            )
            .ToList();
        var status = "Unverified";
        var reason =
            "No deterministic verification of this complete claim. Review its meaning and supporting evidence.";
        var quantities = Quantities(text);
        var authoritative = evidence
            .Where(e => e.Kind != "Advisory")
            .SelectMany(e => Quantities(e.Text))
            .ToArray();
        var conflicts = quantities
            .Where(q => authoritative.Any(a => a.Unit == q.Unit) && !authoritative.Any(a => a == q))
            .ToArray();
        var unsupported = quantities.Where(q => !authoritative.Contains(q)).ToArray();
        var risk = Regex.IsMatch(
            text,
            @"\b(certified|zertifiziert|certification|compatible|kompatibel|included|inklusive|waterproof|wasserdicht|medical|medizinisch)\b",
            RegexOptions.IgnoreCase
        );
        var exact = evidence.FirstOrDefault(e =>
            e.Kind != "Advisory"
            && (normalized == Normalize(e.Text) || normalized == Normalize($"{e.Label}: {e.Text}"))
        );
        if (exact is not null)
        {
            status = "Supported";
            reason =
                "Exact match to a supplied identity or fact. No semantic inference was applied.";
            linked = [exact];
        }
        else if (conflicts.Length > 0)
        {
            status = "Conflict";
            reason =
                "A numeric value differs from the supplied values for the same unit. Check the attribute and context manually.";
            linked.AddRange(
                evidence.Where(e =>
                    e.Kind == "Fact"
                    && Quantities(e.Text).Any(a => conflicts.Any(c => c.Unit == a.Unit))
                )
            );
        }
        else if (unsupported.Length > 0 || risk)
        {
            status = "Unsupported";
            reason =
                "A quantity or sensitive product claim lacks an exact authoritative match. This is a review flag, not a semantic proof of falsehood.";
        }
        else if (
            Regex.IsMatch(
                text,
                @"\b(use|clean|store|avoid|disconnect|recommended|reinigen|lagern|vermeiden|empfohlen|trennen)\b",
                RegexOptions.IgnoreCase
            )
        )
        {
            var advisory = evidence
                .Where(e =>
                    e.Kind == "Advisory"
                    && Normalize(e.Text).Contains(normalized, StringComparison.Ordinal)
                )
                .ToArray();
            if (advisory.Length > 0)
            {
                status = "Advisory";
                reason =
                    "Exact passage match in reference guidance; it does not establish a product specification.";
                linked.AddRange(advisory);
            }
        }
        return new(
            Id(field + ":" + text),
            field,
            text,
            status,
            reason,
            linked.DistinctBy(e => e.Id).ToArray()
        );
    }

    private static IEnumerable<string> Sentences(string text) =>
        Regex.Split(text, @"(?<=[.!?])\s+(?=[A-ZÄÖÜ])").Where(s => !string.IsNullOrWhiteSpace(s));

    private static string Normalize(string text) =>
        Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim().TrimEnd('.', '!', '?');

    private static IReadOnlyList<(string Value, string Unit)> Quantities(string text) =>
        Regex
            .Matches(
                text,
                @"(?<![\p{L}\d])(\d+(?:[.,]\d+)?)\s*(ml|kg|mm|cm|°c|tb|gb|mah|hz|lm|w|v|g|l)\b",
                RegexOptions.IgnoreCase
            )
            .Select(m =>
                (m.Groups[1].Value.Replace(',', '.'), m.Groups[2].Value.ToLowerInvariant())
            )
            .ToArray();

    private static EvidenceSnapshot Snapshot(string kind, string label, string text) =>
        new(Id(kind + ":" + label + ":" + text), kind, label, text);

    private static string Id(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..20].ToLowerInvariant();
}
