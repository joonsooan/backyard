using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace Backyard;

public sealed record TuiCell(char Glyph, string? Crop, string StageLabel, double Fraction, string Status, bool Watered = true);

public enum TuiScreen
{
    Main,
    Shop,
    Codex
}

// Every user-visible string lives here.
public static class TuiText
{
    public const int MinWidth = 100;
    public const int MinHeight = 20;
    public const int ResizeWidth = 100;
    public const int ResizeHeight = 30;
    public const string TooSmallTitle = "[bold]backyard[/]";
    public const string TooSmallCurrent = "window is too small: {0}x{1}";
    public const string TooSmallNeeds = "needs at least {0}x{1}";
    public const string TooSmallResizeKey = $"r[{TuiColors.Muted}]  resize automatically[/]";
    public const string TooSmallQuitKey = $"esc[{TuiColors.Muted}]  quit[/]";
    public static readonly string[] ScreenNames = ["garden", "shop", "codex"];
    public const string MainKeysLine =
        $" wasd[{TuiColors.Muted}] move │ [/]f[{TuiColors.Muted}] harvest │ [/]x[{TuiColors.Muted}] remove │ [/]tab[{TuiColors.Muted}] next │ [/]r[{TuiColors.Muted}] resize │ [/]esc[{TuiColors.Muted}] quit[/]";
    public const string ShopKeysLine =
        $" space[{TuiColors.Muted}] buy │ [/]tab[{TuiColors.Muted}] next │ [/]r[{TuiColors.Muted}] resize │ [/]esc[{TuiColors.Muted}] quit[/]";
    public const string CodexKeysLine =
        $" up/down[{TuiColors.Muted}] select │ [/]tab[{TuiColors.Muted}] next │ [/]r[{TuiColors.Muted}] resize │ [/]esc[{TuiColors.Muted}] quit[/]";
    public const string UnlockItemName = "{0} seeds";
    public const string UnlockItemDesc = "unlocks {0}";
    public const string UnlockGrowTime = "grow time  {0}m x {1}";
    public const string UnlockSeed = "seed       {0}G";
    public const string UnlockSell = "sell       {0}G";
    public const string HiddenName = "???";
    public const string FlavorLocked = "??? (harvest {0} times to read)";
    public const string FlavorTitle = "interesting facts │ {0}";
    public const string EmptyLabel = "empty";
    public const string PlantHint = "press a number to plant";
    public const string LockedLabel = "locked";
    public const string LockedHint = "buy extra row in shop";
    public const string NotEnoughGold = "not enough gold";
    public const string SoldOut = "sold out";
    public const string RowItemName = "extra row";
    public const string RowItemDesc = "adds one more row to garden";
    public const string NeedsWater = "finish a task to water";
    public const int BarWidth = 16;
    public const char StepFilled = '▰';
    public const char StepEmpty = '▱';

    public static readonly string[] StageLabels = ["seed", "sprout", "budding", "ready to harvest"];

    public const char EmptyGlyph = '_';
    public const char LockedGlyph = '#';
    public const char SeedGlyph = '.';
    public const char SproutGlyph = ',';
    public const char GrowingGlyph = 'v';
    public const char ReadyGlyph = 'Y';

    public const string FarmerGlyph = "o/";
    public const int MaxFarmers = 3;
    public const int FarmerMoveMinSeconds = 3;
    public const int FarmerMoveMaxSeconds = 8;
    public const string WorkingLabel = "working";
    public const int WorkingFrameWidth = 10;
    public static readonly int[] WorkingDots = [3, 2, 1, 0, 1, 2];
}

public static class TuiColors
{
    public const string Muted = "grey";
    public const string Dry = "tan";
    public const string Gold = "yellow";
    public const string Sprout = "green";
    public const string Growing = "bold green";
    public const string Empty = "dim grey";
    public const string SelectedBorder = "bold white";
    public static readonly string[] FarmerColors = ["white", "blue", "magenta"];
}

public sealed class Farmer
{
    public int X;
    public int Direction = 1;
    public DateTimeOffset NextMoveAt;
}

// Rendering and key handling only.
public static partial class Tui
{
    public const string WindowTitle = "backyard";
    private const int CellInnerWidth = 3;
    private const int CellGap = 1;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WmImeControl = 0x0283;
    private const int ImcSetConversionMode = 0x0002;

