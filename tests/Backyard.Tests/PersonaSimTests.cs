using Xunit.Abstractions;

namespace Backyard.Tests;

public sealed record Persona(
    string Name,
    int WorkStartHour,
    int WorkHours,
    int EventIntervalMinutes,
    int CheckInIntervalMinutes,
    bool Weekends,
    string Replant);

public sealed class PersonaReport
{
    public Dictionary<string, double> CropUnlockedDay = [];
    public Dictionary<int, double> RowReachedDay = [];
    public double FirstHarvestHour = -1;
    public int CheckIns;
    public int IdleCheckIns;
    public double RipeCellHours;
    public double PlantedCellHours;
    public int Coins;
    public int Rows;
}

public class PersonaSimTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    public static readonly Persona[] Personas =
    [
        new("dev      6h/day, ev 15m, check 2h (target)", 9, 6, 15, 120, false, "best"),
        new("dev-ev5  6h/day, ev 5m,  check 2h", 9, 6, 5, 120, false, "best"),
        new("dev-ev10 6h/day, ev 10m, check 2h", 9, 6, 10, 120, false, "best"),
        new("dev-ev20 6h/day, ev 20m, check 2h", 9, 6, 20, 120, false, "best"),
        new("light    3h/day, ev 20m, check 1/day", 13, 3, 20, 24 * 60, false, "best"),
        new("heavy    8h/day, ev 10m, check 1h", 9, 8, 10, 60, false, "best"),
        new("hobby    8h/day, ev 10m, check 30m, 7d", 9, 8, 10, 30, true, "best"),
    ];

    public static PersonaReport Run(Persona p, int days)
    {
        var state = new FarmState(T0);
        var r = new PersonaReport();
        var end = T0.AddDays(days);
        var now = T0;
        var nextEvent = T0;
        var nextCheck = T0;
        var lastRows = 1;
        var step = TimeSpan.FromMinutes(5);
        for (; now < end; now += step)
        {
            var working = IsWorking(p, now);
            if (working && now >= nextEvent)
            {
                state.Apply([new WatchEvent("sim", $"t{now.Ticks}", now, new Dictionary<string, int> { ["Edit"] = 1 })], now);
                nextEvent = now + TimeSpan.FromMinutes(p.EventIntervalMinutes);
            }
            else
                state.Settle(now);

            if (working && now >= nextCheck)
            {
                CheckIn(p, state, now, r);
                nextCheck = now + TimeSpan.FromMinutes(p.CheckInIntervalMinutes);
            }

            foreach (var cell in state.Planted)
            {
                r.PlantedCellHours += step.TotalHours;
                if (cell.IsRipe) r.RipeCellHours += step.TotalHours;
            }
            if (state.Rows != lastRows)
            {
                lastRows = state.Rows;
                r.RowReachedDay[lastRows] = (now - T0).TotalDays;
            }
            foreach (var c in state.UnlockedCrops)
                r.CropUnlockedDay.TryAdd(c, (now - T0).TotalDays);
        }
        r.Coins = state.Coins;
        r.Rows = state.Rows;
        return r;
    }

    private static bool IsWorking(Persona p, DateTimeOffset t)
    {
        if (!p.Weekends && t.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;
        return t.Hour >= p.WorkStartHour && t.Hour < p.WorkStartHour + p.WorkHours;
    }

    private static void CheckIn(Persona p, FarmState state, DateTimeOffset now, PersonaReport r)
    {
        r.CheckIns++;
        var acted = false;
        for (var i = 0; i < state.Cells.Length; i++)
        {
            if (state.TryHarvest(i, now))
            {
                acted = true;
                if (r.FirstHarvestHour < 0) r.FirstHarvestHour = (now - T0).TotalHours;
            }
        }

        if (p.Replant == "expand")
        {
            while (state.TryExpand()) acted = true;
            while (state.TryUnlock()) acted = true;
        }
        else if (p.Replant != "carrot")
        {
            while (state.TryUnlock()) acted = true;
            while (state.TryExpand()) acted = true;
        }

        var choices = p.Replant == "carrot"
            ? [Balance.Crops[0]]
            : Balance.Crops.Where(c => !c.Retired).OrderByDescending(c => c.SeedPrice).ToArray();
        for (var i = 0; i < state.Cells.Length; i++)
        {
            if (state.Cells[i] is not null) continue;
            foreach (var crop in choices)
                if (state.TryPlant(i, crop.Name, now)) { acted = true; break; }
        }
        if (!acted) r.IdleCheckIns++;
    }

    [Fact]
    public void PrintPersonaCurves()
    {
        const int days = 28;
        foreach (var p in Personas)
        {
            var r = Run(p, days);
            output.WriteLine($"== {p.Name} ({days}d)");
            output.WriteLine($"   first harvest: {r.FirstHarvestHour:F1}h  final: rows {r.Rows}, coins {r.Coins}");
            output.WriteLine("   unlock: " + string.Join(", ", r.CropUnlockedDay.Where(k => k.Key != "carrot").Select(k => $"{k.Key} d{k.Value:F1}")));
            output.WriteLine("   rows:   " + string.Join(", ", r.RowReachedDay.OrderBy(k => k.Key).Select(k => $"{k.Key}행 d{k.Value:F1}")));
            output.WriteLine($"   check-ins {r.CheckIns}, idle {r.IdleCheckIns} ({100.0 * r.IdleCheckIns / Math.Max(1, r.CheckIns):F0}%), ripe-waiting {100.0 * r.RipeCellHours / Math.Max(1, r.PlantedCellHours):F0}% of planted cell-time");
        }
    }

    [Fact]
    public void TargetDev_ReachesTenthRowInAboutAMonth()
    {
        var r = Run(Personas[0], 40);
        Assert.InRange(r.RowReachedDay[10], 21, 35);
    }
}
