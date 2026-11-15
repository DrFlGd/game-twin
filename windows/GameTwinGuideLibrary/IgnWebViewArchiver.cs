using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using GameTwinGuideLibrary.Core;

namespace GameTwinGuideLibrary;

/// <summary>
/// Captures IGN pages from the same WebView2 the user browses with, so ordinary
/// cookies and interactive page state are retained. No bot-bypass logic.
/// </summary>
public sealed class IgnWebViewArchiver(WebView2 browser)
{
    private const int NavigationTimeoutSeconds = 45;

    public async Task<IgnBrowserSnapshot> CaptureAsync(
        string sourceUrl,
        bool alreadyOnPage,
        CancellationToken cancellationToken)
    {
        if (browser.CoreWebView2 is null)
            throw new InvalidOperationException("The browser is not initialized.");
        if (!IgnWikiDirectoryReader.TryGetWikiSlug(sourceUrl, out var expectedWiki))
            throw new ArgumentException("Only HTTPS IGN wiki pages can be archived.");

        if (!alreadyOnPage || !SameDocument(browser.CoreWebView2.Source, sourceUrl))
            await NavigateAsync(sourceUrl, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        if (!IgnWikiDirectoryReader.TryGetWikiSlug(browser.CoreWebView2.Source, out var actualWiki) ||
            actualWiki != expectedWiki)
            throw new InvalidOperationException("IGN redirected outside the requested wiki.");

        // Give the IGN React app time to hydrate before preloading lazy images.
        await Task.Delay(1000, cancellationToken);
        var imageAudit = await WarmImagesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // CDP's Page.captureSnapshot returns one serialized MHTML document.
        // Unlike HTML-only saving it includes loaded resources such as images.
        var raw = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
            "Page.captureSnapshot", "{\"format\":\"mhtml\"}");
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(raw);
        var mhtml = document.RootElement.GetProperty("data").GetString()
                    ?? throw new InvalidDataException("Empty MHTML response from Chromium.");
        if (mhtml.Length < 128)
            throw new InvalidDataException("The archived page is unexpectedly small.");

        return new IgnBrowserSnapshot(mhtml, imageAudit.TotalImages, imageAudit.MissingImages);
    }

    private async Task NavigateAsync(string url, CancellationToken cancellationToken)
    {
        var core = browser.CoreWebView2;
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args) =>
            completion.TrySetResult(args);

        core.NavigationCompleted += Completed;
        try
        {
            core.Navigate(url);
            var result = await completion.Task.WaitAsync(
                TimeSpan.FromSeconds(NavigationTimeoutSeconds), cancellationToken);
            if (!result.IsSuccess)
                throw new InvalidOperationException("IGN navigation failed: " + result.WebErrorStatus);
            if (!SameDocument(core.Source, url))
                throw new InvalidOperationException("IGN redirected to another article: " + core.Source);
        }
        finally
        {
            core.NavigationCompleted -= Completed;
        }
    }

    /// <summary>
    /// Best-effort image warming: force image loading, scroll the document in
    /// increments, wait for loads, then return to the top. Missing images are
    /// reported in the manifest; they are never claimed as included.
    /// </summary>
    private async Task<ImageAudit> WarmImagesAsync(CancellationToken cancellationToken)
    {
        const string prepare = """
            (() => {
              for (const image of document.images) {
                try {
                  image.loading = 'eager';
                  if (!image.getAttribute('src')) {
                    const candidate = image.getAttribute('data-src') ||
                                      image.getAttribute('data-original');
                    if (candidate) image.setAttribute('src', candidate);
                  }
                  if (!image.getAttribute('srcset') && image.getAttribute('data-srcset'))
                    image.setAttribute('srcset', image.getAttribute('data-srcset'));
                } catch {}
              }
              return Math.max(document.documentElement.scrollHeight, document.body?.scrollHeight || 0);
            })()
            """;

        var heightJson = await browser.ExecuteScriptAsync(prepare);
        var height = 0;
        if (int.TryParse(heightJson, out var parsed)) height = parsed;
        const int step = 850;
        var iterations = Math.Min(130, Math.Max(1, (int)Math.Ceiling(height / (double)step)));
        for (var i = 0; i < iterations; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await browser.ExecuteScriptAsync($"window.scrollTo(0, {i * step});");
            await Task.Delay(65, cancellationToken);
        }
        await browser.ExecuteScriptAsync("window.scrollTo(0, 0);");

        ImageAudit last = new(0, 0);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var auditJson = await browser.ExecuteScriptAsync("""
                (() => ({
                  totalImages: document.images.length,
                  missingImages: Array.from(document.images).filter(
                    i => !i.complete || i.naturalWidth === 0
                  ).length
                }))()
                """);
            try
            {
                using var doc = JsonDocument.Parse(auditJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    last = new ImageAudit(
                        doc.RootElement.GetProperty("totalImages").GetInt32(),
                        doc.RootElement.GetProperty("missingImages").GetInt32());
                    if (last.MissingImages == 0) break;
                }
            }
            catch (JsonException) { /* image audit is best-effort */ }

            await Task.Delay(400, cancellationToken);
        }
        await Task.Delay(550, cancellationToken);
        return last;
    }

    private static bool SameDocument(string current, string requested)
    {
        if (!Uri.TryCreate(current, UriKind.Absolute, out var a) ||
            !Uri.TryCreate(requested, UriKind.Absolute, out var b)) return false;
        return a.GetLeftPart(UriPartial.Path).Equals(
            b.GetLeftPart(UriPartial.Path), StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ImageAudit(int TotalImages, int MissingImages);
}

public sealed record IgnBrowserSnapshot(string Mhtml, int ImageCount, int MissingImages);
