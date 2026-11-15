using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GameTwinGuideLibrary.Core;

public static partial class GameNameMatcher
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "and", "of", "to", "in", "for", "with", "version",
        "edition", "remastered", "remaster", "deluxe", "ultimate", "definitive"
    };

    public static string Normalize(string value)
    {
        var decomposed = (value ?? "").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    public static string IgnSlug(string gameName)
    {
        var slug = Regex.Replace(Normalize(gameName), @"[^a-z0-9]+", "-").Trim('-');
        return slug;
    }

    private static HashSet<string> Tokens(string text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !StopWords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Catalog matching is advisory; the source's own search remains available.</summary>
    public static double Score(string gameName, string title)
    {
        var requested = Tokens(gameName);
        var candidate = Tokens(title);
        if (requested.Count == 0 || candidate.Count == 0) return 0;

        // Never claim a numbered sequel is the same guide as a different one.
        var leftNumbers = requested.Where(t => t.All(char.IsDigit)).OrderBy(t => t).ToArray();
        var rightNumbers = candidate.Where(t => t.All(char.IsDigit)).OrderBy(t => t).ToArray();
        if (!leftNumbers.SequenceEqual(rightNumbers)) return 0;

        var shared = requested.Count(candidate.Contains);
        if (shared == 0) return 0;
        var jaccard = shared / (double)requested.Union(candidate).Count();
        var coverage = shared / (double)requested.Count;
        var exact = requested.SetEquals(candidate);
        if (exact) return 1;
        if (jaccard >= 0.60) return jaccard;
        // Series guides often omit the franchise prefix in their displayed title,
        // e.g. "The Legend of Zelda: Ocarina of Time" vs "Ocarina of Time".
        if (shared >= 2 && shared == candidate.Count && shared / (double)requested.Count >= 0.45)
            return 0.60;
        if (requested.Count >= 2 && coverage == 1 && shared / (double)candidate.Count >= 0.55)
            return 0.60;
        return 0;
    }

    public static bool ContainsWords(string candidate, string search)
    {
        var normalizedQuery = Normalize(search);
        return normalizedQuery.Length == 0 || Normalize(candidate).Contains(normalizedQuery, StringComparison.Ordinal);
    }
}
