# Game Guide Library for Windows — Phase 1 + IGN archiving and reader

A standalone Windows guide browser and local archive application. The Android project is unchanged.

## Feature coverage

### Phase 1 — browse sources

- Add/select/remove game names; saved in %LOCALAPPDATA%\GameTwinGuideLibrary\games.json.
- Browse Game Twin's **8,130 index records**: GameFAQs (2,938), StrategyWiki (1,473), VGMaps (3,481), Steam (46), curated series (86), Internet Archive code-wheel ZIPs (106).
- Search by source and title, inspect original URLs and browse them in Edge WebView2.
- Open IGN, GameFAQs, Neoseeker, Steam, and game-manual shortcuts.

The Android catalogs contain only **titles and links, not guide content**.

### Phase 2 — save an IGN article

1. Open an IGN wiki page in the built-in browser, for example https://www.ign.com/wikis/cyberpunk-2077.
2. Complete any normal publisher consent flow if it appears.
3. Choose **Save IGN page** above the browser.
4. Open **Saved IGN → Read offline** (clean reader mode is now the default).

The app scrolls to trigger lazy image loading, waits for image completion,
and uses Chromium DevTools Page.captureSnapshot to create one MHTML
archive. It records the page URL, title, file size and best-effort image
diagnostics.

### Phase 3 — archive selected IGN sidebar pages

1. Open an IGN wiki page, preferably its landing page.
2. Click **Discover IGN sidebar**.
3. Use the **IGN sidebar** tab to browse its hierarchical page list,
   filter by category, search article names, and choose Select all,
   Select visible, or individual checkboxes.
4. Choose **Archive selected IGN pages**. Large batches require confirmation.
5. Monitor progress; press Cancel to stop after the current operation.
6. Open the **Saved IGN** tab to view completed articles, image warnings,
   original sources, and saved files.

**Page discovery is not recursive.** The app reads IGN's embedded
Next.js JSON field props.pageProps.page.navigation. In a Cyberpunk 2077
HTML sample it represented roughly 600 distinct wiki page URLs including
the landing page. IGN can change this format and other wikis may differ.

**Resume/retry:** Each wiki has its own manifest.json. Successful pages
are skipped on repeat runs, failed pages are recorded, and cancellation
keeps completed work. Retry by selecting failed pages again. Visits are
sequential with a short delay; no CAPTCHA or access bypass is attempted.

**Offline mode:** Saved .mhtml files open in WebView2. The app disables
scripts and blocks HTTP(S) requests while reading offline. Links to
other saved IGN articles redirect to their local MHTML copies; unsaved
links are blocked. Use Open source to return online. Standalone MHTML
files can always be inspected with Show in folder.

## IGN reading mode and clean export

With a saved IGN archive open, **Reader: On** displays only the guide article,
title and byline. Its logic is adapted from the original Game Twin IGN
ReadingModeFilters.kt: it keeps .wiki-html and [data-cy=page-header], hides
the site header, wiki sidebar, ad wrappers, embeds, breadcrumb and share
controls, and expands the article for a centered reading experience.

Choose **Reader: Off** to restore the original IGN layout without changing
any archived file. The filter changes only the viewer's live DOM. The
original MHTML remains byte-for-byte unchanged. Offline webpage scripts
are disabled and HTTP(S) requests remain blocked; only the host-injected
display script runs. If the article element is absent, the view is left
unchanged instead of hiding the page.

**Export clean MHTML** creates a *separate* .reader.mhtml file. It clones
the article and page header, removes site chrome, scripts, ads and embeds
from a temporary DOM, includes source attribution, and asks Chromium to
recapture the cleaned page as standalone MHTML with whatever resources
are available from the original archive. The original .mhtml is never
overwritten, and the viewer is reloaded after export.

Clean export is MHTML, **not an HTML+assets-folder conversion**. Images
or styles missing from the original capture cannot be reconstructed.
Test representative offline articles before a large export.

Implementation: windows/GameTwinGuideLibrary/Resources/ign_reader.js.
After IGN site changes, the selectors may need updates.
## Storage

Default archive location:

    %USERPROFILE%\Documents\Game Guide Library\IGN\<wiki-slug>\

Every wiki directory has a manifest.json and one .mhtml per successfully
captured page. SHA-256 URL hashes avoid filename collisions. The files
can be copied or backed up independently of the app.

**Limit:** MHTML preserves loaded HTML, images, styles and other
resources. Lazy images, third-party CDN restrictions, protected pages
and script-dependent layouts may result in missing content. Image
diagnostics indicate loading problems, not a guarantee that every
resource was embedded.

## Building on Windows

Requires Windows 10/11 x64, .NET 10 SDK to build, and Microsoft
Edge WebView2 Runtime (typically already installed).

Run from the repo root:

    dotnet run --project windows/GameTwinGuideLibrary/GameTwinGuideLibrary.csproj

Build a self-contained win-x64 release:

    dotnet publish windows/GameTwinGuideLibrary/GameTwinGuideLibrary.csproj -c Release -r win-x64 --self-contained true -o artifacts/GameTwinGuideLibrary

Run artifacts/GameTwinGuideLibrary/GameTwinGuideLibrary.exe, or
download GameTwinGuideLibrary-win-x64 from a successful GitHub Actions
run for this branch.

## Tests

    python windows/tools/generate_series_catalog.py --check
    dotnet run --project windows/GameTwinGuideLibrary.SmokeTests/GameTwinGuideLibrary.SmokeTests.csproj

Smoke tests cover catalog totals, game matching, representative
IGN Next.js navigation parsing, deduplication, malicious-domain and
path rejection, and local manifest and MHTML persistence.
Windows CI checks reader JavaScript syntax, compiles WPF/WebView2 and publishes a build.

**Live IGN archiving and offline image completeness must still be
tested on Windows against real source pages.**

## Project structure

- GameTwinGuideLibrary: WPF browser, IGN sidebar page selector, image
  preloading and MHTML batch archiver.
- GameTwinGuideLibrary.Core: resource catalogs, IGN directory parser,
  local MHTML archive manifest and saved game-name store.
- GameTwinGuideLibrary.SmokeTests: portable fixture-based tests.
- tools/generate_series_catalog.py: export the curated Kotlin guide URLs.

## Boundaries

- Only IGN wiki pages can be archived; other guide sources are browse-only.
- This visits listed sidebar articles, not every URL in the IGN domain.
- A publisher may deny pages, require consent, rate-limit, or change formats.
- Start with one section before attempting hundreds of pages.
- No background crawling, game integrations or cloud sync.
- Existing GPL-3.0-or-later source licensing and third-party copyright apply;
  check IGN's terms and do not redistribute archives without permission.
