namespace Backyard.Tests;

public class GrowthTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int minutes) => T0 + TimeSpan.FromMinutes(minutes);

    private static WatchEvent EventAt(int minutes, string turnId = "t") =>
        new("session", turnId + minutes, At(minutes), new Dictionary<string, int> { ["Bash"] = 1 });

    private static CropInfo Crop(string name) => Balance.Crop(name)!;

    private static FarmState AllUnlocked()
    {
        var state = new FarmState(T0);
        state.UnlockedCrops = Balance.Crops.Select(c => c.Name).ToList();
        return state;
    }

    [Fact]
    public void TryPlant_FailsForLockedCrop()
    {
        var state = new FarmState(T0);
        state.Coins = 100;
        Assert.Equal(["carrot"], state.UnlockedCrops);
        Assert.False(state.TryPlant(1, "radish", T0));
        Assert.Equal(100, state.Coins);
    }

    [Fact]
    public void TryUnlock_FollowsCatalogOrderAndChargesUnlockPrice()
    {
        var state = new FarmState(T0);
        var radish = Crop("radish");
        state.Coins = radish.UnlockPrice - 1;
        Assert.Equal("radish", state.NextUnlock!.Name);
        Assert.False(state.TryUnlock());
        state.Coins = radish.UnlockPrice;
        Assert.True(state.TryUnlock());
        Assert.Equal(0, state.Coins);
        Assert.Equal(["carrot", "radish"], state.UnlockedCrops);
        Assert.Equal("potato", state.NextUnlock!.Name);
        Assert.False(state.IsUnlocked("potato"));
        state.Coins = radish.SeedPrice;
        Assert.True(state.TryPlant(1, "radish", T0));
    }

    [Fact]
    public void TryUnlock_FailsWhenEverythingIsUnlocked()
    {
        var state = AllUnlocked();
        state.Coins = 10000;
        Assert.Null(state.NextUnlock);
        Assert.False(state.TryUnlock());
        Assert.Equal(10000, state.Coins);
    }

    [Fact]
    public void CreateNew_StartsWithStartCoinsOneRowAndFreeCarrot()
    {
        var state = new FarmState(T0);

        Assert.Equal(Balance.StartCoins, state.Coins);
        Assert.Equal(1, state.Rows);
        Assert.Equal(Balance.Columns, state.Cells.Length);
        Assert.Equal("carrot", state.Cells[0]!.Crop);
        Assert.Equal(0, state.Cells[0]!.Stage);
        Assert.All(state.Cells.Skip(1), c => Assert.Null(c));
        Assert.Equal(T0, state.FirstRunAt);
    }

    [Fact]
    public void Carrot_RipensAfterOneWateringAndStageMinutes()
    {
        var state = new FarmState(T0);

        state.Apply([EventAt(1)], At(1 + Crop("carrot").StageMinutes));

        Assert.Equal(1, state.Cells[0]!.Stage);
        Assert.True(state.Cells[0]!.IsRipe);
        Assert.False(state.Cells[0]!.Watered);
    }

    [Fact]
    public void Stage_WaitsWithWaterUntilStageMinutesPass()
    {
        var state = new FarmState(T0);

        state.Apply([EventAt(1)], At(Crop("carrot").StageMinutes - 1));

        Assert.Equal(0, state.Cells[0]!.Stage);
        Assert.True(state.Cells[0]!.Watered);
    }

    [Fact]
    public void Stage_WaitsWithoutWaterEvenAfterDays()
    {
        var state = new FarmState(T0);

        state.Apply([], T0 + TimeSpan.FromDays(5));

        Assert.Equal(0, state.Cells[0]!.Stage);
        Assert.False(state.Cells[0]!.Watered);
    }

    [Fact]
    public void Water_IsIgnoredWhenStageAlreadyWatered()
    {
        var state = new FarmState(T0);

        state.Apply([EventAt(1), EventAt(2), EventAt(3)], At(Crop("carrot").StageMinutes - 1));

        Assert.Equal(0, state.Cells[0]!.Stage);
        Assert.True(state.Cells[0]!.Watered);
    }

    [Fact]
    public void OfflineReplay_ThreeEventsRaisePotatoToReady()
    {
        var state = AllUnlocked();
        var potato = Crop("potato");
        state.Coins = potato.SeedPrice;
        state.Cells[0] = null;
        Assert.True(state.TryPlant(1, "potato", T0));
        var coinsAfterPlanting = state.Coins;

        var m = potato.StageMinutes;
        state.Apply([EventAt(m), EventAt(4 * m), EventAt(7 * m)], At(10 * m));

        Assert.Equal(potato.MaxStage, state.Cells[1]!.Stage);
        Assert.Equal(coinsAfterPlanting, state.Coins);
        Assert.False(state.HarvestCounts.ContainsKey("potato"));
    }

    [Fact]
    public void Pumpkin_NeedsItsOwnStageMinutes()
    {
        var state = AllUnlocked();
        var pumpkin = Crop("pumpkin");
        state.Coins = pumpkin.SeedPrice;
        Assert.True(state.TryPlant(1, "pumpkin", T0));

        state.Apply([EventAt(0)], At(pumpkin.StageMinutes - 1));
        Assert.Equal(0, state.Cells[1]!.Stage);

        state.Apply([], At(pumpkin.StageMinutes));
        Assert.Equal(1, state.Cells[1]!.Stage);
    }

    [Fact]
    public void ReadyCrop_StaysUntilManualHarvest()
    {
        var state = new FarmState(T0);

        state.Apply([EventAt(0), EventAt(15), EventAt(40)], At(60));

        Assert.Equal(1, state.Cells[0]!.Stage);
        Assert.Equal(Balance.StartCoins, state.Coins);

        Assert.True(state.TryHarvest(0, At(60)));
        Assert.Equal(1, state.HarvestCounts["carrot"]);
        Assert.Equal(At(60), state.FirstHarvestAt["carrot"]);
        Assert.Equal(Balance.StartCoins + Crop("carrot").SellPrice, state.Coins);
        Assert.Null(state.Cells[0]);
    }

    [Fact]
    public void Events_BeforeFirstRun_AreIgnored()
    {
        var state = new FarmState(At(10));

        state.Apply([EventAt(5)], At(15));

        Assert.False(state.Cells[0]!.Watered);
    }

    [Fact]
    public void TryPlant_FailsOnOccupiedCellAndWithoutCoins()
    {
        var state = AllUnlocked();
        state.Coins = Crop("potato").SeedPrice + Crop("radish").SeedPrice;

        Assert.False(state.TryPlant(0, "carrot", T0));
        Assert.True(state.TryPlant(1, "potato", T0));
        Assert.True(state.TryPlant(2, "radish", T0));
        Assert.False(state.TryPlant(3, "carrot", T0));
        Assert.False(state.TryPlant(3, "unknown", T0));
        Assert.Equal(0, state.Coins);
    }

    [Fact]
    public void TryExpand_ChargesRowPricesAndStopsAtMaxRows()
    {
        var state = new FarmState(T0);
        state.Coins = Balance.SecondRowPrice + Balance.ThirdRowPrice;

        Assert.True(state.TryExpand());
        Assert.Equal(2, state.Rows);
        Assert.Equal(2 * Balance.Columns, state.Cells.Length);
        Assert.Equal(Balance.ThirdRowPrice, state.Coins);

        Assert.True(state.TryExpand());
        Assert.Equal(3, state.Rows);
        Assert.Equal(3 * Balance.Columns, state.Cells.Length);
        Assert.Equal(0, state.Coins);

        Assert.Equal(2 * Balance.ThirdRowPrice, Balance.NextRowPrice(3));
        Assert.Equal(128 * Balance.ThirdRowPrice, Balance.NextRowPrice(9));

        state.Coins = Enumerable.Range(3, Balance.MaxRows - 3).Sum(Balance.NextRowPrice);
        while (state.Rows < Balance.MaxRows)
            Assert.True(state.TryExpand());
        Assert.Equal(0, state.Coins);
        Assert.Equal(Balance.MaxRows * Balance.Columns, state.Cells.Length);

        state.Coins = 50000;
        Assert.False(state.TryExpand());
    }

    [Fact]
    public void TryExpand_FailsWithoutCoins()
    {
        var state = new FarmState(T0);

        Assert.False(state.TryExpand());
        Assert.Equal(1, state.Rows);
        Assert.Equal(Balance.StartCoins, state.Coins);
    }

    [Fact]
    public void TryHarvest_FailsOnGrowingOrEmptyCell()
    {
        var state = new FarmState(T0);

        Assert.False(state.TryHarvest(0, T0));
        Assert.False(state.TryHarvest(5, T0));
    }

    [Fact]
    public void TryRemove_ClearsPlantedCellOnly()
    {
        var state = new FarmState(T0);

        Assert.True(state.TryRemove(0));
        Assert.Null(state.Cells[0]);
        Assert.False(state.TryRemove(0));
        Assert.False(state.TryRemove(99));
        Assert.Equal(Balance.StartCoins, state.Coins);
    }

    [Fact]
    public void FullField_HasNoRoomToPlant()
    {
        var state = new FarmState(T0);
        state.Coins = Balance.Columns * Crop("carrot").SeedPrice;
        for (var i = 1; i < state.Cells.Length; i++)
            Assert.True(state.TryPlant(i, "carrot", T0));

        Assert.False(state.TryPlant(0, "carrot", T0));
        Assert.False(state.TryPlant(Balance.Columns, "carrot", T0));
    }
}
