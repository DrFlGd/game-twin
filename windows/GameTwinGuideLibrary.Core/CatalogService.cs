using System.Text.Json;

namespace GameTwinGuideLibrary.Core;

/// <summary>
/// Reads the exact index assets shipped in the upstream Android application.
/// No website is scraped at startup, and no third-party guide text is bundled.
/// </summary>
public sealed class CatalogService
{
    private readonly List<GuideResource> _entries = [];
    public IReadOnlyList<GuideResource> Entries => _entries;
    public IReadOnlyList<CatalogGroup> Groups { get; }
    public int TotalCount => _entries.Count;

    public CatalogService(string catalogDirectory)
    {
        if (!Directory.Exists(catalogDirectory))
            throw new DirectoryNotFoundException($"Catalog directory not found: {catalogDirectory}");

        LoadGameFaqs(Path.Combine(catalogDirectory, "gamefaqs_catalog.json"));
        LoadStrategyWiki(Path.Combine(catalogDirectory, "strategywiki_catalog.json"));
        LoadSteam(Path.Combine(catalogDirectory, "steam_guides_catalog.json"));
        LoadVgmaps(Path.Combine(catalogDirectory, "vgmaps_catalog.json"));
        LoadCodeWheels(Path.Combine(catalogDirectory, "code_wheels.json"));
        LoadSeries(Path.Combine(catalogDirectory, "series_catalog.json"));

        Groups =
        [
            Group("all", "All resources", "Browse every locally indexed source"),
            Group("gamefaqs", "GameFAQs", "Curated FAQ and walkthrough URLs"),
            Group("strategywiki", "StrategyWiki", "Walkthroughs graded Thorough or better"),
            Group("steam", "Steam Community", "Curated community guide links"),
            Group("vgmaps", "VGMaps", "Map-directory entries by platform"),
            Group("series", "Series guides", "Omega Metroid, Zelda Dungeon, Bulbapedia and Fire Emblem WoD"),
            Group("codewheels", "Code wheels", "Archive.org downloadable code-wheel ZIP listings")
        ];
    }

    private CatalogGroup Group(string key, string name, string description) =>
        new(key, name, description, key == "all" ? _entries.Count : _entries.Count(e => e.CatalogKey == key));

    private static JsonDocument Read(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"A Game Twin catalog file is missing: {path}", path);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string Value(JsonElement element, string key) =>
        element.TryGetProperty(key, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString() : "";

    private void LoadGameFaqs(string path)
    {
        using var doc = Read(path);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var title = Value(item, "t");
            var platform = Value(item, "p");
            var filePath = Value(item, "u");
            if (string.IsNullOrWhiteSpace(filePath)) continue;
            _entries.Add(new("gamefaqs", "GameFAQs", title, "Guide", platform,
                "https://gamefaqs.gamespot.com/" + filePath.TrimStart('/')));
        }
    }

