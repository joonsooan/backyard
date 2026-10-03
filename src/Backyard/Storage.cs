using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backyard;

public sealed class SavedState
{
    public int Coins { get; set; }
    public int Rows { get; set; }
    public FarmCell?[] Cells { get; set; } = [];
    public DateTimeOffset FirstRunAt { get; set; }
    public DateTimeOffset? LastWateredAt { get; set; }
    public DateTimeOffset? LastEventAt { get; set; }
    public Dictionary<string, int> HarvestCounts { get; set; } = [];
    public Dictionary<string, long> WatcherOffsets { get; set; } = [];
    public Diagnostics Diag { get; set; } = new();
}

public sealed class Diagnostics
{
    public int ParseErrors { get; set; }
    public int UnknownLines { get; set; }
    public DateTimeOffset? LastDetectedAt { get; set; }
}

public sealed class Storage(string filePath)
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "backyard", "state.json");

    public string FilePath { get; } = filePath;

    public SavedState? Load()
    {
        if (!File.Exists(FilePath))
            return null;
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(FilePath), BackyardJson.Default.SavedState);
        }
        catch (JsonException)
        {
            File.Copy(FilePath, FilePath + ".bak", overwrite: true);
            return null;
        }
    }

    public void Save(SavedState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(state, BackyardJson.Default.SavedState));
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SavedState))]
[JsonSerializable(typeof(TranscriptLine))]
public partial class BackyardJson : JsonSerializerContext;
