using System.Text.Json;

namespace GameTwinGuideLibrary.Core;

/// <summary>Only stores a list of user-selected game names. Archiving is Phase 2.</summary>
public sealed class GameLibraryStore(string filePath)
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameTwinGuideLibrary", "games.json");

    public IReadOnlyList<string> Load()
    {
        if (!File.Exists(filePath))
            return ["Cyberpunk 2077", "Elden Ring", "The Legend of Zelda: Ocarina of Time"];
        try
        {
            var names = JsonSerializer.Deserialize<string[]>(File.ReadAllText(filePath)) ?? [];
            return NormalizeNames(names);
        }
        catch (JsonException)
        {
            throw new InvalidDataException($"The saved game list is invalid JSON: {filePath}");
        }
    }

    public void Save(IEnumerable<string> names)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var temp = filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(NormalizeNames(names),
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, filePath, overwrite: true);
    }

    private static string[] NormalizeNames(IEnumerable<string> names) =>
        names.Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
}
