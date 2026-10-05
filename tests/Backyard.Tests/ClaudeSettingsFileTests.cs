using System.Text.Json.Nodes;

namespace Backyard.Tests;

public sealed class ClaudeSettingsFileTests
{
    private const string Cmd = "backyard status";
    private const string Other = """{"model":"opus","statusLine":{"type":"command","command":"my-status.sh","refreshInterval":2}}""";

    private static JsonObject StatusLine(string json) => (JsonObject)JsonNode.Parse(json)!["statusLine"]!;

    [Fact]
    public void Add_CreatesStatusLine_WhenMissing()
    {
        var statusLine = StatusLine(ClaudeSettingsFile.AddStatusline("{}", Cmd));
        Assert.Equal("command", (string?)statusLine["type"]);
        Assert.Equal("backyard status", (string?)statusLine["command"]);
        Assert.Equal(1, (int?)statusLine["refreshInterval"]);
    }

    [Fact]
    public void Add_ChainsAfterExistingCommand_PreservingOtherKeys()
    {
        var result = ClaudeSettingsFile.AddStatusline(Other, Cmd);
        var root = JsonNode.Parse(result)!;
        Assert.Equal("opus", (string?)root["model"]);
        Assert.Equal("my-status.sh && backyard status", (string?)root["statusLine"]!["command"]);
        Assert.Equal(2, (int?)root["statusLine"]!["refreshInterval"]);
    }

    [Fact]
    public void Add_ReturnsSameReference_WhenAlreadyPresent()
    {
        var input = ClaudeSettingsFile.AddStatusline(Other, Cmd);
        Assert.Same(input, ClaudeSettingsFile.AddStatusline(input, Cmd));
    }

    [Fact]
    public void Add_TreatsEmptyInputAsEmptyObject()
    {
        Assert.Equal(ClaudeSettingsFile.AddStatusline("{}", Cmd), ClaudeSettingsFile.AddStatusline("", Cmd));
    }

    [Fact]
    public void Remove_RestoresOriginalCommand()
    {
        var root = JsonNode.Parse(ClaudeSettingsFile.RemoveStatusline(ClaudeSettingsFile.AddStatusline(Other, Cmd), Cmd))!;
        Assert.Equal("my-status.sh", (string?)root["statusLine"]!["command"]);
        Assert.Equal("opus", (string?)root["model"]);
    }

    [Fact]
    public void Remove_DeletesStatusLine_WhenOnlyBackyard()
    {
        var root = (JsonObject)JsonNode.Parse(ClaudeSettingsFile.RemoveStatusline(ClaudeSettingsFile.AddStatusline("{}", Cmd), Cmd))!;
        Assert.False(root.ContainsKey("statusLine"));
    }

    [Fact]
    public void Remove_ReturnsSameReference_WhenNoStatusLine()
    {
        const string input = """{"model":"opus"}""";
        Assert.Same(input, ClaudeSettingsFile.RemoveStatusline(input, Cmd));
    }

    [Fact]
    public void Add_ReplacesStaleBackyardSegment_AfterToolUpdate()
    {
        const string old = """{"statusLine":{"type":"command","command":"my-status.sh && \"C:/old/backyard.exe\" status"}}""";
        var result = ClaudeSettingsFile.AddStatusline(old, "\"C:/new/backyard.exe\" status");
        Assert.Equal("my-status.sh && \"C:/new/backyard.exe\" status", (string?)StatusLine(result)["command"]);
    }

    [Fact]
    public void StableExePath_ReplacesStoreVersionDir_WithToolsShim()
    {
        var toolsDir = Path.Combine(Path.GetTempPath(), ".dotnet", "tools");
        var store = Path.Combine(toolsDir, ".store", "backyard-farm", "0.2.1", "backyard-farm.win-x64", "0.2.1", "tools", "any", "win-x64", "backyard.exe");
        var expected = Path.Combine(toolsDir, OperatingSystem.IsWindows() ? "backyard.cmd" : "backyard");
        Assert.Equal(expected, ClaudeSettingsFile.StableExePath(store));
    }

    [Fact]
    public void StableExePath_KeepsOtherPaths()
    {
        Assert.Equal(@"C:\dev\publish\backyard.exe", ClaudeSettingsFile.StableExePath(@"C:\dev\publish\backyard.exe"));
        Assert.Null(ClaudeSettingsFile.StableExePath(null));
    }
}
