namespace Backyard.Tests;

public class GrowthTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int minutes) => T0 + TimeSpan.FromMinutes(minutes);

    private static WatchEvent EventAt(int minutes, string turnId = "t") =>
        new("session", turnId + minutes, At(minutes), new Dictionary<string, int> { ["Bash"] = 1 });

    [Fact]
    public void CreateNew_StartsWithTenCoinsOneRowAndFreeCarrot()
    {
        var state = FarmState.CreateNew(T0);

        Assert.Equal(10, state.Coins);
        Assert.Equal(1, state.Rows);
        Assert.Equal(10, state.Cells.Length);
        Assert.Equal("carrot", state.Cells[0]!.Crop);
        Assert.Equal(0, state.Cells[0]!.Stage);
        Assert.All(state.Cells.Skip(1), c => Assert.Null(c));
        Assert.Equal(T0, state.FirstRunAt);
    }

    [Fact]
    public void Stage_RisesAfterTenMinutesWithWater()
    {
        var state = FarmState.CreateNew(T0);

        state.Apply([EventAt(1)], At(15));

        Assert.Equal(1, state.Cells[0]!.Stage);
        Assert.False(state.Cells[0]!.Watered);
    }

    [Fact]
    public void Stage_WaitsWithWaterUntilTenMinutesPass()
    {
        var state = FarmState.CreateNew(T0);

        state.Apply([EventAt(1)], At(5));

        Assert.Equal(0, state.Cells[0]!.Stage);
        Assert.True(state.Cells[0]!.Watered);
    }

    [Fact]
    public void Stage_WaitsWithoutWaterEvenAfterDays()
    {
        var state = FarmState.CreateNew(T0);

        state.Apply([], T0 + TimeSpan.FromDays(5));

        Assert.Equal(0, state.Cells[0]!.Stage);
        Assert.False(state.Cells[0]!.Watered);
    }

    [Fact]
    public void Water_IsIgnoredWhenStageAlreadyWatered()
    {
        var state = FarmState.CreateNew(T0);

        state.Apply([EventAt(1), EventAt(2), EventAt(3)], At(5));

        Assert.Equal(0, state.Cells[0]!.Stage);
        Assert.True(state.Cells[0]!.Watered);
        Assert.Equal(At(1), state.LastWateredAt);
    }

    [Fact]
    public void OfflineReplay_ThreeEventsRaisePotatoToReady()
    {
        var state = FarmState.CreateNew(T0);
        state.Cells[0] = null;
        Assert.True(state.TryPlant(1, "potato", T0));
        var coinsAfterPlanting = state.Coins;

        state.Apply([EventAt(10), EventAt(40), EventAt(70)], At(100));

        Assert.Equal(3, state.Cells[1]!.Stage);
        Assert.Equal(coinsAfterPlanting, state.Coins);
        Assert.False(state.HarvestCounts.ContainsKey("potato"));
    }

    [Fact]
    public void ReadyCrop_StaysUntilManualHarvest()
    {
        var state = FarmState.CreateNew(T0);

        state.Apply([EventAt(0), EventAt(15), EventAt(40)], At(60));

        Assert.Equal(2, state.Cells[0]!.Stage);
        Assert.Equal(10, state.Coins);

        Assert.True(state.TryHarvest(0));
        Assert.Equal(1, state.HarvestCounts["carrot"]);
        Assert.Equal(10 + 4, state.Coins);
        Assert.Null(state.Cells[0]);
    }

    [Fact]
    public void Events_BeforeFirstRun_AreIgnored()
    {
        var state = FarmState.CreateNew(At(10));

        state.Apply([EventAt(5)], At(15));

        Assert.False(state.Cells[0]!.Watered);
        Assert.Null(state.LastEventAt);
    }

    [Fact]
    public void TryPlant_FailsOnOccupiedCellAndWithoutCoins()
    {
        var state = FarmState.CreateNew(T0);

        Assert.False(state.TryPlant(0, "carrot", T0));
        Assert.True(state.TryPlant(1, "potato", T0));
        Assert.True(state.TryPlant(2, "potato", T0));
        Assert.False(state.TryPlant(3, "potato", T0));
        Assert.Equal(0, state.Coins);
    }

    [Fact]
    public void TryExpand_ChargesRowPricesAndStopsAtTenRows()
    {
        var state = FarmState.CreateNew(T0);
        state.Coins = 200;

        Assert.True(state.TryExpand());
        Assert.Equal(2, state.Rows);
        Assert.Equal(20, state.Cells.Length);
        Assert.Equal(150, state.Coins);

        Assert.True(state.TryExpand());
        Assert.Equal(3, state.Rows);
        Assert.Equal(30, state.Cells.Length);
        Assert.Equal(0, state.Coins);

        Assert.Equal(300, Balance.NextRowPrice(3));
        Assert.Equal(19200, Balance.NextRowPrice(9));

        state.Coins = 300 + 600 + 1200 + 2400 + 4800 + 9600 + 19200;
        while (state.Rows < 10)
            Assert.True(state.TryExpand());
        Assert.Equal(0, state.Coins);
        Assert.Equal(100, state.Cells.Length);

        state.Coins = 50000;
        Assert.False(state.TryExpand());
    }

    [Fact]
    public void TryExpand_FailsWithoutCoins()
    {
        var state = FarmState.CreateNew(T0);

        Assert.False(state.TryExpand());
        Assert.Equal(1, state.Rows);
        Assert.Equal(10, state.Coins);
    }

    [Fact]
    public void TryHarvest_FailsOnGrowingOrEmptyCell()
    {
        var state = FarmState.CreateNew(T0);

        Assert.False(state.TryHarvest(0));
        Assert.False(state.TryHarvest(5));
    }

    [Fact]
    public void FullField_HasNoRoomToPlant()
    {
        var state = FarmState.CreateNew(T0);
        state.Coins = 100;
        for (var i = 1; i < state.Cells.Length; i++)
            Assert.True(state.TryPlant(i, "carrot", T0));

        Assert.False(state.TryPlant(0, "carrot", T0));
        Assert.False(state.TryPlant(10, "carrot", T0));
    }
}
