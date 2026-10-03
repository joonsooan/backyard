namespace Backyard;

public static class Balance
{
    public static int Speed = 1;
    public static TimeSpan StageDuration => TimeSpan.FromMinutes(10.0 / Speed);
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
