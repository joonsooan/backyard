using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backyard;

public sealed class SavedState : FarmState
{
    public const int CurrentVersion = 2;

    public SavedState()
    {
    }

    public SavedState(DateTimeOffset now) : base(now)
    {
    }

    public int Version { get; set; } = CurrentVersion;
    public Dictionary<string, WatcherCursor> WatcherCursors { get; set; } = [];
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
            var state = JsonSerializer.Deserialize(File.ReadAllText(FilePath), BackyardJson.Default.SavedState);
            return state is null ? null : Migrate(state);
        }
        catch (JsonException)
        {
            File.Copy(FilePath, FilePath + ".bak", overwrite: true);
            return null;
        }
    }

    private static SavedState Migrate(SavedState state)
    {
        switch (state.Version)
        {
            case 0:
                state.Version = 1;
                goto case 1;
            case 1:
                if (state.UnlockedCrops.All(c => c == Balance.Crops[0].Name))
                {
                    state.UnlockedCrops = new[] { Balance.Crops[0].Name }
                        .Concat(state.HarvestCounts.Keys)
                        .Concat(state.Planted.Select(c => c.Crop))
                        .Where(c => Balance.Crop(c) is not null)
                        .Distinct()
                        .OrderBy(Balance.CropOrder)
                        .ToList();
                }
                state.Version = 2;
                goto case 2;
            case 2:
                return state;
            default:
                throw new JsonException($"state version {state.Version} is newer than supported {SavedState.CurrentVersion}");
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

public sealed class ClaudeSettings
{
    [JsonPropertyName("statusLine")] public ClaudeStatusLine? StatusLine { get; set; }
}

public sealed class ClaudeStatusLine
{
    [JsonPropertyName("command")] public string? Command { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SavedState))]
[JsonSerializable(typeof(TranscriptLine))]
[JsonSerializable(typeof(ClaudeSettings))]
public partial class BackyardJson : JsonSerializerContext;
