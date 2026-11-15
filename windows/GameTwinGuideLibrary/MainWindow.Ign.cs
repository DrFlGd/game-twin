using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameTwinGuideLibrary.Core;
using Microsoft.Web.WebView2.Core;

namespace GameTwinGuideLibrary;

public sealed class IgnPageChoice(IgnWikiPage page) : INotifyPropertyChanged
{
    private bool _selected;
    public IgnWikiPage Page { get; } = page;
    public string DisplayTitle => Page.DisplayTitle;
    public string Section => Page.Section;
    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class MainWindow
{
    private const string AllIgnSections = "All sections";
    private readonly IgnArchiveStore _ignStore = new(IgnArchiveStore.DefaultDirectory);
    private readonly List<IgnPageChoice> _ignChoices = [];
    private List<IgnPageChoice> _ignVisible = [];
    private IgnWikiDirectory? _activeIgnWiki;
    private CancellationTokenSource? _ignCancellation;
    private bool _archiveBusy;
    private bool _offlineReading;

    private void InitializeIgnUi()
    {
        IgnSectionCombo.ItemsSource = new[] { AllIgnSections };
        IgnSectionCombo.SelectedIndex = 0;
        IgnArchiveProgressLabel.Text = "Open an IGN wiki in the browser to get started.";
        RefreshIgnSavedList();
    }

    private void InitializeOfflineBrowserEvents()
    {
        var core = GuideBrowser.CoreWebView2;
        core.NavigationStarting += HandleOfflineNavigationStarting;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            if (!_offlineReading) return;
            if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri)) return;
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return;

