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

Console.WriteLine("All Phase 1 catalog smoke checks passed.");
