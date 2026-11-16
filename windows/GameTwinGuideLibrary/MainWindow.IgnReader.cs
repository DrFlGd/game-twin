using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using GameTwinGuideLibrary.Core;
using Microsoft.Win32;

namespace GameTwinGuideLibrary;

/// <summary>
/// An offline-only, reversible IGN reader view. The original MHTML remains
/// intact. The clean export works on a temporary DOM and reloads the original.
/// </summary>
public partial class MainWindow
{
    private static readonly Lazy<string> ReaderTemplate = new(() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "ign_reader.js")));

    private IgnSavedPageLocation? _currentOfflinePage;
    private bool _ignReaderEnabled = true;
    private bool _ignExporting;

    private void InitializeIgnReaderEvents()
    {
        GuideBrowser.CoreWebView2.NavigationCompleted += async (_, args) =>
        {
            if (!_offlineReading || !args.IsSuccess || _archiveBusy) return;
            if (!_ignReaderEnabled) return;
            try
            {
                var result = await ExecuteIgnReaderAsync(IgnReaderAction.Apply);
                if (result == "missing-article")
                    BrowserStatusText.Text = "This MHTML has no .wiki-html article; showing the original page.";
                else if (result == "reader")
                    BrowserStatusText.Text = "Offline IGN reader view · title, guide and images retained; other site chrome hidden.";
            }
            catch (Exception ex)
            {
                BrowserStatusText.Text = "Reader filter unavailable; original MHTML is still intact: " + ex.Message;
            }
        };
    }

    private void UpdateIgnReaderControls()
    {
        if (IgnReaderToggleButton is null || IgnExportCleanButton is null) return;
        var enabled = _offlineReading && _currentOfflinePage != null && !_archiveBusy && !_ignExporting;
        IgnReaderToggleButton.IsEnabled = enabled;
        IgnExportCleanButton.IsEnabled = enabled;
        IgnReaderToggleButton.Content = _ignReaderEnabled ? "Reader: On" : "Reader: Off";
        IgnReaderToggleButton.ToolTip = _ignReaderEnabled
            ? "Display original IGN markup instead of the cleaned reader view"
            : "Hide IGN navigation, ads, embeds, and share buttons";
    }

    private async Task<string> ExecuteIgnReaderAsync(IgnReaderAction action)
    {
        if (!_offlineReading || _currentOfflinePage is null)
            throw new InvalidOperationException("Open a saved IGN page offline first.");

        var script = IgnReaderScript.Build(
            ReaderTemplate.Value,
            action,
            _currentOfflinePage.Page.Url,
            _currentOfflinePage.Title);

        // Official WebView2 behavior: host-injected scripts still execute
        // when the archived document's scripts are disabled.
        var result = await GuideBrowser.CoreWebView2.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<string>(result) ?? "no-result";
    }

    private async void IgnReaderToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_offlineReading || _archiveBusy || _ignExporting || _currentOfflinePage is null) return;
        var requested = !_ignReaderEnabled;
        try
        {
            var result = await ExecuteIgnReaderAsync(
                requested ? IgnReaderAction.Apply : IgnReaderAction.Restore);
            if (result is not ("reader" or "original"))
            {
                BrowserStatusText.Text = "Reader mode could not be applied: " + result;
                return;
            }
            _ignReaderEnabled = requested;
            UpdateIgnReaderControls();
            BrowserStatusText.Text = requested
                ? "Reader view enabled. Original MHTML remains unchanged."
                : "Original IGN layout restored without reloading the archive.";
        }
        catch (Exception ex)
        {
            BrowserStatusText.Text = "Could not switch reader view: " + ex.Message;
        }
    }

    private async void IgnExportClean_Click(object sender, RoutedEventArgs e)
    {
        if (!_offlineReading || _archiveBusy || _ignExporting || _currentOfflinePage is null) return;

        var safeTitle = string.Concat(_currentOfflinePage.Title
                .Where(ch => !Path.GetInvalidFileNameChars().Contains(ch))).Trim();
        if (safeTitle.Length == 0) safeTitle = "IGN-guide";
        if (safeTitle.Length > 70) safeTitle = safeTitle[..70];

        var dialog = new SaveFileDialog
        {
            Title = "Export a clean offline IGN article (MHTML)",
            Filter = "MHTML web archives (*.mhtml)|*.mhtml",
            DefaultExt = ".mhtml",
            AddExtension = true,
            FileName = safeTitle + ".reader.mhtml",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;

        _ignExporting = true;
        _archiveBusy = true; // avoid concurrent browsing, saves or navigation
        GuideBrowser.IsEnabled = false;
        IgnSaveSelectedButton.IsEnabled = false;
        IgnSaveCurrentButton.IsEnabled = false;
        IgnDiscoverButton.IsEnabled = false;
        UpdateIgnReaderControls();

        try
        {
            BrowserStatusText.Text = "Preparing clean IGN export from the saved page…";
            var result = await ExecuteIgnReaderAsync(IgnReaderAction.Isolate);
            if (result != "isolated")
                throw new InvalidDataException(
                    "Could not isolate the IGN article. Result: " + result);

            // The new archive contains a DOM with only the guide article,
            // page header, attribution, and resources still referenced by them.
            // No copy of the original MHTML is overwritten.
            var raw = await GuideBrowser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Page.captureSnapshot", "{\"format\":\"mhtml\"}");
            using var snapshot = JsonDocument.Parse(raw);
            var mhtml = snapshot.RootElement.GetProperty("data").GetString();
            if (string.IsNullOrEmpty(mhtml) ||
                (!mhtml.StartsWith("MIME-Version:", StringComparison.OrdinalIgnoreCase) &&
                 !mhtml.Contains("multipart/related", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Chromium did not produce a valid MHTML export.");

            // Make the output replaceable without leaving a truncated file
            // if the export operation is interrupted.
            var fullPath = Path.GetFullPath(dialog.FileName);
            var staging = fullPath + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                await File.WriteAllTextAsync(staging, mhtml, new UTF8Encoding(false));
                File.Move(staging, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
            BrowserStatusText.Text = "Clean reader MHTML exported to " + fullPath;
            MessageBox.Show(this,
                "Export complete.\n\n" + fullPath +
                "\n\nThe original saved IGN archive was not modified. " +
                "Embedded images depend on what the browser originally captured.",
                "Clean IGN export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Clean export failed: " + ex.Message +
                "\n\nThe original archive is unchanged.", "IGN export",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _archiveBusy = false;
            _ignExporting = false;
            GuideBrowser.IsEnabled = true;
            IgnSaveSelectedButton.IsEnabled = true;
            IgnSaveCurrentButton.IsEnabled = true;
            IgnDiscoverButton.IsEnabled = true;
            UpdateIgnReaderControls();
            // The export intentionally isolated the *viewer* document in memory.
            // Reloading restores the unmodified archived MHTML, then the
            // NavigationCompleted handler reapplies reader mode if enabled.
            if (_offlineReading && GuideBrowser.CoreWebView2 is not null)
                GuideBrowser.CoreWebView2.Reload();
        }
    }
}
