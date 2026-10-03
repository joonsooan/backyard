using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backyard;

public enum AgentSignalKind { Working, Waiting }

public sealed record AgentSignal(AgentSignalKind Kind, DateTimeOffset Timestamp);

public sealed record WatchEvent(
    string SessionId,
    string TurnId,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, int> ToolCounts);

public sealed class TranscriptLine
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("subtype")] public string? Subtype { get; set; }
    [JsonPropertyName("sessionId")] public string? SessionId { get; set; }
    [JsonPropertyName("promptId")] public string? PromptId { get; set; }
    [JsonPropertyName("isSidechain")] public bool? IsSidechain { get; set; }
    [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }
    [JsonPropertyName("message")] public TranscriptMessage? Message { get; set; }
}

public sealed class TranscriptMessage
{
    [JsonPropertyName("content")] public JsonElement Content { get; set; }
}

public sealed class WatcherCursor
{
    public long Offset { get; set; }
    public string? TurnId { get; set; }
    public string? SessionId { get; set; }
    public Dictionary<string, int> ToolCounts { get; set; } = new();
}

public sealed class Watcher(Dictionary<string, WatcherCursor>? cursors = null)
{
    private static readonly string[] KnownIgnoredTypes =
    [
        "attachment", "mode", "permission-mode", "ai-title", "last-prompt",
        "file-history-snapshot", "file-history-delta", "queue-operation"
    ];

    private readonly Dictionary<string, WatcherCursor> _cursors = cursors ?? new();
    private readonly HashSet<string> _seenTurns = new();

    public int ParseErrorCount { get; private set; }
    public int UnknownLineCount { get; private set; }
    public DateTimeOffset? LastEventTimestamp { get; private set; }
    public AgentSignal? LatestSignal { get; private set; }

    public List<WatchEvent> Scan(string projectsRoot)
    {
        var events = new List<WatchEvent>();
        var seen = new HashSet<string>();
        if (Directory.Exists(projectsRoot))
        {
            foreach (var projectDir in Directory.EnumerateDirectories(projectsRoot))
                foreach (var path in Directory.EnumerateFiles(projectDir, "*.jsonl", SearchOption.TopDirectoryOnly))
                {
                    seen.Add(Path.GetFileNameWithoutExtension(path));
                    ReadFile(path, events);
                }
        }
        foreach (var key in _cursors.Keys.Where(k => !seen.Contains(k)).ToList())
            _cursors.Remove(key);
        return events;
    }

    public List<WatchEvent> ReadFile(string path)
    {
        var events = new List<WatchEvent>();
        ReadFile(path, events);
        return events;
    }

    private void ReadFile(string path, List<WatchEvent> events)
    {
        var sessionKey = Path.GetFileNameWithoutExtension(path);
        if (!_cursors.TryGetValue(sessionKey, out var cursor))
            _cursors[sessionKey] = cursor = new WatcherCursor();

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < cursor.Offset)
        {
            cursor.Offset = 0;
            cursor.TurnId = null;
            cursor.ToolCounts = new();
        }
        if (stream.Length == cursor.Offset)
            return;

        stream.Seek(cursor.Offset, SeekOrigin.Begin);
        var buffer = new byte[stream.Length - cursor.Offset];
        stream.ReadExactly(buffer);

        var consumable = Array.LastIndexOf(buffer, (byte)'\n') + 1;
        if (consumable == 0)
            return;

        var text = Encoding.UTF8.GetString(buffer, 0, consumable);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length > 0)
                ProcessLine(line, cursor, events);
        }
        cursor.Offset += consumable;
    }

    private void ProcessLine(string line, WatcherCursor cursor, List<WatchEvent> events)
    {
        TranscriptLine? record;
        try
        {
            record = JsonSerializer.Deserialize(line, BackyardJson.Default.TranscriptLine);
        }
        catch (JsonException)
        {
            ParseErrorCount++;
            return;
        }
        if (record?.Type is null)
        {
            ParseErrorCount++;
            return;
        }
        if (record.IsSidechain == true)
            return;

        TrackSignal(record);

        switch (record.Type)
        {
            case "user" when record.PromptId is not null && StartsTurn(record.Message):
                cursor.TurnId = record.PromptId;
                cursor.SessionId = record.SessionId;
                cursor.ToolCounts = new();
                break;
            case "user":
            case "system" when record.Subtype != "turn_duration":
                break;
            case "assistant":
                CountToolUses(record.Message, cursor.ToolCounts);
                break;
            case "system":
                EndTurn(record, cursor, events);
                break;
            default:
                if (!KnownIgnoredTypes.Contains(record.Type))
                    UnknownLineCount++;
                break;
        }
    }

    private void TrackSignal(TranscriptLine record)
    {
        var isTurnEnd = record.Type == "system" && record.Subtype == "turn_duration";
        if (!isTurnEnd && record.Type is not ("user" or "assistant"))
            return;
        if (!DateTimeOffset.TryParse(record.Timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var timestamp))
            return;
        if (LatestSignal is not null && timestamp < LatestSignal.Timestamp)
            return;
        var kind = isTurnEnd ? AgentSignalKind.Waiting : AgentSignalKind.Working;
        LatestSignal = new AgentSignal(kind, timestamp);
    }

    private static bool StartsTurn(TranscriptMessage? message)
    {
        if (message is null)
            return false;
        var content = message.Content;
        if (content.ValueKind == JsonValueKind.String)
            return true;
        if (content.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var block in content.EnumerateArray())
            if (!block.TryGetProperty("type", out var type) || type.GetString() != "tool_result")
                return true;
        return false;
    }

    private static void CountToolUses(TranscriptMessage? message, Dictionary<string, int> toolCounts)
    {
        if (message is null || message.Content.ValueKind != JsonValueKind.Array)
            return;
        foreach (var block in message.Content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.GetString() == "tool_use"
                && block.TryGetProperty("name", out var name) && name.GetString() is { } toolName)
                toolCounts[toolName] = toolCounts.GetValueOrDefault(toolName) + 1;
        }
    }

    private void EndTurn(TranscriptLine record, WatcherCursor cursor, List<WatchEvent> events)
    {
        var turnId = cursor.TurnId;
        var sessionId = cursor.SessionId ?? record.SessionId;
        var toolCounts = cursor.ToolCounts;
        cursor.TurnId = null;
        cursor.ToolCounts = new();

        if (turnId is null || sessionId is null || toolCounts.Count == 0)
            return;
        if (!_seenTurns.Add($"{sessionId}|{turnId}"))
            return;
        if (!DateTimeOffset.TryParse(record.Timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var timestamp))
            return;

        LastEventTimestamp = timestamp;
        events.Add(new WatchEvent(sessionId, turnId, timestamp, toolCounts));
    }
}
