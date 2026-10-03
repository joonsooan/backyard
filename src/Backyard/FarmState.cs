namespace Backyard;

public static class Balance
{
    public static int Speed = 1;
    public static TimeSpan StageDuration => TimeSpan.FromMinutes(10.0 / Speed);
    public static TimeSpan StatusTimeout => TimeSpan.FromMinutes(30.0 / Speed);
    public static TimeSpan WaterAfterglow => TimeSpan.FromMinutes(10.0 / Speed);
    public const int Columns = 10;
    public const int MaxRows = 10;
    public const int StartCoins = 10;
    public const int SecondRowPrice = 50;
    public const int ThirdRowPrice = 150;

    public static int NextRowPrice(int currentRows) => currentRows switch
    {
        1 => SecondRowPrice,
        2 => ThirdRowPrice,
        _ => ThirdRowPrice << (currentRows - 2)
    };

    public static readonly IReadOnlyDictionary<string, CropInfo> Crops = new Dictionary<string, CropInfo>
    {
        ["carrot"] = new CropInfo(MaxStage: 2, SeedPrice: 2, SellPrice: 4),
        ["potato"] = new CropInfo(MaxStage: 3, SeedPrice: 5, SellPrice: 10),
    };
}

public sealed record CropInfo(int MaxStage, int SeedPrice, int SellPrice);

public sealed class FarmCell
{
    public required string Crop { get; set; }
    public int Stage { get; set; }
    public bool Watered { get; set; }
    public DateTimeOffset StageStartedAt { get; set; }
}

public static class StatusLine
{
    public static char Glyph(FarmCell? cell)
    {
        if (cell is null)
            return '_';
        var maxStage = Balance.Crops[cell.Crop].MaxStage;
        return ". , v Y"[2 * (cell.Stage * 3 / maxStage)];
    }

    private const string Dim = "\x1b[90m";
    private const string Green = "\x1b[32m";
    private const string BoldGreen = "\x1b[1;32m";
    private const string Gold = "\x1b[93m";
    private const string Tan = "\x1b[38;5;180m";
    private const string Reset = "\x1b[0m";

    private static string Paint(char glyph, bool watered, bool color) => !color ? glyph.ToString() : glyph switch
    {
        '_' => $"{Dim}_{Reset}",
        'Y' => $"{Gold}Y{Reset}",
        _ when !watered => $"{Tan}{glyph}{Reset}",
        'v' => $"{BoldGreen}v{Reset}",
        _ => $"{Green}{glyph}{Reset}",
    };

    public static string FieldSegment(FarmCell?[] cells, int rows, bool color = false)
    {
        if (rows == 1)
            return "[" + string.Join(' ', cells.Select(c => Paint(Glyph(c), c?.Watered ?? true, color))) + "]";
        var groups = cells.GroupBy(Glyph).ToDictionary(g => g.Key, g => (Count: g.Count(), Watered: g.Any(c => c?.Watered ?? true)));
        return string.Join(' ', "Yv,._"
            .Where(groups.ContainsKey)
            .Select(g => $"{Paint(g, groups[g].Watered, color)}:{groups[g].Count}"));
    }

    public static string Compose(FarmState farm, DateTimeOffset now, bool color = false)
    {
        var coins = color ? $"{Gold}{farm.Coins}G{Reset}" : $"{farm.Coins}G";
        var line = $"{FieldSegment(farm.Cells, farm.Rows, color)} │ {coins}";
        return GrowthSegment(farm, now, color) is { } growth ? $"{line} │ {growth}" : line;
    }

    private static string? GrowthSegment(FarmState farm, DateTimeOffset now, bool color)
    {
        var planted = farm.Cells.OfType<FarmCell>().ToArray();
        if (planted.Length == 0)
            return null;
        if (planted.Any(c => c.Stage >= Balance.Crops[c.Crop].MaxStage))
            return color ? $"{Gold}Y!{Reset}" : "Y!";
        var growing = planted
            .Where(c => c.Watered && c.Stage < Balance.Crops[c.Crop].MaxStage)
            .ToArray();
        if (growing.Length > 0)
        {
            var next = growing.Min(c => c.StageStartedAt) + Balance.StageDuration;
            var minutes = Math.Max(1, (int)Math.Ceiling((next - now).TotalMinutes));
            return color ? $"{Green}~ {minutes}m{Reset}" : $"~ {minutes}m";
        }
        return color ? $"{Tan}dry{Reset}" : "dry";
    }
}

