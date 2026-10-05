namespace Backyard;

public static class Balance
{
    public static int Speed = 1;
    public const int Columns = 10;
    public const int MaxRows = 10;
    public const int StartCoins = 5;
    public const int SecondRowPrice = 50;
    public const int ThirdRowPrice = 150;
    public const int UnlockMultiplier = 10;
    public const int WorkingWindowMinutes = 3;
    public static readonly int[] FlavorThresholds = [5, 10, 20];

    public static readonly Dictionary<string, string[]> Flavor = new()
    {
        ["carrot"] =
        [
            "Carrots weren't always orange; purple and yellow varieties came first.",
            "The Dutch helped make orange carrots famous across Europe.",
            "You can eat the leaves, too, and they can be used like herbs."
        ],

        ["radish"] =
        [
            "Some radishes are ready in just three weeks after planting.",
            "Ancient Egyptians grew radishes and valued them as a food crop.",
            "Did you know radish seeds can be pressed to make cooking oil?"
        ],

        ["potato"] =
        [
            "Potatoes come from the Andes Mountains, where they were first cultivated.",
            "The little 'eyes' can grow into new plants when the potato is planted.",
            "There are thousands of potato varieties, in colors from purple to yellow."
        ],

        ["tomato"] =
        [
            "Botanically speaking, tomatoes are fruit because they grow from a flower.",
            "Europe once thought tomatoes were poisonous, partly because of their relatives.",
            "Tomatoes and potatoes are close relatives, both belonging to the nightshade family."
        ],

        ["pumpkin"] =
        [
            "Pumpkins are technically berries, although they don't look like one.",
            "Some pumpkins grow heavier than a small car and can weigh over 500 kilograms.",
            "Pumpkins and cucumbers share the same family, along with melons and squash."
        ],
    };

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

    public static int CropOrder(string name) => Array.FindIndex(Crops, c => c.Name == name);
}

public sealed record CropInfo(string Name, int MaxStage, int StageMinutes, int SeedPrice, int SellPrice, bool Retired = false)
{
    public int UnlockPrice => SeedPrice * Balance.UnlockMultiplier;
    public TimeSpan StageDuration => TimeSpan.FromMinutes((double)StageMinutes / Balance.Speed);
}