    private static void ForceEnglishIme()
    {
        if (OperatingSystem.IsWindows())
            SendMessage(ImmGetDefaultIMEWnd(GetConsoleWindow()), WmImeControl, ImcSetConversionMode, IntPtr.Zero);
    }

    private static bool WindowTooSmall() =>
        Console.WindowWidth < TuiText.MinWidth || Console.WindowHeight < TuiText.MinHeight;

    // Ask the terminal to grow via the xterm resize sequence first, then fall back to the Console API on Windows.
    private static void TryResizeWindow(int width, int height)
    {
        Console.Write($"\x1b[8;{height};{width}t");
        Thread.Sleep(150);
        if (!OperatingSystem.IsWindows() || (Console.WindowWidth >= width && Console.WindowHeight >= height))
            return;
        try
        {
            Console.SetBufferSize(Math.Max(Console.BufferWidth, width), Math.Max(Console.BufferHeight, height));
            Console.SetWindowSize(width, height);
        }
        catch (Exception)
        {
        }
    }

    private static int FieldWidth(int n) =>
        n * (CellInnerWidth + 2) + (n - 1) * CellGap;

    // Redraw on every key, and re-read state.json every few seconds so transcript events show up while the TUI is open.
    private static readonly TimeSpan ResyncInterval = TimeSpan.FromSeconds(5);

    public static int Run(FarmState farm, Action<FarmState> save, Func<FarmState> resync)
    {
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings { ColorSystem = ColorSystemSupport.EightBit });
        Console.Title = WindowTitle;
        ForceEnglishIme();
        if (WindowTooSmall())
            TryResizeWindow(TuiText.MinWidth, TuiText.MinHeight);

        var selected = 0;
        var shopSelected = 0;
        var codexSelected = 0;
        var shopMessage = "";
        var gardenMessage = "";
        var screen = TuiScreen.Main;
        var quit = false;
        var farmers = new Dictionary<string, Farmer>();

