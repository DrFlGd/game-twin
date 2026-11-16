using GameTwinGuideLibrary.Core;

static void Check(bool ok, string description)
{
    if (!ok) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
}

var catalog = new CatalogService(Path.Combine(AppContext.BaseDirectory, "Catalogs"));
Check(catalog.Groups.Single(g => g.Key == "gamefaqs").Count == 2938, "GameFAQs: all bundled records");
Check(catalog.Groups.Single(g => g.Key == "strategywiki").Count == 1473, "StrategyWiki: all graded records");
Check(catalog.Groups.Single(g => g.Key == "vgmaps").Count == 3481, "VGMaps: all system records");
Check(catalog.Groups.Single(g => g.Key == "steam").Count == 46, "Steam: all curated guide records");
Check(catalog.Groups.Single(g => g.Key == "codewheels").Count == 106, "Code wheels: all ZIP records");
Check(catalog.Groups.Single(g => g.Key == "series").Count == 86, "Series: 75 guides + 11 separate maps");
Check(catalog.TotalCount == 8130, "Bundled directory: 8,130 usable URLs");
Check(catalog.Entries.All(e => Uri.TryCreate(e.Url, UriKind.Absolute, out var u) &&
                                   (u.Scheme == "https" || u.Scheme == "http")), "Every entry has a valid web URL");
Check(catalog.Browse("steam", "Celeste").Any(e => e.Title == "Celeste"), "Search finds Steam guide");
Check(catalog.Browse("series", "Metroid Dread").Count == 2, "Browse sees walkthrough and map separately");
Check(catalog.ForGame("Metroid Dread").Any(e => e.Source == "Omega Metroid"), "Selected-game discovery finds curated series");
Check(catalog.ForGame("Celeste").Any(e => e.Source == "Steam Community"), "Selected-game discovery finds Steam");
Check(catalog.ForGame("The Legend of Zelda: Ocarina of Time").Any(e =>
    e.Source == "Zelda Dungeon" && e.Title == "Ocarina of Time"), "Series guide matches franchise-prefixed game title");
Check(catalog.ShortcutsForGame("The Legend of Zelda: Ocarina of Time").Any(s =>
    s.Name == "Zelda Dungeon"), "Franchise-specific hub is offered as shortcut");
Check(GameNameMatcher.Score("God of War 2", "God of War 3") == 0, "Numbered sequel mismatch rejected");
Check(GameNameMatcher.IgnSlug("Pokémon: Red") == "pokemon-red", "IGN slug normalizes accents");
Check(CatalogService.SourceShortcuts("Cyberpunk 2077").Any(s =>
    s.Name == "IGN" && s.Url == "https://www.ign.com/wikis/cyberpunk-2077"), "IGN source shortcut");
Check(catalog.Browse("all", "___nonsense_no_match___").Count == 0, "Empty searches work");
Check(catalog.Browse("all", "", 200).Count == 200, "Catalog paging limit works");

var directory = Path.Combine(Path.GetTempPath(), "game-twin-smoke-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new GameLibraryStore(Path.Combine(directory, "games.json"));
    store.Save(["Elden Ring", "elden ring", "Cyberpunk 2077"]);
    Check(store.Load().Count == 2, "Game selection is saved and case-insensitively deduplicated");
}
finally
{
    if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
}



// Phase 2/3: sanitized representative IGN __NEXT_DATA__ fixture.
// No publisher page text is included in the repository.
var ignFixture = System.Text.Json.JsonSerializer.Serialize(new
{
    props = new
    {
        pageProps = new
        {
            page = new
            {
                wikiSlug = "cyberpunk-2077",
                name = "Cyberpunk 2077",
                navigation = new object[]
                {
                    new { label = "Cyberpunk 2077 Guide", url = "", subnav = Array.Empty<object>() },
                    new
                    {
                        label = "Walkthrough", url = "Walkthrough",
                        subnav = new object[]
                        {
                            new { label = "Main Job", url = "Main_Job", subnav = Array.Empty<object>() },
                            new { label = "I've Seen That Face", url = "I%27ve_Seen_That_Face", subnav = Array.Empty<object>() }
                        }
                    },
                    new { label = "Duplicate", url = "Main_Job", subnav = Array.Empty<object>() },
                    new { label = "Invalid", url = "../../../outside", subnav = Array.Empty<object>() }
                }
            }
        }
    }
});
var ignDirectory = IgnWikiDirectoryReader.ParseNextData(
    ignFixture, "https://www.ign.com/wikis/cyberpunk-2077");
Check(ignDirectory.Pages.Count == 4, "IGN sidebar: landing page, nested pages, dedup, unsafe URL filtered");
Check(ignDirectory.Pages[0].Url == "https://www.ign.com/wikis/cyberpunk-2077",
    "IGN sidebar: empty URL resolves to wiki homepage");
Check(ignDirectory.Pages.Single(p => p.Title == "Main Job").Depth == 1,
    "IGN sidebar: hierarchy is preserved");
Check(ignDirectory.Pages.Single(p => p.Title == "Main Job").Section == "Walkthrough",
    "IGN sidebar: category groups are preserved");
Check(ignDirectory.Pages.Any(p => p.Url.EndsWith("I%27ve_Seen_That_Face")),
    "IGN sidebar: encoded apostrophe is preserved");
Check(!IgnWikiDirectoryReader.TryGetWikiSlug(
    "https://evilign.com/wikis/cyberpunk-2077", out _), "IGN URLs: reject other domains");
