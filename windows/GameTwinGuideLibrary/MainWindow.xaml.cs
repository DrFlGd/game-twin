using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameTwinGuideLibrary.Core;

namespace GameTwinGuideLibrary;

public partial class MainWindow : Window
{
    private const int CatalogPageSize = 200;
    private readonly ObservableCollection<string> _games = [];
    private readonly GameLibraryStore _gameStore = new(GameLibraryStore.DefaultPath);
    private CatalogService? _catalog;
    private IReadOnlyList<GuideResource> _filteredCatalog = [];
    private int _catalogPage;
    private bool _browserReady;
    private string? _pendingNavigation;

    public MainWindow()
    {
        InitializeComponent();
        GamesListBox.ItemsSource = _games;
        BackButton.IsEnabled = false;
        ForwardButton.IsEnabled = false;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _catalog = new CatalogService(System.IO.Path.Combine(AppContext.BaseDirectory, "Catalogs"));
            CatalogSummary.Text = $"{_catalog.TotalCount:N0} directory records from Game Twin's bundled catalogs. " +
                "These records are URLs and metadata, not locally saved guide pages.";
            CatalogSourceCombo.ItemsSource = _catalog.Groups;
            CatalogSourceCombo.SelectedIndex = 0;
            UpdateCatalog();

            foreach (var name in _gameStore.Load()) _games.Add(name);
            if (_games.Count > 0) GamesListBox.SelectedIndex = 0;
            else UpdateGameDiscovery();
        }
        catch (Exception ex)
        {
            BrowserStatusText.Text = "Could not initialize the local catalog.";
            MessageBox.Show(this,
                $"Failed to load the bundled catalog or game list.\n\n{ex.Message}",
                "Game Guide Library", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        try
        {
            await GuideBrowser.EnsureCoreWebView2Async();
            var core = GuideBrowser.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.NavigationStarting += (_, args) =>
            {
                BrowserStatusText.Text = "Loading " + args.Uri;
            };
            core.NavigationCompleted += (_, args) =>
            {
                AddressBox.Text = core.Source;
                BackButton.IsEnabled = core.CanGoBack;
                ForwardButton.IsEnabled = core.CanGoForward;
                BrowserStatusText.Text = args.IsSuccess ? "Page loaded from the original website." :
                    $"Website navigation failed: {args.WebErrorStatus}";
            };
            core.DownloadStarting += (_, args) =>
            {
                BrowserStatusText.Text = "Browser download requested: " +
                    System.IO.Path.GetFileName(args.ResultFilePath);
            };
            _browserReady = true;
            BrowserStatusText.Text = "Ready. Double-click a catalog entry or source to open it.";
            if (_pendingNavigation is not null)
            {
                var pending = _pendingNavigation;
                _pendingNavigation = null;
                Navigate(pending);
            }
        }
        catch (Exception ex)
        {
            BrowserStatusText.Text = "Microsoft Edge WebView2 Runtime is required.";
            MessageBox.Show(this,
                "The embedded browser could not start. Install the Microsoft Edge WebView2 Runtime, then restart.\n\n" +
                ex.Message,
                "Browser unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateGameDiscovery()
    {
        if (_catalog is null) return;
        var game = GamesListBox.SelectedItem as string ?? "";
        SelectedGameHeading.Text = game.Length == 0 ? "Choose or add a game" : game;
        SourceShortcutsList.ItemsSource = _catalog.ShortcutsForGame(game);

        IReadOnlyList<GuideResource> matched = game.Length == 0 ? Array.Empty<GuideResource>() : _catalog.ForGame(game);
        MatchesList.ItemsSource = matched;
        MatchedGuidesHeading.Text = matched.Count == 0
            ? "NO EXACT CATALOG MATCHES — USE SOURCE SHORTCUTS"
            : $"MATCHING CATALOG ENTRIES  •  {matched.Count}";
    }

    private void GamesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateGameDiscovery();

    private void GameNameInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddGame_Click(sender, e);
    }

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var game = GameNameInput.Text.Trim();
        if (game.Length == 0) return;
        var existing = _games.FirstOrDefault(g => string.Equals(g, game, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            GamesListBox.SelectedItem = existing;
            GameNameInput.Clear();
            return;
        }
        _games.Add(game);
        PersistGames();
        GamesListBox.SelectedItem = game;
        GameNameInput.Clear();
    }

    private void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (GamesListBox.SelectedItem is not string game) return;
        var index = GamesListBox.SelectedIndex;
        _games.Remove(game);
        PersistGames();
        if (_games.Count > 0) GamesListBox.SelectedIndex = Math.Min(index, _games.Count - 1);
        else UpdateGameDiscovery();
    }

    private void PersistGames()
    {
        try { _gameStore.Save(_games); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save the game list:\n{ex.Message}",
                "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CatalogSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateCatalog();

    private void CatalogSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateCatalog();

    private void UpdateCatalog()
    {
        if (_catalog is null || CatalogEntriesList is null || CatalogSearchBox is null) return;
        var key = (CatalogSourceCombo.SelectedItem as CatalogGroup)?.Key ?? "all";
        _filteredCatalog = _catalog.Browse(key, CatalogSearchBox.Text, int.MaxValue);
        _catalogPage = 0;
        ShowCatalogPage();
    }

    private void ShowCatalogPage()
    {
        var start = _catalogPage * CatalogPageSize;
        CatalogEntriesList.ItemsSource = _filteredCatalog.Skip(start).Take(CatalogPageSize).ToArray();
        CatalogPreviousButton.IsEnabled = _catalogPage > 0;
        CatalogNextButton.IsEnabled = start + CatalogPageSize < _filteredCatalog.Count;
        CatalogPageInfo.Text = _filteredCatalog.Count == 0 ? "No matching entries" :
            $"{start + 1:N0}–{Math.Min(start + CatalogPageSize, _filteredCatalog.Count):N0}" +
            $" of {_filteredCatalog.Count:N0}";
    }

    private void CatalogPrevious_Click(object sender, RoutedEventArgs e)
    {
        if (_catalogPage == 0) return;
        _catalogPage--;
        ShowCatalogPage();
    }

    private void CatalogNext_Click(object sender, RoutedEventArgs e)
    {
        if ((_catalogPage + 1) * CatalogPageSize >= _filteredCatalog.Count) return;
        _catalogPage++;
        ShowCatalogPage();
    }

    private void MatchesList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MatchesList.SelectedItem is GuideResource resource) OpenResource(resource);
    }

    private void CatalogEntriesList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CatalogEntriesList.SelectedItem is GuideResource resource) OpenResource(resource);
    }

