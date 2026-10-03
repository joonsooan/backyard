using Xunit.Abstractions;

namespace Backyard.Tests;

public class BalanceSimTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const int EventIntervalMinutes = 20;

    public static FarmState Simulate(int hours, Action<int, FarmState>? onRowUnlocked = null)
    {
        var state = new FarmState(T0);
        var rows = state.Rows;
        for (var minute = EventIntervalMinutes; minute <= hours * 60; minute += EventIntervalMinutes)
        {
            var now = T0 + TimeSpan.FromMinutes(minute);
            state.Apply([new WatchEvent("sim", $"t{minute}", now, new Dictionary<string, int> { ["Bash"] = 1 })], now);
            HarvestAndReplant(state, now);
            while (state.TryExpand())
                PlantBest(state, now);
            if (state.Rows != rows)
            {
                rows = state.Rows;
                onRowUnlocked?.Invoke(minute, state);
            }
        }
        return state;
    }

    private static void HarvestAndReplant(FarmState state, DateTimeOffset now)
    {
        for (var i = 0; i < state.Cells.Length; i++)
        {
            if (state.Cells[i] is { } cell && cell.Stage >= Balance.CropOrFallback(cell.Crop).MaxStage)
                state.TryHarvest(i, now);
        }
        PlantBest(state, now);
    }

    private static void PlantBest(FarmState state, DateTimeOffset now)
    {
        var byProfit = Balance.Crops.Where(c => !c.Retired).OrderByDescending(c => c.SeedPrice).ToArray();
        for (var i = 0; i < state.Cells.Length; i++)
        {
            if (state.Cells[i] is not null)
                continue;
            foreach (var crop in byProfit)
            {
                if (state.TryPlant(i, crop.Name, now))
                    break;
            }
        }
    }

    [Fact]
    public void PrintCurve()
    {
        Simulate(hours: 24 * 14, (minute, s) =>
            output.WriteLine($"row {s.Rows,2} at {minute / 60.0,7:F1}h  coins {s.Coins}"));
    }

    [Fact]
    public void SecondRow_UnlocksWithinFirstWorkDay()
    {
        var unlockedAt = int.MaxValue;
        Simulate(hours: 24, (minute, s) => { if (s.Rows == 2) unlockedAt = Math.Min(unlockedAt, minute); });
        Assert.InRange(unlockedAt, 60, 8 * 60);
    }

    [Fact]
    public void ThirdRow_UnlocksWithinTwoWorkDays()
    {
        var unlockedAt = int.MaxValue;
        Simulate(hours: 48, (minute, s) => { if (s.Rows == 3) unlockedAt = Math.Min(unlockedAt, minute); });
        Assert.InRange(unlockedAt, 2 * 60, 16 * 60);
    }

    [Fact]
    public void PricierSeed_IsAlwaysMoreProfitablePerHour()
    {
        var ordered = Balance.Crops.OrderBy(c => c.SeedPrice).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            var prev = ordered[i - 1];
            var cur = ordered[i];
            Assert.True(ProfitPerStage(cur) > ProfitPerStage(prev), $"{cur.Name} must beat {prev.Name}");
        }
    }

    private static double ProfitPerStage(CropInfo c) => (double)(c.SellPrice - c.SeedPrice) / c.MaxStage;
}
