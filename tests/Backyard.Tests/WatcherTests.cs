namespace Backyard.Tests;

public class WatcherTests : IDisposable
{
    private static readonly string FixturesDir = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("backyard-watcher-tests").FullName;

    public void Dispose() => Directory.Delete(_tempDir, true);

    private static string Fixture(string name) => Path.Combine(FixturesDir, name);

    [Fact]
    public void ToolTurn_EmitsOneEventWithToolCounts()
    {
        var events = new Watcher().ReadFile(Fixture("session-tool-turn.jsonl"));

        var e = Assert.Single(events);
        Assert.Equal("aaaaaaaa-0000-0000-0000-000000000001", e.SessionId);
        Assert.Equal("bbbbbbbb-0000-0000-0000-000000000001", e.TurnId);
        Assert.Equal(DateTimeOffset.Parse("2026-10-02T13:45:40.500Z"), e.Timestamp);
        Assert.Equal(1, e.ToolCounts["Read"]);
        Assert.Single(e.ToolCounts);
    }

    [Fact]
    public void NoToolTurn_EmitsNothing()
    {
        var watcher = new Watcher();
        Assert.Empty(watcher.ReadFile(Fixture("session-no-tool-turn.jsonl")));
        Assert.Equal(0, watcher.ParseErrorCount);
    }

    [Fact]
    public void Mixed_CountsParentAgentTurn_IgnoresSidechainAndMeta_DeduplicatesTurnDuration()
    {
        var watcher = new Watcher();
        var events = watcher.ReadFile(Fixture("session-mixed.jsonl"));

        var e = Assert.Single(events);
        Assert.Equal("bbbbbbbb-0000-0000-0000-000000000003", e.TurnId);
        Assert.Equal(1, e.ToolCounts["Agent"]);
        Assert.Single(e.ToolCounts);
        Assert.Equal(2, watcher.ParseErrorCount);
    }

    [Fact]
    public void Scan_ReadsOnlyTopLevelFiles_NotSubagentFolders()
    {
        var events = new Watcher().Scan(FixturesDir);

        Assert.Equal(2, events.Count);
        Assert.DoesNotContain(events, e => e.ToolCounts.ContainsKey("Bash"));
    }

    [Fact]
    public void IncrementalRead_PicksUpAppendedLines_WithoutDoubleCounting()
    {
        var lines = File.ReadAllLines(Fixture("session-tool-turn.jsonl"));
        var path = Path.Combine(_tempDir, "session.jsonl");
        File.WriteAllLines(path, lines[..3]);

        var watcher = new Watcher();
        Assert.Empty(watcher.ReadFile(path));

        File.AppendAllLines(path, lines[3..]);
        Assert.Single(watcher.ReadFile(path));
        Assert.Empty(watcher.ReadFile(path));
    }

    [Fact]
    public void IncrementalRead_IgnoresPartialTrailingLine_UntilNewlineArrives()
    {
        var content = File.ReadAllText(Fixture("session-no-tool-turn.jsonl")).Replace("\r\n", "\n");
        var path = Path.Combine(_tempDir, "session.jsonl");
        File.WriteAllText(path, content[..^20]);

        var watcher = new Watcher();
        watcher.ReadFile(path);
        var errorsBeforeCompletion = watcher.ParseErrorCount;

        File.WriteAllText(path, content);
        watcher.ReadFile(path);

        Assert.Equal(0, errorsBeforeCompletion);
        Assert.Equal(0, watcher.ParseErrorCount);
    }

    [Fact]
    public void TruncatedFile_IsReadFromStart_AndDeduplicated()
    {
        var source = File.ReadAllBytes(Fixture("session-tool-turn.jsonl"));
        var path = Path.Combine(_tempDir, "session.jsonl");
        File.WriteAllBytes(path, source);

        var watcher = new Watcher();
        Assert.Single(watcher.ReadFile(path));

        File.WriteAllBytes(path, source[..(source.Length / 2)]);
        Assert.Empty(watcher.ReadFile(path));

        File.WriteAllBytes(path, source);
        Assert.Empty(watcher.ReadFile(path));
    }

    [Fact]
    public void SameTurnInRerun_IsDeduplicatedAcrossReads()
    {
        var watcher = new Watcher();
        var path = Path.Combine(_tempDir, "session.jsonl");
        File.Copy(Fixture("session-tool-turn.jsonl"), path);

        Assert.Single(watcher.ReadFile(path));
        File.AppendAllLines(path, File.ReadAllLines(Fixture("session-tool-turn.jsonl")));
        Assert.Empty(watcher.ReadFile(path));
    }
}
