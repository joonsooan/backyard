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

    public static readonly CropInfo[] Crops =
    [
        new("carrot", MaxStage: 2, SeedPrice: 2, SellPrice: 4),
        new("potato", MaxStage: 3, SeedPrice: 6, SellPrice: 12),
    ];

    public static CropInfo? Crop(string name) => Array.Find(Crops, c => c.Name == name);

    public static CropInfo CropOrFallback(string name) => Crop(name) ?? Crops[0];
}

public sealed record CropInfo(string Name, int MaxStage, int SeedPrice, int SellPrice, bool Retired = false)
{
    public TimeSpan GrowTime => Balance.StageDuration * MaxStage;
}