Check(!IgnWikiDirectoryReader.TryGetWikiSlug(
    "https://www.ign.com/games/cyberpunk-2077", out _), "IGN URLs: reject nonwiki pages");

var archiveTestRoot = Path.Combine(Path.GetTempPath(), "game-twin-ign-" + Guid.NewGuid().ToString("N"));
try
{
    var archives = new IgnArchiveStore(archiveTestRoot);
    var samplePage = ignDirectory.Pages.Single(p => p.Title == "Main Job");
    var sampleMhtml = "MIME-Version: 1.0\r\n" +
        "Content-Type: multipart/related; boundary=archive\r\n" +
        "From: <Saved by Blink>\r\n\r\n--archive\r\n" +
        "Content-Type: text/html\r\n\r\n" +
        "<html><head><title>Test article</title></head>" +
        "<body><p>Offline content fixture - not a publisher article.</p></body></html>\r\n--archive--";
    var saved = await archives.SaveSnapshotAsync(
        "cyberpunk-2077", "Cyberpunk 2077", samplePage.Url, samplePage.Title,
        sampleMhtml, 3, 1);
    Check(saved.Status == "Saved" && saved.SizeBytes > 128,
        "IGN archive: MHTML file saved on disk");
    Check(archives.IsSaved(samplePage.Url), "IGN archive: saved page can be resumed/skipped");
    Check(archives.FindSavedFile(samplePage.Url) is { } path && File.Exists(path),
        "IGN archive: URL resolves to local offline file");
    Check(archives.ListPages().Count == 1 &&
          archives.ListPages()[0].Page.MissingImages == 1,
        "IGN archive: metadata and image warnings persisted");
    archives.RecordFailure("cyberpunk-2077", "Cyberpunk 2077",
        "Main Job", samplePage.Url, "Network interrupted");
    Check(archives.IsSaved(samplePage.Url),
        "IGN archive: a later failed attempt cannot overwrite a saved page");
    archives.RecordFailure("cyberpunk-2077", "Cyberpunk 2077",
        "Bad page", ignDirectory.Pages.Last().Url, "HTTP failure");
    Check(archives.FailureCount() == 1,
        "IGN archive: failed pages are recorded for retry");
}
finally
{
    if (Directory.Exists(archiveTestRoot)) Directory.Delete(archiveTestRoot, recursive: true);
}

Console.WriteLine("All Phase 1–3 catalog and IGN archive smoke checks passed.");



// Phase 4: offline IGN reader script validation.
// Reader mode runs on saved pages with publisher JavaScript disabled.
var readerTemplate = File.ReadAllText(Path.Combine(
    AppContext.BaseDirectory, "Resources", "ign_reader.js"));
Check(readerTemplate.Contains("document.querySelector(\".wiki-html\")") &&
      readerTemplate.Contains("[data-cy=\"page-header\"]") &&
      readerTemplate.Contains(".wiki-bobble") &&
      readerTemplate.Contains(".adunit-wrapper"),
    "IGN reader: original Game Twin content and chrome selectors are represented");

foreach (var action in new[] {
    IgnReaderAction.Apply, IgnReaderAction.Restore, IgnReaderAction.Isolate })
{
    var script = IgnReaderScript.Build(readerTemplate, action,
        "https://www.ign.com/wikis/cyberpunk-2077/Walkthrough", "A \"test\" guide");
    Check(!script.Contains("__GG_ACTION__") &&
          !script.Contains("__GG_SOURCE_URL__") &&
          !script.Contains("__GG_ARTICLE_TITLE__") &&
          script.Contains(System.Text.Json.JsonSerializer.Serialize("A \"test\" guide")),
        "IGN reader: " + action + " script parameters are safely quoted");
}
Check(IgnReaderScript.Build(readerTemplate, IgnReaderAction.Apply, null, "")
        .Contains("const action = \"apply\""),
    "IGN reader: apply command is correctly selected");
Check(IgnReaderScript.Build(readerTemplate, IgnReaderAction.Restore, null, "")
        .Contains("const action = \"restore\""),
    "IGN reader: original layout can be restored");
Check(IgnReaderScript.Build(readerTemplate, IgnReaderAction.Isolate,
        "https://www.ign.com/wikis/cyberpunk-2077", "Test")
        .Contains("const action = \"isolate\""),
    "IGN reader: clean export isolates the document");
var rejectedExternal = false;
try
{
    IgnReaderScript.Build(readerTemplate, IgnReaderAction.Apply,
        "https://evil.example/wikis/cyberpunk-2077", "Unsafe");
}
catch (ArgumentException) { rejectedExternal = true; }
Check(rejectedExternal, "IGN reader: source URL validation rejects non-IGN domains");
var markerLikeTitle = "__GG_SOURCE_URL__ and __GG_ARTICLE_TITLE__";
var escapedMarkerTitle = IgnReaderScript.Build(readerTemplate, IgnReaderAction.Apply,
    "https://www.ign.com/wikis/cyberpunk-2077", markerLikeTitle);
Check(escapedMarkerTitle.Contains(System.Text.Json.JsonSerializer.Serialize(markerLikeTitle)),
    "IGN reader: template-like article titles remain harmless literal data");
Check(readerTemplate.Contains("state.modified[i]") &&
      readerTemplate.Contains("delete window[STATE_KEY]"),
    "IGN reader: original inline styles are recoverable without an archive rewrite");
Check(readerTemplate.Contains("cloneNode(true)") &&
      readerTemplate.Contains("document.body.replaceChildren(main)"),
    "IGN clean export: isolated DOM retains article elements and images");
Console.WriteLine("IGN reader and cleaned-export script smoke checks passed.");
