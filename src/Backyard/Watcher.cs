using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backyard;

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

public sealed class Watcher
{
    private static readonly string[] KnownIgnoredTypes =
    [
        "attachment", "mode", "permission-mode", "ai-title", "last-prompt",
        "file-history-snapshot", "file-history-delta", "queue-operation"
    ];

    private sealed class FileCursor
    {
        public long Offset;
        public string? TurnId;
        public string? SessionId;
        public Dictionary<string, int> ToolCounts = new();
    }

    private readonly Dictionary<string, FileCursor> _cursors = new();
    private readonly HashSet<string> _seenTurns = new();

    public int ParseErrorCount { get; private set; }
    public DateTimeOffset? LastEventTimestamp { get; private set; }

    public List<WatchEvent> Scan(string projectDirectory)
    {
        var events = new List<WatchEvent>();
        foreach (var path in Directory.EnumerateFiles(projectDirectory, "*.jsonl", SearchOption.TopDirectoryOnly))
            ReadFile(path, events);
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
        if (!_cursors.TryGetValue(path, out var cursor))
            _cursors[path] = cursor = new FileCursor();

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

    private void ProcessLine(string line, FileCursor cursor, List<WatchEvent> events)
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
                    ParseErrorCount++;
                break;
        }
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

    private void EndTurn(TranscriptLine record, FileCursor cursor, List<WatchEvent> events)
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
