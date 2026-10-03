namespace Backyard.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("backyard-test").FullName;

    private Storage NewStorage() => new(Path.Combine(_dir, "state.json"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_ReturnsNull_WhenFileMissing()
    {
        Assert.Null(NewStorage().Load());
    }

    [Fact]
    public void Load_ReturnsNull_AndBacksUp_WhenJsonCorrupt()
    {
        var storage = NewStorage();
        File.WriteAllText(storage.FilePath, "{not json");
        Assert.Null(storage.Load());
        Assert.True(File.Exists(storage.FilePath + ".bak"));
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var storage = NewStorage();
        var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(9));
        var saved = new SavedState
        {
            Coins = 42,
            Rows = 2,
            Cells = new FarmCell?[20],
            FirstRunAt = now,
            LastWateredAt = now.AddMinutes(5),
            LastEventAt = now.AddMinutes(7),
            HarvestCounts = { ["carrot"] = 3 },
            FirstHarvestAt = { ["carrot"] = now.AddMinutes(3) },
            WatcherCursors = { ["session-a"] = new WatcherCursor { Offset = 1234, TurnId = "turn-1", SessionId = "session-a", ToolCounts = { ["Read"] = 2 } } },
            Diag = { ParseErrors = 1, UnknownLines = 2, LastDetectedAt = now.AddMinutes(7) },
        };
        saved.Cells[1] = new FarmCell { Crop = "potato", Stage = 2, Watered = true, StageStartedAt = now };

        storage.Save(saved);
        var loaded = storage.Load();

        Assert.NotNull(loaded);
        Assert.Equal(42, loaded.Coins);
        Assert.Equal(2, loaded.Rows);
        Assert.Equal(20, loaded.Cells.Length);
        Assert.Null(loaded.Cells[0]);
        Assert.Equal("potato", loaded.Cells[1]!.Crop);
        Assert.Equal(2, loaded.Cells[1]!.Stage);
        Assert.True(loaded.Cells[1]!.Watered);
        Assert.Equal(now, loaded.Cells[1]!.StageStartedAt);
        Assert.Equal(now, loaded.FirstRunAt);
        Assert.Equal(now.AddMinutes(5), loaded.LastWateredAt);
        Assert.Equal(now.AddMinutes(7), loaded.LastEventAt);
        Assert.Equal(3, loaded.HarvestCounts["carrot"]);
        Assert.Equal(now.AddMinutes(3), loaded.FirstHarvestAt["carrot"]);
        Assert.Equal(1234L, loaded.WatcherCursors["session-a"].Offset);
        Assert.Equal("turn-1", loaded.WatcherCursors["session-a"].TurnId);
        Assert.Equal(2, loaded.WatcherCursors["session-a"].ToolCounts["Read"]);
        Assert.Equal(1, loaded.Diag.ParseErrors);
        Assert.Equal(2, loaded.Diag.UnknownLines);
        Assert.Equal(now.AddMinutes(7), loaded.Diag.LastDetectedAt);
    }

    [Fact]
    public void SaveThenLoad_PreservesNullOptionals()
    {
        var storage = NewStorage();
        storage.Save(new SavedState { Cells = new FarmCell?[10] });
        var loaded = storage.Load();
        Assert.NotNull(loaded);
        Assert.Null(loaded.LastWateredAt);
        Assert.Null(loaded.LastEventAt);
        Assert.Null(loaded.Diag.LastDetectedAt);
        Assert.All(loaded.Cells, Assert.Null);
    }
}