public sealed class FarmState
{
    public int Coins { get; set; }
    public int Rows { get; set; }
    public FarmCell?[] Cells { get; set; } = [];
    public DateTimeOffset FirstRunAt { get; set; }
    public DateTimeOffset? LastWateredAt { get; set; }
    public DateTimeOffset? LastEventAt { get; set; }
    public Dictionary<string, int> HarvestCounts { get; set; } = [];

    public static FarmState CreateNew(DateTimeOffset now)
    {
        var state = new FarmState
        {
            Coins = Balance.StartCoins,
            Rows = 1,
            Cells = new FarmCell?[Balance.Columns],
            FirstRunAt = now,
        };
        state.Cells[0] = new FarmCell { Crop = "carrot", Stage = 0, Watered = false, StageStartedAt = now };
        return state;
    }

    public void Apply(IEnumerable<WatchEvent> events, DateTimeOffset now)
    {
        foreach (var e in events.OrderBy(e => e.Timestamp))
        {
            if (e.Timestamp < FirstRunAt || e.Timestamp > now)
                continue;
            Settle(e.Timestamp);
            WaterReadyCells(e.Timestamp);
            LastEventAt = e.Timestamp;
        }
        Settle(now);
    }

    public void Settle(DateTimeOffset now)
    {
        for (var i = 0; i < Cells.Length; i++)
        {
            var cell = Cells[i];
            if (cell is null)
                continue;
            var crop = Balance.Crops[cell.Crop];
            if (cell.Watered && cell.Stage < crop.MaxStage && cell.StageStartedAt + Balance.StageDuration <= now)
                AdvanceStage(cell, cell.StageStartedAt + Balance.StageDuration);
        }
    }

    private void WaterReadyCells(DateTimeOffset at)
    {
        var wateredAny = false;
        foreach (var cell in Cells)
        {
            if (cell is null || cell.Watered)
                continue;
            var crop = Balance.Crops[cell.Crop];
            if (cell.Stage >= crop.MaxStage)
                continue;
            if (cell.StageStartedAt + Balance.StageDuration <= at)
                AdvanceStage(cell, at);
            else
                cell.Watered = true;
            wateredAny = true;
        }
        if (wateredAny)
            LastWateredAt = at;
    }

    private static void AdvanceStage(FarmCell cell, DateTimeOffset at)
    {
        cell.Stage++;
        cell.Watered = false;
        cell.StageStartedAt = at;
    }

    public bool TryPlant(int index, string cropName, DateTimeOffset now)
    {
        if (index < 0 || index >= Cells.Length || Cells[index] is not null)
            return false;
        if (!Balance.Crops.TryGetValue(cropName, out var crop) || Coins < crop.SeedPrice)
            return false;
        Coins -= crop.SeedPrice;
        Cells[index] = new FarmCell { Crop = cropName, Stage = 0, Watered = false, StageStartedAt = now };
        return true;
    }

    public bool TryHarvest(int index)
    {
        if (index < 0 || index >= Cells.Length || Cells[index] is not { } cell)
            return false;
        var crop = Balance.Crops[cell.Crop];
        if (cell.Stage < crop.MaxStage)
            return false;
        Coins += crop.SellPrice;
        HarvestCounts[cell.Crop] = HarvestCounts.GetValueOrDefault(cell.Crop) + 1;
        Cells[index] = null;
        return true;
    }

    public bool TryExpand()
    {
        if (Rows >= Balance.MaxRows)
            return false;
        var price = Balance.NextRowPrice(Rows);
        if (Coins < price)
            return false;
        Coins -= price;
        Rows++;
        var expanded = new FarmCell?[Rows * Balance.Columns];
        Cells.CopyTo(expanded, 0);
        Cells = expanded;
        return true;
    }
}
