namespace Backyard;

public static class Balance
{
    public static int Speed = 1;
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

    public static readonly CropInfo[] Crops =
    [
        new("carrot", MaxStage: 1, StageMinutes: 5, SeedPrice: 1, SellPrice: 2),
        new("radish", MaxStage: 2, StageMinutes: 10, SeedPrice: 2, SellPrice: 6),
        new("potato", MaxStage: 3, StageMinutes: 10, SeedPrice: 6, SellPrice: 15),
        new("tomato", MaxStage: 3, StageMinutes: 15, SeedPrice: 10, SellPrice: 22),
        new("pumpkin", MaxStage: 3, StageMinutes: 20, SeedPrice: 16, SellPrice: 31),
    ];

    public static CropInfo? Crop(string name) => Array.Find(Crops, c => c.Name == name);

    public static CropInfo CropOrFallback(string name) => Crop(name) ?? Crops[0];
}

public sealed record CropInfo(string Name, int MaxStage, int StageMinutes, int SeedPrice, int SellPrice, bool Retired = false)
{
    public TimeSpan StageDuration => TimeSpan.FromMinutes((double)StageMinutes / Balance.Speed);
    public TimeSpan GrowTime => StageDuration * MaxStage;
}
