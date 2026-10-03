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
    public Dictionary<string, WatcherCursor> WatcherCursors { get; set; } = [];
    public Diagnostics Diag { get; set; } = new();

    public FarmState ToFarmState() => new()
    {
        Coins = Coins,
        Rows = Rows,
        Cells = Cells,
        FirstRunAt = FirstRunAt,
        LastWateredAt = LastWateredAt,
        LastEventAt = LastEventAt,
        HarvestCounts = HarvestCounts,
    };

    public void CopyFrom(FarmState farm)
    {
        Coins = farm.Coins;
        Rows = farm.Rows;
        Cells = farm.Cells;
        FirstRunAt = farm.FirstRunAt;
        LastWateredAt = farm.LastWateredAt;
        LastEventAt = farm.LastEventAt;
        HarvestCounts = farm.HarvestCounts;
    }
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
        var tmp = $"{FilePath}.{Environment.ProcessId}.tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, BackyardJson.Default.SavedState));
        File.Move(tmp, FilePath, overwrite: true);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SavedState))]
[JsonSerializable(typeof(TranscriptLine))]
public partial class BackyardJson : JsonSerializerContext;