            // In offline mode, block all network requests. Embedded MHTML
            // resources still load, and links only open local saved pages.
            args.Response = core.Environment.CreateWebResourceResponse(
                new MemoryStream(), 403, "Offline mode", "Content-Type: text/plain");
        };
    }

    private void HandleOfflineNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!_offlineReading || !Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri))
            return;
        if (uri.IsFile)
        {
            // Only permit the app's own saved archive files during offline reading.
            var fullPath = Path.GetFullPath(uri.LocalPath);
            var root = Path.GetFullPath(_ignStore.RootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                !fullPath.EndsWith(".mhtml", StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
                BrowserStatusText.Text = "Blocked navigation outside saved IGN archives.";
            }
            return;
        }

        args.Cancel = true;
        if (IgnWikiDirectoryReader.TryGetWikiSlug(args.Uri, out _))
        {
            var file = _ignStore.FindSavedFile(uri.GetLeftPart(UriPartial.Path));
            if (file is not null)
            {
                var archiveUrl = new Uri(file).AbsoluteUri;
                Dispatcher.BeginInvoke(() => GuideBrowser.CoreWebView2.Navigate(archiveUrl));
                return;
            }
            BrowserStatusText.Text = "This linked IGN page was not saved. Open the original source to view it online.";
        }
        else BrowserStatusText.Text = "External navigation blocked while reading offline.";
    }

    private void SetOfflineReading(bool enabled)
    {
        _offlineReading = enabled;
        if (GuideBrowser.CoreWebView2 is not null)
            GuideBrowser.CoreWebView2.Settings.IsScriptEnabled = !enabled;
    }

    private async void IgnDiscover_Click(object sender, RoutedEventArgs e)
    {
        if (_archiveBusy || !_browserReady) return;
        var url = GuideBrowser.CoreWebView2.Source;
        if (!IgnWikiDirectoryReader.TryGetWikiSlug(url, out _))
        {
            ShowIgnMessage("Open an IGN wiki page first, such as https://www.ign.com/wikis/cyberpunk-2077.");
            return;
        }
        try
        {
            // Unlike the Android app's sidebar label-to-anchor join, the full
            // Next.js navigation supplies a URL for every sidebar article.
            var result = await GuideBrowser.ExecuteScriptAsync(
                "document.getElementById('__NEXT_DATA__')?.textContent ?? null");
            var nextData = JsonSerializer.Deserialize<string>(result);
            if (string.IsNullOrWhiteSpace(nextData))
                throw new InvalidDataException("The page has no IGN __NEXT_DATA__ navigation. It may be a different IGN layout.");
            _activeIgnWiki = IgnWikiDirectoryReader.ParseNextData(nextData, url);

            _ignChoices.Clear();
            foreach (var entry in _activeIgnWiki.Pages)
            {
                var choice = new IgnPageChoice(entry);
                choice.PropertyChanged += (_, _) => UpdateIgnSelectionCount();
                _ignChoices.Add(choice);
            }

            IgnSectionCombo.ItemsSource = new[] { AllIgnSections }.Concat(_activeIgnWiki.Sections).ToArray();
            IgnSectionCombo.SelectedIndex = 0;
            IgnPageSearchBox.Clear();
            FilterIgnPages();
            IgnDirectorySummary.Text = $"{_activeIgnWiki.WikiTitle} · {_ignChoices.Count:N0} distinct sidebar pages · " +
                $"{_activeIgnWiki.Sections.Count} categories. Select all or filter to individual sections.";
            DiscoveryTabs.SelectedIndex = 2;
            IgnArchiveProgressLabel.Text = "Directory loaded from IGN's embedded navigation; no recursive crawling.";
        }
        catch (Exception ex)
        {
            ShowIgnMessage("Could not extract IGN navigation: " + ex.Message);
        }
    }

    private void FilterIgnPages()
    {
        var query = IgnPageSearchBox.Text;
        var section = IgnSectionCombo.SelectedItem as string ?? AllIgnSections;
        _ignVisible = _ignChoices.Where(choice =>
                (section == AllIgnSections ||
                 choice.Page.Section.Equals(section, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(query) ||
                 GameNameMatcher.ContainsWords(choice.Page.Title, query) ||
                 GameNameMatcher.ContainsWords(choice.Page.Section, query)))
            .ToList();
        IgnPagesList.ItemsSource = _ignVisible;
        UpdateIgnSelectionCount();
    }

    private void UpdateIgnSelectionCount()
    {
        if (IgnArchiveProgressLabel is null || _archiveBusy) return;
        var selected = _ignChoices.Count(c => c.IsSelected);
        if (_activeIgnWiki != null)
            IgnArchiveProgressLabel.Text = $"{selected:N0} selected · {_ignVisible.Count:N0} shown · " +
                $"{_ignChoices.Count:N0} total pages (one archive per page).";
    }

    private void IgnPageSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        FilterIgnPages();

    private void IgnSectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IgnPageSearchBox is not null) FilterIgnPages();
    }

    private void IgnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_archiveBusy) return;
        foreach (var page in _ignChoices) page.IsSelected = true;
    }

    private void IgnSelectVisible_Click(object sender, RoutedEventArgs e)
    {
        if (_archiveBusy) return;
        foreach (var page in _ignVisible) page.IsSelected = true;
    }

    private void IgnClearSelection_Click(object sender, RoutedEventArgs e)
    {
        if (_archiveBusy) return;
        foreach (var page in _ignChoices) page.IsSelected = false;
    }

    private void IgnPagesList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_archiveBusy) return;
        if (IgnPagesList.SelectedItem is IgnPageChoice page)
        {
            SetOfflineReading(false);
            Navigate(page.Page.Url);
        }
    }

    private async void IgnSaveCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_archiveBusy || !_browserReady) return;
        var url = GuideBrowser.CoreWebView2.Source;
        if (!IgnWikiDirectoryReader.TryGetWikiSlug(url, out var slug))
        {
            ShowIgnMessage("Save IGN page works only on a live https://www.ign.com/wikis/... article.");
            return;
        }

        string title;
        try
        {
            var result = await GuideBrowser.ExecuteScriptAsync("document.title");
            title = JsonSerializer.Deserialize<string>(result) ?? slug;
        }
        catch { title = slug; }

        var gameName = GamesListBox.SelectedItem as string;
        var display = _activeIgnWiki?.WikiSlug == slug ? _activeIgnWiki.WikiTitle :
            gameName is not null && GameNameMatcher.IgnSlug(gameName) == slug ? gameName : slug;
        var canonicalUrl = new Uri(url).GetLeftPart(UriPartial.Path);
        var directory = new IgnWikiDirectory(slug, display, "https://www.ign.com/wikis/" + slug,
            [new IgnWikiPage(title, canonicalUrl, "This page", 0)]);
        await ArchiveIgnPagesAsync(directory, directory.Pages, isSinglePage: true);
    }

    private async void IgnSaveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_archiveBusy || !_browserReady) return;
        if (_activeIgnWiki is null)
        {
            ShowIgnMessage("Discover the IGN sidebar before selecting pages.");
            return;
        }
        var pages = _ignChoices.Where(choice => choice.IsSelected)
            .Select(choice => choice.Page).ToArray();
        if (pages.Length == 0)
        {
            ShowIgnMessage("Select at least one page from the IGN sidebar.");
            return;
        }

        if (pages.Length > 50 &&
            MessageBox.Show(this,
                $"Archive {pages.Length:N0} IGN pages?\n\n" +
                "This uses browser navigation, one article at a time. The process may involve substantial traffic and disk space. " +
                "You can cancel, then rerun to skip pages already saved. Make sure your use follows IGN's terms.",
                "Confirm IGN batch archive", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await ArchiveIgnPagesAsync(_activeIgnWiki, pages, isSinglePage: false);
    }

    private async Task ArchiveIgnPagesAsync(
        IgnWikiDirectory wiki, IReadOnlyList<IgnWikiPage> pages, bool isSinglePage)
    {
        if (_archiveBusy || !_browserReady) return;
        _archiveBusy = true;
        _ignCancellation = new CancellationTokenSource();
        var token = _ignCancellation.Token;
        IgnCancelButton.IsEnabled = true;
        IgnSaveSelectedButton.IsEnabled = false;
        IgnSaveCurrentButton.IsEnabled = false;
        IgnDiscoverButton.IsEnabled = false;
        GuideBrowser.IsEnabled = false; // avoid interrupting the live capture
        IgnArchiveProgress.Maximum = Math.Max(1, pages.Count);
        IgnArchiveProgress.Value = 0;
        var saved = 0;
        var skipped = 0;
        var failed = 0;
        var cancelled = false;

        try
        {
            var runner = new IgnWebViewArchiver(GuideBrowser);
            for (var i = 0; i < pages.Count; i++)
            {
                if (token.IsCancellationRequested) { cancelled = true; break; }
                var page = pages[i];
                IgnArchiveProgressLabel.Text =
                    $"{i + 1:N0}/{pages.Count:N0}: {page.Title} · saved {saved}, skipped {skipped}, failed {failed}";
                try
                {
                    if (_ignStore.IsSaved(page.Url))
                    {
                        skipped++;
                    }
                    else
                    {
                        var snapshot = await runner.CaptureAsync(page.Url,
                            alreadyOnPage: isSinglePage, cancellationToken: token);
                        await _ignStore.SaveSnapshotAsync(
                            wiki.WikiSlug, wiki.WikiTitle, page.Url, page.Title,
                            snapshot.Mhtml, snapshot.ImageCount, snapshot.MissingImages, token);
                        saved++;
                    }
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    failed++;
                    try
                    {
                        _ignStore.RecordFailure(wiki.WikiSlug, wiki.WikiTitle, page.Title, page.Url, ex.Message);
                    }
                    catch (Exception writeError)
                    {
                        IgnArchiveProgressLabel.Text = "Cannot write archive manifest: " + writeError.Message;
                        break;
                    }
                }

                IgnArchiveProgress.Value = i + 1;
                if (i < pages.Count - 1)
                {
                    try { await Task.Delay(900, token); }
                    catch (OperationCanceledException) { cancelled = true; break; }
                }
            }
        }
        finally
        {
            _archiveBusy = false;
            GuideBrowser.IsEnabled = true;
            IgnCancelButton.IsEnabled = false;
            IgnSaveSelectedButton.IsEnabled = true;
            IgnSaveCurrentButton.IsEnabled = true;
            IgnDiscoverButton.IsEnabled = true;
            _ignCancellation?.Dispose();
            _ignCancellation = null;
            IgnArchiveProgressLabel.Text =
                $"{(cancelled ? "Cancelled" : "Completed")} · {saved:N0} saved, {skipped:N0} already saved, {failed:N0} failed. " +
                "Failed pages can be selected and retried. Successful captures remain on disk.";
            RefreshIgnSavedList();
        }
    }

    private void IgnCancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_archiveBusy) return;
        IgnArchiveProgressLabel.Text = "Cancelling after the current browser operation…";
        _ignCancellation?.Cancel();
        GuideBrowser.CoreWebView2?.Stop();
    }

    private void IgnViewSaved_Click(object sender, RoutedEventArgs e)
    {
        RefreshIgnSavedList();
        DiscoveryTabs.SelectedIndex = 3;
    }

    private void IgnRefreshSaved_Click(object sender, RoutedEventArgs e) => RefreshIgnSavedList();

    private void RefreshIgnSavedList()
    {
        try
        {
            var pages = _ignStore.ListPages();
            IgnSavedList.ItemsSource = pages;
            IgnSavedSummary.Text = $"{pages.Count:N0} saved IGN pages · " +
                $"{_ignStore.FailureCount():N0} unsuccessful attempts · " +
                $"Folder: {_ignStore.RootDirectory}";
        }
        catch (Exception ex)
        {
            IgnSavedSummary.Text = "Could not load saved archive manifest: " + ex.Message;
        }
    }

    private void IgnSavedList_DoubleClick(object sender, MouseButtonEventArgs e) =>
        ReadSelectedIgnArchive();

    private void IgnReadOffline_Click(object sender, RoutedEventArgs e) =>
        ReadSelectedIgnArchive();

    private void ReadSelectedIgnArchive()
    {
        if (_archiveBusy || !_browserReady) return;
        if (IgnSavedList.SelectedItem is not IgnSavedPageLocation selected)
        {
            ShowIgnMessage("Select a saved IGN article first.");
            return;
        }
        if (!File.Exists(selected.FullPath))
        {
            ShowIgnMessage("Archive missing on disk. Refresh the saved list.");
            return;
        }
        SetOfflineReading(true);
        ResourceTitleText.Text = $"OFFLINE · IGN · {selected.Page.WikiTitle} · {selected.Title}";
        GuideBrowser.CoreWebView2.Navigate(new Uri(selected.FullPath).AbsoluteUri);
        AddressBox.Text = selected.FullPath;
        BrowserStatusText.Text = "Opening saved MHTML. Network requests are blocked in offline mode.";
    }

    private void IgnSavedOpenOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (_archiveBusy) return;
        if (IgnSavedList.SelectedItem is not IgnSavedPageLocation selected)
        {
            ShowIgnMessage("Select a saved IGN article first.");
            return;
        }
        SetOfflineReading(false);
        Navigate(selected.Page.Url);
    }

    private void IgnShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (IgnSavedList.SelectedItem is IgnSavedPageLocation selected)
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{selected.FullPath}\"")
                { UseShellExecute = true });
            else
            {
                Directory.CreateDirectory(_ignStore.RootDirectory);
                Process.Start(new ProcessStartInfo(_ignStore.RootDirectory) { UseShellExecute = true });
            }
        }
        catch (Exception ex) { ShowIgnMessage("Could not open the archive folder: " + ex.Message); }
    }

    private void ShowIgnMessage(string message)
    {
        BrowserStatusText.Text = message;
        MessageBox.Show(this, message, "IGN archiving", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
