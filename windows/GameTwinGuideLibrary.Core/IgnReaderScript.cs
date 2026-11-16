using System.Text.RegularExpressions;
using System.Text.Json;

namespace GameTwinGuideLibrary.Core;

/// <summary>
/// Host-owned actions run via WebView2.ExecuteScriptAsync on an offline IGN
/// archive. The saved source MHTML is never modified by reader mode.
/// </summary>
public enum IgnReaderAction { Apply, Restore, Isolate }

public static class IgnReaderScript
{
    public static string Build(string template, IgnReaderAction action, string? sourceUrl, string? title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        if (!template.Contains("\"__GG_ACTION__\"", StringComparison.Ordinal) ||
            !template.Contains("__GG_SOURCE_URL__", StringComparison.Ordinal) ||
            !template.Contains("__GG_ARTICLE_TITLE__", StringComparison.Ordinal))
            throw new InvalidDataException("IGN reader script is missing a required parameter marker.");

        if (sourceUrl is { Length: > 0 } &&
            !IgnWikiDirectoryReader.TryGetWikiSlug(sourceUrl, out _))
            throw new ArgumentException("The reader can only process saved HTTPS IGN wiki pages.");

        var command = action switch
        {
            IgnReaderAction.Apply => "apply",
            IgnReaderAction.Restore => "restore",
            IgnReaderAction.Isolate => "isolate",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        // JSON encoding protects quote/backslash/newline data supplied by an
        // article title or URL from changing the injected JavaScript program.
        var commandJson = JsonSerializer.Serialize(command);
        var sourceJson = JsonSerializer.Serialize(sourceUrl ?? "");
        var titleJson = JsonSerializer.Serialize(title ?? "");
        // One-pass replacement: a title containing a parameter marker is
        // kept as literal data instead of being substituted a second time.
        return Regex.Replace(template,
            "\"__GG_ACTION__\"|__GG_SOURCE_URL__|__GG_ARTICLE_TITLE__",
            match => match.Value switch
            {
                "\"__GG_ACTION__\"" => commandJson,
                "__GG_SOURCE_URL__" => sourceJson,
                "__GG_ARTICLE_TITLE__" => titleJson,
                _ => throw new InvalidOperationException("Unknown reader template marker")
            });
    }
}
