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
        Assert.Equal(1, watcher.ParseErrorCount);
        Assert.Equal(1, watcher.UnknownLineCount);
    }

    private string MakeProjectDir(string name = "project-a")
    {
        var dir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Scan_ReadsAllProjectSubfolders_NotSubagentFolders()
    {
        var projectA = MakeProjectDir("project-a");
        var projectB = MakeProjectDir("project-b");
        File.Copy(Fixture("session-tool-turn.jsonl"), Path.Combine(projectA, "session-tool-turn.jsonl"));
        File.Copy(Fixture("session-mixed.jsonl"), Path.Combine(projectB, "session-mixed.jsonl"));
        var subagents = Path.Combine(projectB, "aaaaaaaa-0000-0000-0000-000000000003", "subagents");
        Directory.CreateDirectory(subagents);
        File.Copy(Fixture(Path.Combine("subagents", "agent-dummy.jsonl")), Path.Combine(subagents, "agent-dummy.jsonl"));

        var cursors = new Dictionary<string, WatcherCursor>();
        var events = new Watcher(cursors).Scan(_tempDir);

        Assert.Equal(2, events.Count);
        Assert.DoesNotContain(events, e => e.ToolCounts.ContainsKey("Bash"));
        Assert.Equal(2, cursors.Count);
    }

    [Fact]
    public void Scan_WithSavedOffsets_DoesNotReemitEvents()
    {
        var projectDir = MakeProjectDir();
        File.Copy(Fixture("session-tool-turn.jsonl"), Path.Combine(projectDir, "session-tool-turn.jsonl"));

        var cursors = new Dictionary<string, WatcherCursor>();
        Assert.Single(new Watcher(cursors).Scan(_tempDir));
        Assert.Empty(new Watcher(cursors).Scan(_tempDir));
    }

    [Fact]
    public void Scan_RemovesOffsetsForMissingFiles()
    {
        var projectDir = MakeProjectDir();
        var path = Path.Combine(projectDir, "session-tool-turn.jsonl");
        File.Copy(Fixture("session-tool-turn.jsonl"), path);

        var cursors = new Dictionary<string, WatcherCursor> { ["gone-session"] = new() { Offset = 123 } };
        new Watcher(cursors).Scan(_tempDir);

        Assert.True(cursors.ContainsKey("session-tool-turn"));
        Assert.False(cursors.ContainsKey("gone-session"));
    }

    [Fact]
    public void Scan_TruncatedFile_IsReadFromStart()
    {
        var projectDir = MakeProjectDir();
        var path = Path.Combine(projectDir, "session-tool-turn.jsonl");
        var source = File.ReadAllBytes(Fixture("session-tool-turn.jsonl"));
        File.WriteAllBytes(path, source);

        var cursors = new Dictionary<string, WatcherCursor>();
        Assert.Single(new Watcher(cursors).Scan(_tempDir));

        var watcher = new Watcher(cursors);
        File.WriteAllBytes(path, source[..(source.Length / 2)]);
        Assert.Empty(watcher.Scan(_tempDir));
        Assert.True(cursors["session-tool-turn"].Offset <= source.Length / 2);

        File.WriteAllBytes(path, source);
        Assert.Single(watcher.Scan(_tempDir));
    }

    [Fact]
    public void TurnSplitAcrossProcesses_StillEmitsEvent_WhenCursorsArePersisted()
    {
        var projectDir = MakeProjectDir();
        var lines = File.ReadAllLines(Fixture("session-tool-turn.jsonl"));
        var path = Path.Combine(projectDir, "session-tool-turn.jsonl");
        File.WriteAllLines(path, lines[..3]);

        var cursors = new Dictionary<string, WatcherCursor>();
        Assert.Empty(new Watcher(cursors).Scan(_tempDir));

        File.AppendAllLines(path, lines[3..]);
        Assert.Single(new Watcher(cursors).Scan(_tempDir));
        Assert.Empty(new Watcher(cursors).Scan(_tempDir));
    }

    [Fact]
    public void LatestSignal_IsWaiting_WhenLastRecordIsTurnDuration()
    {
        var watcher = new Watcher();
        watcher.ReadFile(Fixture("session-tool-turn.jsonl"));

        Assert.NotNull(watcher.LatestSignal);
        Assert.Equal(AgentSignalKind.Waiting, watcher.LatestSignal!.Kind);
        Assert.Equal(DateTimeOffset.Parse("2026-10-02T13:45:40.500Z"), watcher.LatestSignal.Timestamp);
    }

    [Fact]
    public void LatestSignal_IsWorking_WhenRecordsFollowTurnDuration()
    {
        var lines = File.ReadAllLines(Fixture("session-tool-turn.jsonl"));
        var path = Path.Combine(_tempDir, "session.jsonl");
        File.WriteAllLines(path, lines[..^1]);

        var watcher = new Watcher();
        watcher.ReadFile(path);

        Assert.NotNull(watcher.LatestSignal);
        Assert.Equal(AgentSignalKind.Working, watcher.LatestSignal!.Kind);
    }

    [Fact]
    public void LatestSignal_IsNull_WhenNothingRead()
    {
        Assert.Null(new Watcher().LatestSignal);
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
