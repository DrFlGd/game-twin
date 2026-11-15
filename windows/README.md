# Game Guide Library for Windows — Phase 1

A standalone Windows frontend for selecting games, browsing Game Twin's bundled directory of walkthroughs, maps and other guide resources, and reading the original websites in an embedded browser. It does **not** require a running game, an emulator, or any Android software.

## What works

- Add, select and remove game titles. Titles are stored in %LOCALAPPDATA%\GameTwinGuideLibrary\games.json.
- Discover matching entries from the bundled GameFAQs, StrategyWiki, Steam, VGMaps and curated-series catalogs.
- Open quick shortcuts for IGN, GameFAQs, Neoseeker, Steam guides and PDF game-manual searches.
- **Browse all 8,130 indexed resources** independently of any selected game, with source filters, keyword search, record counts and 200-record pages.
- Open any original resource URL in an embedded Microsoft Edge WebView2 browser, with Back, Forward, Refresh, address bar and Open Externally.
- Browse the 106 Game Twin code-wheel entries, each linked to its original Internet Archive ZIP download.

### What's actually bundled?

The original Android project's JSON assets contain **catalog/index entries, not the guide text or images**. This Windows project reuses those original JSON files at build time:

| Catalog | Indexed resources |
| --- | ---: |
| GameFAQs | 2,938 |
| StrategyWiki | 1,473 |
| VGMaps | 3,481 |
| Steam Community | 46 |
| Code wheels | 106 |
| Curated series | 75 walkthroughs + 11 map links |
| **Total** | **8,130** |

The curated-series JSON under GameTwinGuideLibrary.Core/Catalogs is generated from the original app/src/main/java/com/twinlore/app/data/GuideSeriesCatalog.kt. To refresh it after the Android source changes:

    python windows/tools/generate_series_catalog.py

### Not implemented yet

**Offline saving is not part of Phase 1.** The guide browser opens the original pages online, and a website's normal browser download links may download files. There is no Save page, MHTML archiver, Save all chapters operation, offline reader or locally downloaded guide collection yet. Those belong to Phases 2–4. The only locally stored user data in Phase 1 is the game-name list.

A catalog record is not a guarantee that the publisher's URL remains live. IGN links are generated from game-title slugs and may need correcting via site search. Game-match suggestions are advisory and may miss alternate regional titles.

## Build on Windows

Requirements:
- Windows 10/11 x64.
- .NET 8 SDK (for building); .NET 8 Desktop Runtime if running a framework-dependent build.
- Microsoft Edge WebView2 Runtime (usually installed on supported Windows systems).

From the repository root:

    dotnet run --project windows/GameTwinGuideLibrary/GameTwinGuideLibrary.csproj

Or create a portable, self-contained Windows release folder:

    dotnet publish windows/GameTwinGuideLibrary/GameTwinGuideLibrary.csproj -c Release -r win-x64 --self-contained true -o artifacts/GameTwinGuideLibrary

Run artifacts/GameTwinGuideLibrary/GameTwinGuideLibrary.exe. The WebView2 runtime is still required. You can also download the Windows build artifact from a successful GitHub Actions workflow run.

## Tests

The smoke tests verify that all catalogs load, that record counts and original URLs are correct, matching and searching work, and the game-name store persists:

    python windows/tools/generate_series_catalog.py --check
    dotnet run --project windows/GameTwinGuideLibrary.SmokeTests/GameTwinGuideLibrary.SmokeTests.csproj

The desktop interface itself and live third-party website behavior require Windows/WebView2 and are not covered by these tests.

## Layout

- GameTwinGuideLibrary — WPF desktop UI and embedded WebView2 browser.
- GameTwinGuideLibrary.Core — dependency-free .NET catalog loader, search, matching, and game-list store.
- GameTwinGuideLibrary.SmokeTests — cross-platform catalog integrity checks.
- tools/generate_series_catalog.py — deterministic export of curated Kotlin links.

The Android app under app/ is unchanged.

## Licensing and source attribution

This remains a fork of an open-source GPL-3.0-or-later project. The Windows program uses new Game Guide Library branding and no reserved Game Twin logos or edition artwork. Every entry is attributed to its original site and linked back there. Guide contents and images remain the property of their rights holders; this frontend does not redistribute copies.
