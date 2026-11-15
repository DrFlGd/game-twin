using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameTwinGuideLibrary.Core;

/// <summary>One actual IGN sidebar entry, not a recursively discovered website link.</summary>
public sealed record IgnWikiPage(string Title, string Url, string Section, int Depth)
{
    public string DisplayTitle => new string('·', Math.Min(Depth, 5)) + (Depth > 0 ? "  " : "") + Title;
}

public sealed record IgnWikiDirectory(
    string WikiSlug,
    string WikiTitle,
    string RootUrl,
    IReadOnlyList<IgnWikiPage> Pages)
{
    public IReadOnlyList<string> Sections =>
        Pages.Select(p => p.Section).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

/// <summary>
/// Interprets props.pageProps.page.navigation from IGN's __NEXT_DATA__.
/// IGN's sidebar is a hierarchy of { label, url, subnav } nodes. The URL field
/// is already a URL path segment (some nodes contain %27, etc).
/// No page-to-page crawling takes place.
/// </summary>
public static class IgnWikiDirectoryReader
{
    private static readonly Regex SafeSlug = new(@"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,119}$", RegexOptions.Compiled);

    public static bool TryGetWikiSlug(string? url, out string slug)
    {
        slug = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("www.ign.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("ign.com", StringComparison.OrdinalIgnoreCase)))
            return false;

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("wikis", StringComparison.OrdinalIgnoreCase))
            return false;
        var candidate = Uri.UnescapeDataString(parts[1]);
        if (!SafeSlug.IsMatch(candidate)) return false;
        slug = candidate.ToLowerInvariant();
        return true;
    }

    public static IgnWikiDirectory ParseNextData(string nextDataJson, string pageUrl)
    {
        if (!TryGetWikiSlug(pageUrl, out var fromUrl))
            throw new ArgumentException("Open an HTTPS ign.com/wikis/<game> page before discovering the sidebar.");

        using var document = JsonDocument.Parse(nextDataJson);
        var props = document.RootElement.GetProperty("props").GetProperty("pageProps").GetProperty("page");
        var wikiSlug = GetString(props, "wikiSlug") ?? fromUrl;
        if (!SafeSlug.IsMatch(wikiSlug))
            throw new FormatException("IGN supplied an invalid wiki slug.");

        // The script's canonical wikiSlug matters when the current URL is an alias.
        wikiSlug = wikiSlug.ToLowerInvariant();
        var root = "https://www.ign.com/wikis/" + wikiSlug;
        var title = GetString(props, "name") ?? GetString(props, "title") ?? wikiSlug;
        if (!props.TryGetProperty("navigation", out var navigation) ||
            navigation.ValueKind != JsonValueKind.Array)
            throw new FormatException("No structured navigation was found in IGN's __NEXT_DATA__. The wiki may use a different layout.");

        var pages = new List<IgnWikiPage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string label, string urlPart, string section, int depth)
        {
            string candidate;
            if (urlPart.Length == 0)
            {
                candidate = root;
            }
            else
            {
                // Resolve under the canonical wiki root and reject links outside that wiki.
                if (urlPart.StartsWith("/", StringComparison.Ordinal) ||
                    urlPart.Contains('\\') ||
                    urlPart.StartsWith("#", StringComparison.Ordinal)) return;
                if (!Uri.TryCreate(new Uri(root + "/"), urlPart, out var resolved) ||
                    resolved is null || resolved.Scheme != Uri.UriSchemeHttps ||
                    !resolved.Host.Equals("www.ign.com", StringComparison.OrdinalIgnoreCase))
                    return;
                var expectedPrefix = "/wikis/" + wikiSlug + "/";
                if (!resolved.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                    return;
                candidate = resolved.GetLeftPart(UriPartial.Path);
            }

            if (seen.Add(candidate))
                pages.Add(new IgnWikiPage(label, candidate, section, depth));
        }

        void Walk(JsonElement nodes, string section, int depth)
        {
            if (nodes.ValueKind != JsonValueKind.Array) return;
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object) continue;
                var label = GetString(node, "label")?.Trim();
                if (string.IsNullOrWhiteSpace(label)) continue;
                var currentSection = depth == 0 ? label : section;
                var url = GetString(node, "url") ?? "";
                Add(label, url, currentSection, depth);
                if (node.TryGetProperty("subnav", out var children))
                    Walk(children, currentSection, depth + 1);
            }
        }

        Walk(navigation, "", 0);
        if (seen.Add(root))
            pages.Insert(0, new IgnWikiPage(title + " (wiki home)", root, "Wiki home", 0));

        if (pages.Count == 0)
            throw new FormatException("IGN returned a navigation object without usable wiki URLs.");

        return new IgnWikiDirectory(wikiSlug, title, root, pages);
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString();
    }
}