    private void SourceShortcutsList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SourceShortcutsList.SelectedItem is SourceShortcut source)
        {
            ResourceTitleText.Text = $"{source.Name}  •  {source.Description}";
            Navigate(source.Url);
        }
    }

    private void OpenSelectedResource_Click(object sender, RoutedEventArgs e)
    {
        var selected = DiscoveryTabs.SelectedIndex == 1 ?
            CatalogEntriesList.SelectedItem : MatchesList.SelectedItem;
        if (selected is GuideResource resource) OpenResource(resource);
        else BrowserStatusText.Text = "Select a resource in the list first.";
    }

    private void OpenResource(GuideResource resource)
    {
        ResourceTitleText.Text = $"{resource.Title}  •  {resource.Subtitle}";
        Navigate(resource.Url);
    }

    private void Navigate(string address)
    {
        var value = address.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            BrowserStatusText.Text = "Enter a valid HTTP or HTTPS address.";
            return;
        }
        AddressBox.Text = uri.AbsoluteUri;
        if (!_browserReady)
        {
            _pendingNavigation = uri.AbsoluteUri;
            BrowserStatusText.Text = "Browser is still starting; navigation queued.";
            return;
        }
        try
        {
            GuideBrowser.CoreWebView2.Navigate(uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            BrowserStatusText.Text = "Navigation failed: " + ex.Message;
        }
    }

    private void Go_Click(object sender, RoutedEventArgs e) => Navigate(AddressBox.Text);

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Navigate(AddressBox.Text);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_browserReady && GuideBrowser.CoreWebView2.CanGoBack) GuideBrowser.CoreWebView2.GoBack();
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (_browserReady && GuideBrowser.CoreWebView2.CanGoForward) GuideBrowser.CoreWebView2.GoForward();
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_browserReady) GuideBrowser.CoreWebView2.Reload();
    }

    private void OpenExternal_Click(object sender, RoutedEventArgs e)
    {
        var value = _browserReady ? GuideBrowser.CoreWebView2.Source : AddressBox.Text;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { BrowserStatusText.Text = "Could not open browser: " + ex.Message; }
        }
        else BrowserStatusText.Text = "Open a web page first.";
    }
}
