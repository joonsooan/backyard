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
    public const string GardenTitle = "backyard ── garden";
    public const string ShopTitle = "backyard ── shop";
    public const string MainKeysLine =
        $" wasd[{TuiColors.Muted}] move │ [/]f[{TuiColors.Muted}] harvest │ [/]tab[{TuiColors.Muted}] shop │ [/]c[{TuiColors.Muted}] codex │ [/]r[{TuiColors.Muted}] resize │ [/]esc[{TuiColors.Muted}] quit[/]";
    public const string ShopKeysLine =
        $" space[{TuiColors.Muted}] buy │ [/]tab[{TuiColors.Muted}] back │ [/]esc[{TuiColors.Muted}] quit[/]";
    public const string CodexKeysLine =
        $" c[{TuiColors.Muted}] back │ [/]esc[{TuiColors.Muted}] quit[/]";
    public const string EmptyLabel = "empty";
    public const string LockedLabel = "locked";
    public const string NotEnoughGold = "not enough gold";
    public const string SoldOut = "sold out";
    public const string RowItemName = "+row";
    public const string RowItemDesc = "adds one more row to your field";
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
}

public static class TuiColors
{
    public const string Muted = "grey";
    public const string Done = "green";
    public const string Dry = "tan";
    public const string Gold = "yellow";
    public const string Sprout = "green";
    public const string Growing = "bold green";
    public const string Ready = "yellow";
    public const string Unwatered = "tan";
    public const string Empty = "dim grey";
    public const string ProgressFull = "green";
    public const string ProgressEmpty = "grey";
    public const string SelectedBorder = "bold white";
}

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
        var shopMessage = "";
        var gardenMessage = "";
        var screen = TuiScreen.Main;
        var quit = false;

        AnsiConsole.AlternateScreen(() =>
        {
            AnsiConsole.Cursor.Hide();
            try
            {
                farm.Settle(DateTimeOffset.Now);
                AnsiConsole.Live(View(farm, selected, shopSelected, shopMessage, gardenMessage, screen)).Start(ctx =>
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
                                if (cropIndex < Balance.Crops.Count)
                                {
                                    if (farm.TryPlant(selected, Balance.Crops.Keys.ElementAt(cropIndex), DateTimeOffset.Now))
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
                                case ConsoleKey.Enter or ConsoleKey.Spacebar when screen == TuiScreen.Shop:
                                    shopMessage = Buy(farm, items[shopSelected].Id, () => save(farm));
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
                                    if (selected < farm.Cells.Length && farm.TryHarvest(selected))
                                        save(farm);
                                    break;
                                case ConsoleKey.Tab when screen != TuiScreen.Codex:
                                    screen = screen == TuiScreen.Shop ? TuiScreen.Main : TuiScreen.Shop;
                                    shopMessage = "";
                                    break;
                                case ConsoleKey.C when screen != TuiScreen.Shop:
                                    screen = screen == TuiScreen.Codex ? TuiScreen.Main : TuiScreen.Codex;
                                    break;
                                case ConsoleKey.R:
                                    TryResizeWindow(TuiText.ResizeWidth, TuiText.ResizeHeight);
                                    AnsiConsole.Clear();
                                    break;
                                case ConsoleKey.Q or ConsoleKey.Escape:
                                    quit = true;
                                    break;
                            }
                        }
                        farm.Settle(DateTimeOffset.Now);
                        ctx.UpdateTarget(View(farm, selected, shopSelected, shopMessage, gardenMessage, screen));
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

    private static int VisibleRowCount(FarmState farm) =>
        Math.Min(farm.Rows + 1, Balance.MaxRows);

    private static int VisibleCellCount(FarmState farm) =>
        VisibleRowCount(farm) * Balance.Columns;

    private static (string Name, string Price, string Desc, string Id)[] ShopItems(FarmState farm) =>
    [
        (TuiText.RowItemName,
            farm.Rows >= Balance.MaxRows ? TuiText.SoldOut : $"{Balance.NextRowPrice(farm.Rows)}G",
            TuiText.RowItemDesc, TuiText.RowItemName)
    ];

    private static string Buy(FarmState farm, string id, Action save)
    {
        if (farm.Rows >= Balance.MaxRows)
            return TuiText.SoldOut;
        if (!farm.TryExpand())
            return TuiText.NotEnoughGold;
        save();
        return "";
    }

    private static TuiCell CellView(FarmCell? cell, DateTimeOffset now)
    {
        if (cell is null)
            return new TuiCell(TuiText.EmptyGlyph, null, "", 0, "");
        var crop = Balance.Crops[cell.Crop];
        var glyph = StatusLine.Glyph(cell);
        var label = TuiText.StageLabels[cell.Stage * 3 / crop.MaxStage];
        if (cell.Stage >= crop.MaxStage)
            return new TuiCell(glyph, cell.Crop, label, 1, "");
        if (!cell.Watered)
            return new TuiCell(glyph, cell.Crop, label, 0, "waiting for water", false);
        var fraction = Math.Clamp((now - cell.StageStartedAt) / Balance.StageDuration, 0, 1);
        var remaining = cell.StageStartedAt + Balance.StageDuration - now;
        var nextLabel = cell.Stage + 1 >= crop.MaxStage ? "harvest" : "grow";
        return new TuiCell(glyph, cell.Crop, label, fraction, $"{FormatRemaining(remaining)} to {nextLabel}");
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

    private static Markup View(FarmState farm, int selected, int shopSelected, string shopMessage, string gardenMessage, TuiScreen screen) =>
        WindowTooSmall()
            ? TooSmallView()
            : Render(farm, selected, shopSelected, shopMessage, gardenMessage, screen);

    private static Markup TooSmallView() => new(string.Join("\n",
        TuiText.TooSmallTitle,
        "",
        string.Format(TuiText.TooSmallCurrent, Console.WindowWidth, Console.WindowHeight),
        string.Format(TuiText.TooSmallNeeds, TuiText.MinWidth, TuiText.MinHeight),
        "",
        TuiText.TooSmallResizeKey,
        TuiText.TooSmallQuitKey));

    private static Markup Render(FarmState farm, int selected, int shopSelected, string shopMessage, string gardenMessage, TuiScreen screen)
    {
        var frameWidth = Math.Max(TuiText.MinWidth, Console.WindowWidth - 2);
        var frameHeight = Math.Max(TuiText.MinHeight - 1, Console.WindowHeight - 1);
        var inner = frameWidth - 2;

        var cells = CellViews(farm, DateTimeOffset.Now);
        var bodyHeight = frameHeight - 4;
        var (title, body, keys) = screen switch
        {
            TuiScreen.Shop => (TuiText.ShopTitle, ShopBody(farm, inner, bodyHeight, shopSelected, shopMessage), TuiText.ShopKeysLine),
            TuiScreen.Codex => (CodexTitle(farm), CodexBody(farm, inner), TuiText.CodexKeysLine),
            _ => (TuiText.GardenTitle, MainBody(farm, cells, selected, gardenMessage, inner, bodyHeight), TuiText.MainKeysLine)
        };

        var lines = new List<string> { TopBorder(title, farm.Coins, frameWidth) };
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

    private static List<string> MainBody(FarmState farm, TuiCell[] cells, int selected, string gardenMessage, int inner, int bodyHeight)
    {
        var cell = cells[selected];
        var body = new List<string>();
        var columnRows = Math.Max(5, bodyHeight);
        var leftWidth = FieldWidth(Balance.Columns) + FieldSideMargin * 2;
        var panelWidth = Math.Max(3, inner - leftWidth - 1);
        var left = FieldLines(cells, selected).ToList();
        left.Insert(0, "");
        var panel = InfoPanel(farm, cell, gardenMessage, panelWidth);
        panel.Insert(0, "");

        for (var row = 0; row < columnRows; row++)
        {
            var l = row < left.Count ? left[row] : "";
            var r = row < panel.Count ? panel[row] : "";
            body.Add(Framed(PadTo(l, leftWidth) + $"[{TuiColors.Muted}]│[/]" + r, inner));
        }
        return body;
    }

    private static List<string> InfoPanel(FarmState farm, TuiCell cell, string gardenMessage, int panelWidth)
    {
        var panelInner = Math.Max(1, panelWidth - 2);
        if (cell.Glyph == TuiText.EmptyGlyph)
        {
            var empty = new List<string> { $" [{TuiColors.Muted}]{TuiText.EmptyLabel}[/]", "" };
            empty.AddRange(Balance.Crops.Select((kv, i) =>
                $" {i + 1} [{TuiColors.Muted}]{Markup.Escape(Trunc(kv.Key, panelInner - 8))}[/] [{TuiColors.Gold}]{kv.Value.SeedPrice}G[/]"));
            if (gardenMessage.Length > 0)
            {
                empty.Add("");
                empty.Add($" [{TuiColors.Dry}]{Markup.Escape(Trunc(gardenMessage, panelInner))}[/]");
            }
            return empty;
        }
        if (cell.Glyph == TuiText.LockedGlyph)
            return
            [
                $" [{TuiColors.Muted}]{TuiText.LockedLabel}[/]",
                "",
                $" [{TuiColors.Muted}]{Markup.Escape(Trunc($"+row {Balance.NextRowPrice(farm.Rows)}G in shop", panelInner))}[/]"
            ];

        var panel = new List<string>
        {
            $" [bold]{Markup.Escape(Trunc(cell.Crop ?? "", panelInner))}[/]",
            $" [{GlyphColor(cell)}]{cell.Glyph}[/] [{TuiColors.Muted}]{Markup.Escape(Trunc(cell.StageLabel, panelInner - 2))}[/]",
            "",
            " " + ProgressBarMarkup(cell.Fraction, Math.Min(TuiText.BarWidth, panelInner))
        };
        if (cell.Status.Length > 0)
            panel.Add(" " + StatusMarkup(Trunc(cell.Status, panelInner)));
        return panel;
    }

    private static string ProgressBarMarkup(double fraction, int barWidth)
    {
        var filled = (int)Math.Round(fraction * barWidth);
        var filledPart = new string(TuiText.StepFilled, filled);
        var rest = new string(TuiText.StepEmpty, barWidth - filled);
        return $"[{TuiColors.ProgressFull}]{filledPart}[/][{TuiColors.ProgressEmpty}]{rest}[/]";
    }

    private static string Trunc(string text, int width) =>
        text.Length <= width ? text : text[..width];

    private static string StatusMarkup(string text)
    {
        var t = text.Trim();
        var color = t switch
        {
            _ when t.Contains("ready") => TuiColors.Ready,
            _ when t.Contains("water") => TuiColors.Dry,
            _ => TuiColors.Muted
        };
        return $"[{color}]{Markup.Escape(t)}[/]";
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
        var panel = new List<string>
        {
            "",
            $" [bold]{Markup.Escape(Trunc(item.Name, panelInner))}[/]",
            $" [{TuiColors.Gold}]{Markup.Escape(item.Price)}[/]",
            "",
            $" [{TuiColors.Muted}]{Markup.Escape(Trunc(item.Desc, panelInner))}[/]"
        };
        if (shopMessage.Length > 0)
        {
            panel.Add("");
            panel.Add($" [{TuiColors.Dry}]{Markup.Escape(Trunc(shopMessage, panelInner))}[/]");
        }

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

    private static string CodexTitle(FarmState farm) =>
        $"backyard ── codex │ {farm.HarvestCounts.Count(kv => kv.Value > 0)}/{Balance.Crops.Count} discovered";

    private static List<string> CodexBody(FarmState farm, int inner)
    {
        var body = new List<string>
        {
            Framed("", inner),
            Framed($" [{TuiColors.Muted}]{"name",-17}{"stages",-9}{"sell",-7}harvested[/]", inner)
        };
        foreach (var (name, info) in Balance.Crops)
        {
            var count = farm.HarvestCounts.GetValueOrDefault(name);
            var line = count > 0
                ? $" [bold]{Markup.Escape(name).PadRight(17)}[/]{info.MaxStage,-9}{$"{info.SellPrice}G",-7}{count}"
                : $" [{TuiColors.Muted}]{"???",-17}{"-",-9}{"-",-7}0[/]";
            body.Add(Framed(line, inner));
        }
        return body;
    }

    private static string TopBorder(string title, int gold, int frameWidth)
    {
        var goldText = $"{gold}G";
        var tail = new string('─', Math.Max(0, frameWidth - title.Length - goldText.Length - 9));
        return $"[{TuiColors.Muted}]┌── [/][bold]{Markup.Escape(title)}[/][{TuiColors.Muted}] {tail} [/]" +
               $"[{TuiColors.Gold}]{goldText}[/][{TuiColors.Muted}] ─┐[/]";
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
        TuiText.SeedGlyph or TuiText.SproutGlyph or TuiText.GrowingGlyph when !cell.Watered => TuiColors.Unwatered,
        TuiText.SeedGlyph or TuiText.SproutGlyph => TuiColors.Sprout,
        TuiText.GrowingGlyph => TuiColors.Growing,
        TuiText.ReadyGlyph => TuiColors.Ready,
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
