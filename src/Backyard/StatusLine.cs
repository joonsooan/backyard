namespace Backyard;

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
    private const string Cyan = "\x1b[96m";
    private const string Reset = "\x1b[0m";

    private static string Link(string text) =>
        $"\x1b]8;;backyard://open\x1b\\{text}\x1b]8;;\x1b\\";

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
        var groups = cells.GroupBy(Glyph).ToDictionary(g => g.Key, g => (Count: g.Count(), Watered: g.All(c => c?.Watered ?? true)));
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
            return color ? Link($"{Cyan}harvest!{Reset}") : "harvest!";
        var growing = planted
            .Where(c => c.Watered && c.Stage < Balance.Crops[c.Crop].MaxStage)
            .ToArray();
        if (growing.Length > 0)
        {
            var next = growing.Min(c => c.StageStartedAt) + Balance.StageDuration;
            var minutes = Math.Max(1, (int)Math.Ceiling((next - now).TotalMinutes));
            return color ? Link($"{Green}~ {minutes}m{Reset}") : $"~ {minutes}m";
        }
        return color ? $"{Tan}waiting{Reset}" : "waiting";
    }
}