    private void LoadStrategyWiki(string path)
    {
        using var doc = Read(path);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var stage = Value(item, "s");
            // Mirror upstream StrategyWikiCatalog.MIN_STAGE.
            if (!int.TryParse(stage, out var level) || level < 3) continue;
            var pathPart = Value(item, "a");
            var grade = level switch { 5 => "Featured", 4 => "Comprehensive", _ => "Thorough" };
            _entries.Add(new("strategywiki", "StrategyWiki", Value(item, "t"), "Walkthrough", grade,
                "https://strategywiki.org/wiki/" + pathPart + "/Walkthrough"));
        }
    }

    private void LoadSteam(string path)
    {
        using var doc = Read(path);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var id = Value(item, "guideId");
            if (id.Length == 0) continue;
            _entries.Add(new("steam", "Steam Community", Value(item, "title"), "Guide",
                Value(item, "appId") is { Length: > 0 } appId ? "App ID " + appId : "",
                "https://steamcommunity.com/sharedfiles/filedetails/?id=" + Uri.EscapeDataString(id)));
        }
    }

    private void LoadVgmaps(string path)
    {
        using var doc = Read(path);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var platform = Value(item, "s");
            var anchor = Value(item, "a");
            _entries.Add(new("vgmaps", "VGMaps", Value(item, "t"), "Map directory", platform,
                "https://www.vgmaps.com/Atlas/" + platform + "/index.htm#" + anchor));
        }
    }

    private void LoadCodeWheels(string path)
    {
        using var doc = Read(path);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var filename = Value(item, "zipFilename");
            _entries.Add(new("codewheels", "Internet Archive", Value(item, "title"), "ZIP download",
                "Code wheel", "https://archive.org/download/code-wheels/" + Uri.EscapeDataString(filename)));
        }
    }

    private void LoadSeries(string path)
    {
        using var doc = Read(path);
        foreach (var group in doc.RootElement.EnumerateArray())
        {
            var source = Value(group, "name");
            foreach (var item in group.GetProperty("entries").EnumerateArray())
            {
                var name = Value(item, "title");
                var url = Value(item, "url");
                _entries.Add(new("series", source, name, "Walkthrough", "Curated series link", url));
                var mapUrl = Value(item, "mapUrl");
                if (mapUrl.Length > 0)
                    _entries.Add(new("series", source, name, "Map", "Curated series map link", mapUrl));
            }
        }
    }

    public IReadOnlyList<GuideResource> Browse(string catalogKey = "all", string search = "", int limit = 400)
    {
        IEnumerable<GuideResource> results = _entries;
        if (catalogKey != "all")
            results = results.Where(entry => entry.CatalogKey == catalogKey);
        if (!string.IsNullOrWhiteSpace(search))
            results = results.Where(entry =>
                GameNameMatcher.ContainsWords(entry.Title, search) ||
                GameNameMatcher.ContainsWords(entry.Source, search) ||
                GameNameMatcher.ContainsWords(entry.Detail, search) ||
                GameNameMatcher.ContainsWords(entry.Kind, search));
        return results.Take(limit).ToArray();
    }

    public int BrowseCount(string catalogKey = "all", string search = "")
    {
        IEnumerable<GuideResource> results = _entries;
        if (catalogKey != "all") results = results.Where(e => e.CatalogKey == catalogKey);
        if (!string.IsNullOrWhiteSpace(search))
            results = results.Where(e =>
                GameNameMatcher.ContainsWords(e.Title, search) ||
                GameNameMatcher.ContainsWords(e.Source, search) ||
                GameNameMatcher.ContainsWords(e.Detail, search) ||
                GameNameMatcher.ContainsWords(e.Kind, search));
        return results.Count();
    }

    public IReadOnlyList<GuideResource> ForGame(string gameName, int perSource = 8) =>
        _entries
            .Select(entry => new MatchedGuide(entry, GameNameMatcher.Score(gameName, entry.Title)))
            .Where(match => match.Score >= 0.60)
            .GroupBy(match => match.Resource.Source)
            .SelectMany(group => group.OrderByDescending(match => match.Score)
                .ThenBy(match => match.Resource.Title).Take(perSource))
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Resource.Source)
            .Select(match => match.Resource)
            .ToArray();

    public static IReadOnlyList<SourceShortcut> SourceShortcuts(string gameName)
    {
        var game = gameName.Trim();
        if (game.Length == 0) return [];
        var encoded = Uri.EscapeDataString(game);
        var slug = GameNameMatcher.IgnSlug(game);
        var ignUrl = slug.Length > 0 ? "https://www.ign.com/wikis/" + slug : "https://www.ign.com/wikis";
        return
        [
            new("IGN", "Predicted wiki URL (may not exist)", ignUrl),
            new("GameFAQs", "Search GameFAQs for this game",
                "https://gamefaqs.gamespot.com/search?game=" + encoded),
            new("Neoseeker", "Search the web for this game's Neoseeker guides",
                "https://www.google.com/search?q=" + Uri.EscapeDataString(game + " walkthrough site:neoseeker.com")),
            new("Steam Community", "Search Steam walkthroughs",
                "https://www.google.com/search?q=" + Uri.EscapeDataString(game + " guide site:steamcommunity.com")),
            new("Game manual", "Search for PDF manuals",
                "https://www.google.com/search?q=" + Uri.EscapeDataString(game + " game manual filetype:pdf"))
        ];
    }
}
