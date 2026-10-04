using System.Text.Json.Nodes;

namespace Backyard.Tests;

public sealed class ClaudeSettingsFileTests
{
    private const string Other = """{"model":"opus","statusLine":{"type":"command","command":"my-status.sh","refreshInterval":2}}""";

    private static JsonObject StatusLine(string json) => (JsonObject)JsonNode.Parse(json)!["statusLine"]!;

    [Fact]
    public void Add_CreatesStatusLine_WhenMissing()
    {
        var statusLine = StatusLine(ClaudeSettingsFile.AddStatusline("{}"));
        Assert.Equal("command", (string?)statusLine["type"]);
        Assert.Equal("backyard status", (string?)statusLine["command"]);
        Assert.Equal(1, (int?)statusLine["refreshInterval"]);
    }

    [Fact]
    public void Add_ChainsAfterExistingCommand_PreservingOtherKeys()
    {
        var result = ClaudeSettingsFile.AddStatusline(Other);
        var root = JsonNode.Parse(result)!;
        Assert.Equal("opus", (string?)root["model"]);
        Assert.Equal("my-status.sh && backyard status", (string?)root["statusLine"]!["command"]);
        Assert.Equal(2, (int?)root["statusLine"]!["refreshInterval"]);
    }

    [Fact]
    public void Add_ReturnsSameReference_WhenAlreadyPresent()
    {
        var input = ClaudeSettingsFile.AddStatusline(Other);
        Assert.Same(input, ClaudeSettingsFile.AddStatusline(input));
    }

    [Fact]
    public void Add_TreatsEmptyInputAsEmptyObject()
    {
        Assert.Equal(ClaudeSettingsFile.AddStatusline("{}"), ClaudeSettingsFile.AddStatusline(""));
    }

    [Fact]
    public void Remove_RestoresOriginalCommand()
    {
        var root = JsonNode.Parse(ClaudeSettingsFile.RemoveStatusline(ClaudeSettingsFile.AddStatusline(Other)))!;
        Assert.Equal("my-status.sh", (string?)root["statusLine"]!["command"]);
        Assert.Equal("opus", (string?)root["model"]);
    }

    [Fact]
    public void Remove_DeletesStatusLine_WhenOnlyBackyard()
    {
        var root = (JsonObject)JsonNode.Parse(ClaudeSettingsFile.RemoveStatusline(ClaudeSettingsFile.AddStatusline("{}")))!;
        Assert.False(root.ContainsKey("statusLine"));
    }

    [Fact]
    public void Remove_ReturnsSameReference_WhenNoStatusLine()
    {
        const string input = """{"model":"opus"}""";
        Assert.Same(input, ClaudeSettingsFile.RemoveStatusline(input));
    }
}