        AnsiConsole.AlternateScreen(() =>
        {
            AnsiConsole.Cursor.Hide();
            try
            {
                farm.Settle(DateTimeOffset.Now);
                UpdateFarmers(farmers, farm, DateTimeOffset.Now);
                AnsiConsole.Live(View(farm, farmers, selected, shopSelected, codexSelected, shopMessage, gardenMessage, screen)).Start(ctx =>
                {
                    ctx.Refresh();
                    var lastSize = (Console.WindowWidth, Console.WindowHeight);
                    var lastSyncAt = DateTimeOffset.Now;
                    while (!quit)
                    {
                        if (DateTimeOffset.Now - lastSyncAt >= ResyncInterval)
                        {
                            try
                            {
                                farm = resync();
                            }
                            catch (Exception)
                            {
                            }
                            lastSyncAt = DateTimeOffset.Now;
                        }
                        var size = (Console.WindowWidth, Console.WindowHeight);
                        if (size != lastSize)
                        {
                            lastSize = size;
                            AnsiConsole.Clear();
                        }
                        while (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(true);
                            if (key.KeyChar > 127)
                            {
                                ForceEnglishIme();
                                AnsiConsole.Clear();
                                continue;
                            }
                            var visibleCells = VisibleCellCount(farm);
                            var items = ShopItems(farm);
                            gardenMessage = "";
                            if (screen == TuiScreen.Main && key.KeyChar is >= '1' and <= '9'
                                && selected < farm.Cells.Length && farm.Cells[selected] is null)
                            {
                                var cropIndex = key.KeyChar - '1';
                                if (cropIndex < Balance.Crops.Length)
                                {
                                    if (farm.TryPlant(selected, Balance.Crops[cropIndex].Name, DateTimeOffset.Now))
                                        save(farm);
                                    else
                                        gardenMessage = TuiText.NotEnoughGold;
                                }
                                continue;
                            }
                            switch (key.Key)
                            {
                                case ConsoleKey.W or ConsoleKey.UpArrow when screen == TuiScreen.Shop:
                                    shopSelected = Math.Max(0, shopSelected - 1);
                                    shopMessage = "";
                                    break;
                                case ConsoleKey.S or ConsoleKey.DownArrow when screen == TuiScreen.Shop:
                                    shopSelected = Math.Min(items.Length - 1, shopSelected + 1);
                                    shopMessage = "";
                                    break;
                                case ConsoleKey.Spacebar when screen == TuiScreen.Shop:
                                    shopMessage = Buy(farm, items[shopSelected].Id, () => save(farm));
                                    shopSelected = Math.Min(shopSelected, ShopItems(farm).Length - 1);
                                    break;
                                case ConsoleKey.W or ConsoleKey.UpArrow when screen == TuiScreen.Codex:
                                    codexSelected = Math.Max(0, codexSelected - 1);
                                    break;
                                case ConsoleKey.S or ConsoleKey.DownArrow when screen == TuiScreen.Codex:
                                    codexSelected = Math.Min(CodexCrops(farm).Length - 1, codexSelected + 1);
                                    break;
                                case ConsoleKey.A or ConsoleKey.LeftArrow when screen == TuiScreen.Main:
                                    selected = selected % Balance.Columns > 0
                                        ? selected - 1
                                        : selected + Balance.Columns - 1;
                                    break;
                                case ConsoleKey.D or ConsoleKey.RightArrow when screen == TuiScreen.Main:
                                    selected = selected % Balance.Columns < Balance.Columns - 1
                                        ? selected + 1
                                        : selected - (Balance.Columns - 1);
                                    break;
                                case ConsoleKey.W or ConsoleKey.UpArrow when screen == TuiScreen.Main:
                                    selected = selected >= Balance.Columns
                                        ? selected - Balance.Columns
                                        : selected + visibleCells - Balance.Columns;
                                    break;
                                case ConsoleKey.S or ConsoleKey.DownArrow when screen == TuiScreen.Main:
                                    selected = selected + Balance.Columns < visibleCells
                                        ? selected + Balance.Columns
                                        : selected % Balance.Columns;
                                    break;
                                case ConsoleKey.F when screen == TuiScreen.Main:
                                    if (selected < farm.Cells.Length && farm.TryHarvest(selected, DateTimeOffset.Now))
                                        save(farm);
                                    break;
                                case ConsoleKey.X when screen == TuiScreen.Main:
                                    if (selected < farm.Cells.Length && farm.TryRemove(selected))
                                        save(farm);
                                    break;
                                case ConsoleKey.Tab:
                                    screen = (TuiScreen)(((int)screen + 1) % TuiText.ScreenNames.Length);
                                    shopMessage = "";
                                    break;
                                case ConsoleKey.R:
                                    TryResizeWindow(TuiText.ResizeWidth, TuiText.ResizeHeight);
                                    AnsiConsole.Clear();
                                    break;
                                case ConsoleKey.Escape:
                                    quit = true;
                                    break;
                            }
                        }
                        farm.Settle(DateTimeOffset.Now);
                        UpdateFarmers(farmers, farm, DateTimeOffset.Now);
                        ctx.UpdateTarget(View(farm, farmers, selected, shopSelected, codexSelected, shopMessage, gardenMessage, screen));
                        var waited = 0;
                        while (!quit && !Console.KeyAvailable && waited < 200)
                        {
                            Thread.Sleep(15);
                            waited += 15;
                        }
                    }
                });
            }
            finally
            {
                AnsiConsole.Cursor.Show();
            }
        });
        save(farm);
        return 0;
    }

    private static int PathWidth => FieldWidth(Balance.Columns) - TuiText.FarmerGlyph.Length;

    private static IReadOnlyList<string> WorkingSessions(FarmState farm) =>
        (farm as SavedState)?.WorkingSessions ?? [];

    private static void UpdateFarmers(Dictionary<string, Farmer> farmers, FarmState farm, DateTimeOffset now)
    {
        var keys = WorkingSessions(farm).Take(TuiText.MaxFarmers).ToList();
        foreach (var gone in farmers.Keys.Where(k => !keys.Contains(k)).ToList())
            farmers.Remove(gone);
        bool Occupied(int x) => farmers.Values.Any(f => f.X == x);
        foreach (var key in keys)
        {
            if (farmers.ContainsKey(key))
                continue;
            var x = Random.Shared.Next(PathWidth + 1);
            for (var tries = 0; tries < PathWidth && Occupied(x); tries++)
                x = Random.Shared.Next(PathWidth + 1);
            farmers[key] = new Farmer { X = x, Direction = Random.Shared.Next(2) == 0 ? -1 : 1, NextMoveAt = now };
        }
        foreach (var farmer in farmers.Values)
        {
            if (now < farmer.NextMoveAt)
                continue;
            var target = farmer.X + farmer.Direction * Random.Shared.Next(1, 3);
            if (target < 0 || target > PathWidth)
            {
                farmer.Direction = -farmer.Direction;
                target = Math.Clamp(target, 0, PathWidth);
            }
            if (!Occupied(target))
                farmer.X = target;
            farmer.NextMoveAt = now.AddSeconds(Random.Shared.Next(TuiText.FarmerMoveMinSeconds, TuiText.FarmerMoveMaxSeconds + 1));
        }
    }

    private static string FarmerPathLine(Dictionary<string, Farmer> farmers, IReadOnlyList<string> keys)
    {
        var line = new string(' ', FieldWidth(Balance.Columns));
        var parts = new List<string>();
        var cursor = 0;
        foreach (var (key, i) in keys.Take(TuiText.MaxFarmers).Select((k, i) => (k, i)).OrderBy(p => farmers.GetValueOrDefault(p.k)?.X ?? -1))
        {
            if (!farmers.TryGetValue(key, out var farmer) || farmer.X < cursor)
                continue;
            parts.Add(line[cursor..farmer.X]);
            parts.Add($"[{TuiColors.FarmerColors[i]}]{TuiText.FarmerGlyph}[/]");
            cursor = farmer.X + TuiText.FarmerGlyph.Length;
        }
        parts.Add(line[cursor..]);
        return new string(' ', FieldSideMargin) + string.Concat(parts);
    }

    private static string WorkingFrame(DateTimeOffset now)
    {
        var left = TuiText.WorkingDots[now.ToUnixTimeSeconds() % TuiText.WorkingDots.Length];
        var frame = new string('.', left) + TuiText.WorkingLabel + new string('.', TuiText.WorkingFrameWidth - TuiText.WorkingLabel.Length - left);
        return $"[{TuiColors.Muted}]{frame} │ [/]";
    }

    // The garden shows all unlocked rows plus one locked preview row.
    private static int VisibleRowCount(FarmState farm) =>
        Math.Min(farm.Rows + 1, Balance.MaxRows);

    private static int VisibleCellCount(FarmState farm) =>
        VisibleRowCount(farm) * Balance.Columns;

    private const string UnlockId = "unlock";
    private const string HiddenId = "hidden";

    private static (string Name, string Price, string Desc, string Id)[] ShopItems(FarmState farm)
    {
        var items = new List<(string, string, string, string)>
        {
            (TuiText.RowItemName,
                farm.Rows >= Balance.MaxRows ? TuiText.SoldOut : $"{Balance.NextRowPrice(farm.Rows)}G",
                TuiText.RowItemDesc, TuiText.RowItemName)
        };
        if (farm.NextUnlock is { } next)
        {
            items.Add((string.Format(TuiText.UnlockItemName, next.Name), $"{next.UnlockPrice}G",
                string.Format(TuiText.UnlockItemDesc, next.Name), UnlockId));
            var hidden = ShopCrops.Count(c => !farm.IsUnlocked(c.Name)) - 1;
            for (var i = 0; i < hidden; i++)
                items.Add((TuiText.HiddenName, "", "", HiddenId));
        }
        return items.ToArray();
    }

    private static string Buy(FarmState farm, string id, Action save)
    {
        switch (id)
        {
            case TuiText.RowItemName when farm.Rows >= Balance.MaxRows:
                return TuiText.SoldOut;
            case TuiText.RowItemName when !farm.TryExpand():
            case UnlockId when !farm.TryUnlock():
                return TuiText.NotEnoughGold;
            case HiddenId:
                return "";
        }
        save();
        return "";
    }

    private static TuiCell CellView(FarmCell? cell, DateTimeOffset now)
    {
        if (cell is null)
            return new TuiCell(TuiText.EmptyGlyph, null, "", 0, "");
        var glyph = StatusLine.Glyph(cell);
        var label = TuiText.StageLabels[cell.Stage * 3 / cell.Info.MaxStage];
        if (cell.IsRipe)
            return new TuiCell(glyph, cell.Crop, label, 1, "");
        if (!cell.Watered)
            return new TuiCell(glyph, cell.Crop, label, 0, TuiText.NeedsWater, false);
        var nextLabel = cell.Stage + 1 >= cell.Info.MaxStage ? "harvest" : "grow";
        return new TuiCell(glyph, cell.Crop, label, cell.Progress(now), $"{FormatRemaining(cell.NextStageAt - now)} to {nextLabel}");
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        var total = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        return total < 60 ? $"{total}s" : $"{total / 60}m {total % 60}s";
    }

    private static TuiCell[] CellViews(FarmState farm, DateTimeOffset now)
    {
        var total = VisibleCellCount(farm);
        var views = new TuiCell[total];
        for (var i = 0; i < total; i++)
            views[i] = i < farm.Cells.Length
                ? CellView(farm.Cells[i], now)
                : new TuiCell(TuiText.LockedGlyph, null, "", 0, TuiText.LockedLabel);
        return views;
    }

    private static Markup View(FarmState farm, Dictionary<string, Farmer> farmers, int selected, int shopSelected, int codexSelected, string shopMessage, string gardenMessage, TuiScreen screen) =>
        WindowTooSmall()
            ? TooSmallView()
            : Render(farm, farmers, selected, shopSelected, codexSelected, shopMessage, gardenMessage, screen);

    private static Markup TooSmallView() => new(string.Join("\n",
        TuiText.TooSmallTitle,
        "",
        string.Format(TuiText.TooSmallCurrent, Console.WindowWidth, Console.WindowHeight),
        string.Format(TuiText.TooSmallNeeds, TuiText.MinWidth, TuiText.MinHeight),
        "",
        TuiText.TooSmallResizeKey,
        TuiText.TooSmallQuitKey));

    // Frame layout: top border, body (screen-specific), separator, key hint line. Sizes follow the console window.
    private static Markup Render(FarmState farm, Dictionary<string, Farmer> farmers, int selected, int shopSelected, int codexSelected, string shopMessage, string gardenMessage, TuiScreen screen)
    {
        var frameWidth = Math.Max(TuiText.MinWidth, Console.WindowWidth - 2);
        var frameHeight = Math.Max(TuiText.MinHeight - 1, Console.WindowHeight - 1);
        var inner = frameWidth - 2;

        var cells = CellViews(farm, DateTimeOffset.Now);
        var bodyHeight = frameHeight - 4;
        var (body, keys) = screen switch
        {
            TuiScreen.Shop => (ShopBody(farm, inner, bodyHeight, shopSelected, shopMessage), TuiText.ShopKeysLine),
            TuiScreen.Codex => (CodexBody(farm, inner, bodyHeight, codexSelected), TuiText.CodexKeysLine),
            _ => (MainBody(farm, farmers, cells, selected, gardenMessage, inner, bodyHeight), TuiText.MainKeysLine)
        };

        var lines = new List<string> { TopBorder(screen, farm, frameWidth) };
        lines.AddRange(body.Take(bodyHeight));
        var filler = bodyHeight - body.Count;
        for (var i = 0; i < filler; i++)
            lines.Add(Framed("", inner));
        lines.Add(Separator(frameWidth));
        lines.Add(Framed(keys, inner));
        lines.Add($"[{TuiColors.Muted}]└{new string('─', frameWidth - 2)}┘[/]");
        return new Markup(string.Join("\n", lines));
    }

    private const int FieldSideMargin = 3;

    private static List<string> MainBody(FarmState farm, Dictionary<string, Farmer> farmers, TuiCell[] cells, int selected, string gardenMessage, int inner, int bodyHeight)
    {
        var leftWidth = FieldWidth(Balance.Columns) + FieldSideMargin * 2;
        var panelWidth = Math.Max(3, inner - leftWidth - 1);
        var left = FieldLines(cells, selected).ToList();
        left.Insert(0, "");
        left.Add(FarmerPathLine(farmers, WorkingSessions(farm)));
        var panel = InfoPanel(farm, cells[selected], gardenMessage, panelWidth);
        panel.Insert(0, "");
        return Columns(left, panel, leftWidth, inner, bodyHeight);
    }

    private static readonly CropInfo[] ShopCrops = Balance.Crops.Where(c => !c.Retired).ToArray();

    private static List<string> InfoPanel(FarmState farm, TuiCell cell, string gardenMessage, int panelWidth)
    {
        var panelInner = Math.Max(1, panelWidth - 2);
        if (cell.Glyph == TuiText.EmptyGlyph)
        {
            var empty = new List<string> { $" [{TuiColors.Muted}]{TuiText.EmptyLabel}[/]" };
            empty.AddRange(Wrap(TuiText.PlantHint, panelInner).Select(l => $" [{TuiColors.Muted}]{Markup.Escape(l)}[/]"));
            empty.Add("");
            empty.AddRange(Balance.Crops
                .Select((crop, i) => (crop, i))
                .Where(x => !x.crop.Retired && farm.IsUnlocked(x.crop.Name))
                .Select(x =>
                    $" [[{x.i + 1}]] [{TuiColors.Muted}]{Markup.Escape(Trunc(x.crop.Name, panelInner - 10))}[/] [{TuiColors.Gold}]{x.crop.SeedPrice}G[/]"));
            if (gardenMessage.Length > 0)
            {
                empty.Add("");
                empty.AddRange(Wrap(gardenMessage, panelInner).Select(l => $" [{TuiColors.Dry}]{Markup.Escape(l)}[/]"));
            }
            return empty;
        }
        if (cell.Glyph == TuiText.LockedGlyph)
            return
            [
                $" [{TuiColors.Muted}]{TuiText.LockedLabel}[/]",
                "",
                $" [{TuiColors.Muted}]{TuiText.LockedHint}[/]"
            ];

        var panel = new List<string>
        {
            $" [bold]{Markup.Escape(Trunc(cell.Crop ?? "", panelInner))}[/]",
            $" [{GlyphColor(cell)}]{cell.Glyph}[/] [{TuiColors.Muted}]{Markup.Escape(Trunc(cell.StageLabel, panelInner - 2))}[/]",
            "",
            " " + ProgressBarMarkup(cell.Fraction, Math.Min(TuiText.BarWidth, panelInner))
        };
        var statusColor = cell.Watered ? TuiColors.Muted : TuiColors.Dry;
        panel.AddRange(Wrap(cell.Status, panelInner).Select(l => $" [{statusColor}]{Markup.Escape(l)}[/]"));
        return panel;
    }

    private static string ProgressBarMarkup(double fraction, int barWidth)
    {
        var filled = (int)Math.Round(fraction * barWidth);
        var filledPart = new string(TuiText.StepFilled, filled);
        var rest = new string(TuiText.StepEmpty, barWidth - filled);
        return $"[{TuiColors.Sprout}]{filledPart}[/][{TuiColors.Muted}]{rest}[/]";
    }

    private static string Trunc(string text, int width) =>
        text.Length <= width ? text : text[..width];

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line;
                line = "";
            }
            line = line.Length == 0 ? word : $"{line} {word}";
        }
        if (line.Length > 0)
            yield return line;
    }

    private static string PadTo(string markup, int width) =>
        markup + new string(' ', Math.Max(0, width - VisibleWidth(markup)));

    private static List<string> ShopBody(FarmState farm, int inner, int bodyHeight, int shopSelected, string shopMessage)
    {
        var items = ShopItems(farm);
        var leftWidth = FieldWidth(Balance.Columns) + FieldSideMargin * 2;
        var panelWidth = Math.Max(3, inner - leftWidth - 1);
        var left = new List<string> { "" };
        left.AddRange(items.Select((item, i) =>
        {
            var name = Markup.Escape(item.Name.PadRight(22));
            return i == shopSelected
                ? $" [bold]> {name}[/][{TuiColors.Gold}]{Markup.Escape(item.Price)}[/]"
                : $"   {name}[{TuiColors.Gold}]{Markup.Escape(item.Price)}[/]";
        }));

        var item = items[shopSelected];
        var panelInner = Math.Max(1, panelWidth - 2);
        var panel = new List<string> { "" };
        switch (item.Id)
        {
            case HiddenId:
                panel.Add($" [{TuiColors.Muted}]{TuiText.HiddenName}[/]");
                break;
            case UnlockId:
                var next = farm.NextUnlock!;
                panel.Add($" [bold]{Markup.Escape(next.Name)}[/]");
                panel.Add($" [{TuiColors.Gold}]{Markup.Escape(item.Price)}[/]");
                panel.Add("");
                panel.AddRange(Wrap(item.Desc, panelInner).Select(l => $" [{TuiColors.Muted}]{Markup.Escape(l)}[/]"));
                panel.Add("");
                panel.Add($" [{TuiColors.Muted}]{string.Format(TuiText.UnlockGrowTime, next.StageMinutes, next.MaxStage)}[/]");
                panel.Add($" [{TuiColors.Muted}]{string.Format(TuiText.UnlockSeed, next.SeedPrice)}[/]");
                panel.Add($" [{TuiColors.Muted}]{string.Format(TuiText.UnlockSell, next.SellPrice)}[/]");
                break;
            default:
                panel.Add($" [bold]{Markup.Escape(Trunc(item.Name, panelInner))}[/]");
                panel.Add($" [{TuiColors.Gold}]{Markup.Escape(item.Price)}[/]");
                panel.Add("");
                panel.AddRange(Wrap(item.Desc, panelInner).Select(l => $" [{TuiColors.Muted}]{Markup.Escape(l)}[/]"));
                break;
        }
        if (shopMessage.Length > 0)
        {
            panel.Add("");
            panel.AddRange(Wrap(shopMessage, panelInner).Select(l => $" [{TuiColors.Dry}]{Markup.Escape(l)}[/]"));
        }

        return Columns(left, panel, leftWidth, inner, bodyHeight);
    }

    // Two-column body used by garden and shop: list on the left, detail panel on the right, split by a vertical rule.
    private static List<string> Columns(List<string> left, List<string> panel, int leftWidth, int inner, int bodyHeight)
    {
        var body = new List<string>();
        var columnRows = Math.Max(Math.Max(left.Count, panel.Count), bodyHeight);
        for (var row = 0; row < columnRows; row++)
        {
            var l = row < left.Count ? left[row] : "";
            var r = row < panel.Count ? panel[row] : "";
            body.Add(Framed(PadTo(l, leftWidth) + $"[{TuiColors.Muted}]│[/]" + r, inner));
        }
        return body;
    }

    private static CropInfo[] CodexCrops(FarmState farm) =>
        Balance.Crops.Where(c => !c.Retired || farm.IsUnlocked(c.Name)).ToArray();

    private static List<string> CodexBody(FarmState farm, int inner, int bodyHeight, int codexSelected)
    {
        var body = new List<string>
        {
            Framed("", inner),
            Framed($"  [{TuiColors.Muted}]{"name",-17}{"grow time",-12}{"seed price",-12}{"sell price",-12}{"harvested",-11}first harvest[/]", inner)
        };
        var crops = CodexCrops(farm);
        for (var i = 0; i < crops.Length; i++)
        {
            var info = crops[i];
            var count = farm.HarvestCounts.GetValueOrDefault(info.Name);
            var grow = $"{info.StageMinutes}m x {info.MaxStage}";
            var cursor = i == codexSelected ? "> " : "  ";
            var line = farm.IsUnlocked(info.Name)
                ? $"{cursor}{Markup.Escape(info.Name),-17}{grow,-12}{$"{info.SeedPrice}G",-12}{$"{info.SellPrice}G",-12}{count,-11}{(farm.FirstHarvestAt.TryGetValue(info.Name, out var first) ? first.ToString("yyyy-MM-dd") : "-")}"
                : $"{cursor}[{TuiColors.Muted}]{TuiText.HiddenName,-17}{"?",-12}{"?",-12}{"?",-12}{0,-11}-[/]";
            if (i == codexSelected) line = $"[bold]{line}[/]";
            body.Add(Framed(line, inner));
        }
        var chosen = crops[Math.Clamp(codexSelected, 0, crops.Length - 1)];
        var chosenName = farm.IsUnlocked(chosen.Name) ? chosen.Name : TuiText.HiddenName;
        var flavorTitle = string.Format(TuiText.FlavorTitle, $"[white]{Markup.Escape(chosenName)}[/]");
        var flavorBlock = new List<string>
        {
            Framed($"[{TuiColors.Muted}]── {flavorTitle} {new string('─', Math.Max(0, inner - VisibleWidth(flavorTitle) - 4))}[/]", inner),
            Framed("", inner)
        };
        var harvested = farm.HarvestCounts.GetValueOrDefault(chosen.Name);
        var flavor = Balance.Flavor.GetValueOrDefault(chosen.Name, []);
        for (var i = 0; i < Balance.FlavorThresholds.Length; i++)
        {
            var threshold = Balance.FlavorThresholds[i];
            var text = !farm.IsUnlocked(chosen.Name) ? TuiText.HiddenName
                : harvested >= threshold && i < flavor.Length ? flavor[i]
                : string.Format(TuiText.FlavorLocked, threshold);
            var color = harvested >= threshold && farm.IsUnlocked(chosen.Name) ? "default" : TuiColors.Muted;
            flavorBlock.Add(Framed($" - [{color}]{Markup.Escape(text)}[/]", inner));
        }
        flavorBlock.Add(Framed("", inner));
        while (body.Count < bodyHeight - flavorBlock.Count)
            body.Add(Framed("", inner));
        body.AddRange(flavorBlock);
        return body;
    }

    private static string TopBorder(TuiScreen screen, FarmState farm, int frameWidth)
    {
        var title = string.Join($"[{TuiColors.Muted}] │ [/]", TuiText.ScreenNames.Select((name, i) =>
            i == (int)screen ? $"[bold]{name}[/]" : $"[{TuiColors.Muted}]{name}[/]"));
        var right = screen switch
        {
            TuiScreen.Main => $"{(WorkingSessions(farm).Count > 0 ? WorkingFrame(DateTimeOffset.Now) : "")}[{TuiColors.Muted}]{farm.Planted.Count()}/{farm.Cells.Length} planted │ [/][{TuiColors.Gold}]{farm.Coins}G[/]",
            TuiScreen.Codex => $"[{TuiColors.Muted}]{farm.UnlockedCrops.Count}/{Balance.Crops.Length} discovered[/]",
            _ => $"[{TuiColors.Gold}]{farm.Coins}G[/]"
        };
        var tail = new string('─', Math.Max(0, frameWidth - VisibleWidth(title) - VisibleWidth(right) - 9));
        return $"[{TuiColors.Muted}]┌── [/]{title}[{TuiColors.Muted}] {tail} [/]{right}[{TuiColors.Muted}] ─┐[/]";
    }

    private static string Separator(int frameWidth) =>
        $"[{TuiColors.Muted}]├{new string('─', frameWidth - 2)}┤[/]";

    private static string Framed(string content, int inner)
    {
        var pad = new string(' ', Math.Max(0, inner - VisibleWidth(content)));
        return $"[{TuiColors.Muted}]│[/]{content}{pad}[{TuiColors.Muted}]│[/]";
    }

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex MarkupTag();

    private static int VisibleWidth(string markup) =>
        MarkupTag().Replace(markup.Replace("[[", "\0").Replace("]]", "\0"), "").Length;

    private static string GlyphColor(TuiCell cell) => cell.Glyph switch
    {
        TuiText.SeedGlyph or TuiText.SproutGlyph or TuiText.GrowingGlyph when !cell.Watered => TuiColors.Dry,
        TuiText.SeedGlyph or TuiText.SproutGlyph => TuiColors.Sprout,
        TuiText.GrowingGlyph => TuiColors.Growing,
        TuiText.ReadyGlyph => TuiColors.Gold,
        _ => TuiColors.Empty
    };

    private static IEnumerable<string> FieldLines(TuiCell[] cells, int selected)
    {
        for (var start = 0; start < cells.Length; start += Balance.Columns)
            foreach (var line in FieldRowLines(cells, start, selected))
                yield return line;
    }

    private static IEnumerable<string> FieldRowLines(TuiCell[] cells, int start, int selected)
    {
        var n = Balance.Columns;
        var indent = new string(' ', FieldSideMargin);
        var gap = new string(' ', CellGap);

        string Row(Func<int, string> box) =>
            indent + string.Join(gap, Enumerable.Range(start, n).Select(box));

        string Border(int i, char heavyL, char heavyR, char lightL, char lightR) => i == selected
            ? $"[{TuiColors.SelectedBorder}]{heavyL}{new string('━', CellInnerWidth)}{heavyR}[/]"
            : $"[{TuiColors.Muted}]{lightL}{new string('─', CellInnerWidth)}{lightR}[/]";

        yield return Row(i => Border(i, '┏', '┓', '┌', '┐'));
        yield return Row(i =>
        {
            var pad = CellInnerWidth / 2;
            var content =
                $"{new string(' ', pad)}[{GlyphColor(cells[i])}]{cells[i].Glyph}[/]{new string(' ', CellInnerWidth - pad - 1)}";
            return i == selected
                ? $"[{TuiColors.SelectedBorder}]┃[/]{content}[{TuiColors.SelectedBorder}]┃[/]"
                : $"[{TuiColors.Muted}]│[/]{content}[{TuiColors.Muted}]│[/]";
        });
        yield return Row(i => Border(i, '┗', '┛', '└', '┘'));
    }
}
