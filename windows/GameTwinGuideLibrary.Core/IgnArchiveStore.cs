using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameTwinGuideLibrary.Core;

public sealed record IgnArchivedPage(
    string WikiSlug,
    string WikiTitle,
    string Title,
    string Url,
    string FileName,
    string Status,
    long SizeBytes,
    int ImageCount,
    int MissingImages,
    DateTimeOffset AttemptedUtc,
    string? Error)
{
    public string Summary =>
        Status == "Saved"
            ? $"IGN · {WikiTitle} · {SizeBytes / 1024d / 1024d:0.0} MB · " +
              $"{ImageCount} images checked, {MissingImages} unavailable · {AttemptedUtc.LocalDateTime:g}"
            : $"IGN · {WikiTitle} · FAILED · {Error}";
}

public sealed class IgnArchiveManifest
{
    public string WikiSlug { get; set; } = "";
    public string WikiTitle { get; set; } = "";
    public string RootUrl { get; set; } = "";
    public List<IgnArchivedPage> Pages { get; set; } = [];
}

public sealed record IgnSavedPageLocation(IgnArchivedPage Page, string FullPath)
{
    public string Title => Page.Title;
    public string Summary => Page.Summary;
}

/// <summary>
/// Local, resumable IGN archive directory. No remote files are redistributed.
/// Each page is an .mhtml file, while manifest.json records source, status,
/// saved bytes, and best-effort image diagnostics. No sqlite dependency.
/// </summary>
public sealed class IgnArchiveStore(string rootDirectory)
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public string RootDirectory { get; } = Path.GetFullPath(rootDirectory);

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Game Guide Library", "IGN");

    private string DirectoryFor(string slug) =>
        Path.Combine(RootDirectory, slug);

    public IgnArchiveManifest LoadWiki(string wikiSlug, string title = "")
    {
        ValidateSlug(wikiSlug);
        var path = Path.Combine(DirectoryFor(wikiSlug), "manifest.json");
        if (!File.Exists(path))
            return new IgnArchiveManifest
            {
                WikiSlug = wikiSlug,
                WikiTitle = title.Length > 0 ? title : wikiSlug,
                RootUrl = "https://www.ign.com/wikis/" + wikiSlug
            };
        var manifest = JsonSerializer.Deserialize<IgnArchiveManifest>(File.ReadAllText(path))
                       ?? throw new InvalidDataException("Empty archive manifest: " + path);
        if (manifest.WikiSlug != wikiSlug)
            throw new InvalidDataException("Archive manifest wiki slug does not match its directory.");
        manifest.Pages ??= [];
        return manifest;
    }

    public bool IsSaved(string url) => FindSavedFile(url) is not null;

    public string? FindSavedFile(string url)
    {
        if (!IgnWikiDirectoryReader.TryGetWikiSlug(url, out var wikiSlug)) return null;
        var canonical = new Uri(url).GetLeftPart(UriPartial.Path);
        var manifest = LoadWiki(wikiSlug);
        var existing = manifest.Pages.FirstOrDefault(page =>
            page.Status == "Saved" &&
            page.Url.Equals(canonical, StringComparison.OrdinalIgnoreCase));
        return existing is not null && IsValidArchive(existing, wikiSlug)
            ? Path.Combine(DirectoryFor(wikiSlug), existing.FileName) : null;
    }

    public async Task<IgnArchivedPage> SaveSnapshotAsync(
        string wikiSlug,
        string wikiTitle,
        string url,
        string title,
        string mhtml,
        int imageCount,
        int missingImages,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(wikiSlug, url);
        if (!mhtml.StartsWith("MIME-Version:", StringComparison.OrdinalIgnoreCase) &&
            !mhtml.Contains("multipart/related", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The browser did not return an MHTML document.");

        var folder = DirectoryFor(wikiSlug);
        Directory.CreateDirectory(folder);
        var name = FileNameFor(url);
        var destination = Path.Combine(folder, name);
        var staging = Path.Combine(folder, name + "." + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            await File.WriteAllTextAsync(staging, mhtml, new UTF8Encoding(false), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (new FileInfo(staging).Length < 128)
                throw new InvalidDataException("The generated archive is unexpectedly empty.");
            File.Move(staging, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }

        var record = new IgnArchivedPage(
            wikiSlug, wikiTitle, title, url, name, "Saved",
            new FileInfo(destination).Length,
            imageCount, missingImages, DateTimeOffset.UtcNow, null);
        var manifest = LoadWiki(wikiSlug, wikiTitle);
        manifest.WikiTitle = wikiTitle;
        ReplacePage(manifest, record);
        WriteManifest(manifest);
        return record;
    }

    public IgnArchivedPage RecordFailure(string wikiSlug, string wikiTitle, string title, string url, string message)
    {
        ValidatePage(wikiSlug, url);
        var manifest = LoadWiki(wikiSlug, wikiTitle);
        // Never turn a working archive into a failed entry.
        var existing = manifest.Pages.FirstOrDefault(p =>
            p.Url.Equals(url, StringComparison.OrdinalIgnoreCase));
        if (existing is { Status: "Saved" } && IsValidArchive(existing, wikiSlug))
            return existing;
        var failure = new IgnArchivedPage(
            wikiSlug, wikiTitle, title, url, "", "Failed", 0, 0, 0,
            DateTimeOffset.UtcNow, message.Length > 400 ? message[..400] : message);
        manifest.WikiTitle = wikiTitle;
        ReplacePage(manifest, failure);
        WriteManifest(manifest);
        return failure;
    }

    public IReadOnlyList<IgnSavedPageLocation> ListPages()
    {
        if (!Directory.Exists(RootDirectory)) return [];
        var output = new List<IgnSavedPageLocation>();
        foreach (var folder in Directory.EnumerateDirectories(RootDirectory))
        {
            var slug = Path.GetFileName(folder);
            if (!IgnWikiDirectoryReader.TryGetWikiSlug(
                    "https://www.ign.com/wikis/" + slug, out _))
                continue;
            var manifest = LoadWiki(slug);
            foreach (var entry in manifest.Pages)
            {
                if (entry.Status != "Saved" || !IsValidArchive(entry, slug)) continue;
                output.Add(new IgnSavedPageLocation(entry,
                    Path.Combine(folder, entry.FileName)));
            }
        }
        return output.OrderByDescending(p => p.Page.AttemptedUtc)
            .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public int FailureCount()
    {
        if (!Directory.Exists(RootDirectory)) return 0;
        var count = 0;
        foreach (var folder in Directory.EnumerateDirectories(RootDirectory))
        {
            var slug = Path.GetFileName(folder);
            if (!IgnWikiDirectoryReader.TryGetWikiSlug(
                    "https://www.ign.com/wikis/" + slug, out _)) continue;
            count += LoadWiki(slug).Pages.Count(p => p.Status == "Failed");
        }
        return count;
    }

    private static void ReplacePage(IgnArchiveManifest manifest, IgnArchivedPage page)
    {
        manifest.Pages.RemoveAll(p => p.Url.Equals(page.Url, StringComparison.OrdinalIgnoreCase));
        manifest.Pages.Add(page);
    }

    private void WriteManifest(IgnArchiveManifest manifest)
    {
        var folder = DirectoryFor(manifest.WikiSlug);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "manifest.json");
        var staging = file + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            File.WriteAllText(staging, JsonSerializer.Serialize(manifest, SerializerOptions));
            File.Move(staging, file, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }

    private bool IsValidArchive(IgnArchivedPage page, string wikiSlug) =>
        page.FileName.Length > 0 &&
        page.FileName.Equals(FileNameFor(page.Url), StringComparison.Ordinal) &&
        File.Exists(Path.Combine(DirectoryFor(wikiSlug), page.FileName));

    private static string FileNameFor(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(hash).ToLowerInvariant() + ".mhtml";
    }

    private static void ValidateSlug(string slug)
    {
        if (!IgnWikiDirectoryReader.TryGetWikiSlug(
                "https://www.ign.com/wikis/" + slug, out var checkedSlug) ||
            !slug.Equals(checkedSlug, StringComparison.Ordinal))
            throw new ArgumentException("Invalid IGN wiki slug.", nameof(slug));
    }

    private static void ValidatePage(string wikiSlug, string url)
    {
        ValidateSlug(wikiSlug);
        if (!IgnWikiDirectoryReader.TryGetWikiSlug(url, out var pageSlug) ||
            !wikiSlug.Equals(pageSlug, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The page URL is not within this IGN wiki.", nameof(url));
    }
}
