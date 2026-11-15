namespace GameTwinGuideLibrary.Core;

/// <summary>
/// A directory entry pointing to content hosted by its original publisher.
/// The bundled Game Twin catalogs contain metadata and URLs, not guide bodies.
/// </summary>
public sealed record GuideResource(
    string CatalogKey,
    string Source,
    string Title,
    string Kind,
    string Detail,
    string Url)
{
    public string Subtitle => $"{Source}  •  {Kind}" +
        (string.IsNullOrWhiteSpace(Detail) ? "" : $"  •  {Detail}");

    public string Host
    {
        get
        {
            return Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.Host : "";
        }
    }
}

public sealed record CatalogGroup(string Key, string Name, string Description, int Count);

public sealed record SourceShortcut(string Name, string Description, string Url);

public sealed record MatchedGuide(GuideResource Resource, double Score);
