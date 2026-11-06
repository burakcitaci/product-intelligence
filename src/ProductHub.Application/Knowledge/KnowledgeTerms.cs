using System.Text.RegularExpressions;

namespace ProductHub.Application.Knowledge;

/// <summary>Versioned, deliberately small bilingual vocabulary. Co-mentions are not semantic facts.</summary>
public static class KnowledgeTerms
{
    public const string Version = "terms-v1";
    private static readonly Dictionary<string, string> Canonical = new(StringComparer.Ordinal)
    {
        ["reinigung"] = "cleaning",
        ["reinigen"] = "cleaning",
        ["clean"] = "cleaning",
        ["wartung"] = "maintenance",
        ["pflege"] = "maintenance",
        ["lagerung"] = "storage",
        ["lagern"] = "storage",
        ["kapazität"] = "capacity",
        ["volumen"] = "capacity",
        ["edelstahl"] = "stainless",
        ["stahl"] = "steel",
        ["flasche"] = "bottle",
        ["flaschen"] = "bottle",
        ["spannung"] = "voltage",
        ["leistung"] = "power",
        ["kompatibilität"] = "compatibility",
        ["kompatibel"] = "compatible",
        ["sicherheit"] = "safety",
        ["gewicht"] = "weight",
        ["bohnenbehälter"] = "hopper",
        ["mahlwerk"] = "grinder",
    };

    public static string[] Tokenize(string text) =>
        Regex
            .Matches(
                text.ToLowerInvariant(),
                @"[\p{L}\p{N}]+(?:[-./][\p{L}\p{N}]+)*",
                RegexOptions.CultureInvariant
            )
            .Select(m => m.Value)
            .Where(t => t.Length > 1)
            .ToArray();

    public static string ConceptType(string term) =>
        term switch
        {
            "cleaning" or "maintenance" or "storage" or "safety" => "Guidance",
            "voltage" or "power" or "weight" or "capacity" => "Attribute",
            "steel" or "stainless" => "Material",
            "bottle" or "hopper" or "grinder" => "ProductTerm",
            _ => "CoMention",
        };

    public static string Normalize(string term) => Canonical.GetValueOrDefault(term, term);

    public static string[] LexicalQueries(string text)
    {
        var tokens = Tokenize(text).Distinct().Take(12).ToArray();
        var canonical = tokens.Select(Normalize).ToHashSet();
        return tokens
            .Concat(canonical)
            .Concat(Canonical.Where(p => canonical.Contains(p.Value)).Select(p => p.Key))
            .Distinct()
            .Take(24)
            .ToArray();
    }
}
