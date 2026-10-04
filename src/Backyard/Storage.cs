using System.Text.Json;
using System.Text.Json.Nodes;
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

    public string BackupPath => FilePath + ".bak";
    public string CorruptPath => FilePath + ".corrupt";

    public SavedState? Load()
    {
        if (!File.Exists(FilePath))
            return null;
        try
        {
            return Parse(File.ReadAllText(FilePath));
        }
        catch (JsonException)
        {
            File.Copy(FilePath, CorruptPath, overwrite: true);
            if (!File.Exists(BackupPath))
                throw;
            return Parse(File.ReadAllText(BackupPath));
        }
    }

    private static SavedState Parse(string json)
    {
        var state = JsonSerializer.Deserialize(json, BackyardJson.Default.SavedState)
                    ?? throw new JsonException("state is null");
        return Migrate(state);
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
        if (File.Exists(FilePath))
            File.Replace(tmp, FilePath, BackupPath, ignoreMetadataErrors: true);
        else
            File.Move(tmp, FilePath);
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

public static class ClaudeSettingsFile
{
    public const string Command = "backyard status";
    private const string ChainSuffix = " && " + Command;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static string AddStatusline(string json)
    {
        var root = Parse(json);
        if (root["statusLine"] is not JsonObject statusLine)
        {
            root["statusLine"] = new JsonObject
            {
                ["type"] = "command",
                ["command"] = Command,
                ["refreshInterval"] = 1,
            };
            return root.ToJsonString(Indented);
        }

        var existing = statusLine["command"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrEmpty(existing))
        {
            statusLine["command"] = Command;
            statusLine["type"] ??= "command";
            return root.ToJsonString(Indented);
        }
        if (existing.Contains(Command))
            return json;

        statusLine["command"] = existing + ChainSuffix;
        return root.ToJsonString(Indented);
    }

    public static string RemoveStatusline(string json)
    {
        var root = Parse(json);
        if (root["statusLine"] is not JsonObject statusLine)
            return json;
        var existing = statusLine["command"]?.GetValue<string>()?.Trim();
        if (existing == Command)
        {
            root.Remove("statusLine");
            return root.ToJsonString(Indented);
        }
        if (existing is not null && existing.EndsWith(ChainSuffix))
        {
            statusLine["command"] = existing[..^ChainSuffix.Length];
            return root.ToJsonString(Indented);
        }
        return json;
    }

    private static JsonObject Parse(string json) =>
        string.IsNullOrWhiteSpace(json) ? new JsonObject() : JsonNode.Parse(json) as JsonObject ?? new JsonObject();
}
