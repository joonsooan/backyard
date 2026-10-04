using System.Text.Json.Serialization;

namespace Backyard;

public sealed class FarmCell
{
    public required string Crop { get; set; }
    public int Stage { get; set; }
    public bool Watered { get; set; }
    public DateTimeOffset StageStartedAt { get; set; }

    [JsonIgnore] public CropInfo Info => Balance.CropOrFallback(Crop);
    [JsonIgnore] public bool IsRipe => Stage >= Info.MaxStage;
    [JsonIgnore] public bool IsGrowing => Watered && !IsRipe;
    [JsonIgnore] public DateTimeOffset NextStageAt => StageStartedAt + Info.StageDuration;

    public double Progress(DateTimeOffset now) =>
        IsRipe ? 1 : Math.Clamp((now - StageStartedAt) / Info.StageDuration, 0, 1);

    public void AdvanceStage(DateTimeOffset at)
    {
        Stage++;
        Watered = false;
        StageStartedAt = at;
    }
}

public class FarmState
{
    public int Coins { get; set; }
    public int Rows { get; set; }
    public FarmCell?[] Cells { get; set; } = [];
    public DateTimeOffset FirstRunAt { get; set; }
    public DateTimeOffset? LastWateredAt { get; set; }
    public DateTimeOffset? LastEventAt { get; set; }
    public Dictionary<string, int> HarvestCounts { get; set; } = [];
    public Dictionary<string, DateTimeOffset> FirstHarvestAt { get; set; } = [];

    public FarmState()
    {
    }

    public FarmState(DateTimeOffset now)
    {
        Coins = Balance.StartCoins;
        Rows = 1;
        Cells = new FarmCell?[Balance.Columns];
        FirstRunAt = now;
        Cells[0] = new FarmCell { Crop = Balance.Crops[0].Name, Stage = 0, Watered = false, StageStartedAt = now };
    }

    [JsonIgnore] public IEnumerable<FarmCell> Planted => Cells.OfType<FarmCell>();

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
        foreach (var cell in Planted)
        {
            if (cell.IsGrowing && cell.NextStageAt <= now)
                cell.AdvanceStage(cell.NextStageAt);
        }
    }

    private void WaterReadyCells(DateTimeOffset at)
    {
        var wateredAny = false;
        foreach (var cell in Planted)
        {
            if (cell.Watered || cell.IsRipe)
                continue;
            if (cell.NextStageAt <= at)
                cell.AdvanceStage(at);
            else
                cell.Watered = true;
            wateredAny = true;
        }
        if (wateredAny)
            LastWateredAt = at;
    }

    public bool TryPlant(int index, string cropName, DateTimeOffset now)
    {
        if (index < 0 || index >= Cells.Length || Cells[index] is not null)
            return false;
        var crop = Balance.Crop(cropName);
        if (crop is null || crop.Retired || Coins < crop.SeedPrice)
            return false;
        Coins -= crop.SeedPrice;
        Cells[index] = new FarmCell { Crop = cropName, Stage = 0, Watered = false, StageStartedAt = now };
        return true;
    }

    public bool TryHarvest(int index, DateTimeOffset now)
    {
        if (index < 0 || index >= Cells.Length || Cells[index] is not { IsRipe: true } cell)
            return false;
        Coins += cell.Info.SellPrice;
        HarvestCounts[cell.Crop] = HarvestCounts.GetValueOrDefault(cell.Crop) + 1;
        FirstHarvestAt.TryAdd(cell.Crop, now);
        Cells[index] = null;
        return true;
    }

    public bool TryRemove(int index)
    {
        if (index < 0 || index >= Cells.Length || Cells[index] is null)
            return false;
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
