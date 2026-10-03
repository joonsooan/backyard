using System.Text.RegularExpressions;
using Spectre.Console;

namespace Backyard;

public sealed record TuiCell(char Glyph, string Info, string Progress);

public sealed record TuiSnapshot(int Gold, TuiCell[] Cells);

public enum TuiScreen
{
    Main,
    Shop
}

public static class TuiText
{
    public const int MinWidth = 100;
    public const int MinHeight = 20;
    public const string TooSmall = "Please enlarge the terminal to at least 100x20.";
    public const string GardenTitle = "backyard ── garden";
    public const string ShopTitle = "backyard ── shop";
    public const string MainKeysLine =
        $" wasd[{TuiColors.Muted}] move │ [/]f[{TuiColors.Muted}] harvest │ [/]tab[{TuiColors.Muted}] shop │ [/]c[{TuiColors.Muted}] codex │ [/]esc[{TuiColors.Muted}] quit[/]";
    public const string ShopKeysLine =
        $" w/s[{TuiColors.Muted}] select │ [/]space[{TuiColors.Muted}] buy │ [/]tab[{TuiColors.Muted}] back │ [/]esc[{TuiColors.Muted}] quit[/]";
    public const string AutoReplant = "auto-replant: on";
    public const string EmptyLabel = "empty";
    public const int BarWidth = 16;
    public const char StepFilled = '▰';
    public const char StepEmpty = '▱';

    public static readonly (string Name, string Price, string Desc)[] ShopItems =
    [
        ("carrot seed", "2G", "a quick, reliable starter crop"),
        ("potato seed", "5G", "slow to grow, sells for more"),
        ("+row", "50G", "adds one more row to your field")
    ];

    public const char SeedGlyph = '.';
    public const char SproutGlyph = ',';
    public const char GrowingGlyph = 'v';
    public const char ReadyGlyph = 'Y';
    public const char EmptyGlyph = '_';
}

public static class TuiColors
{
    public const string Muted = "grey";
    public const string Done = "green";
    public const string Dry = "tan";
    public const string Gold = "yellow";
    public const string Seed = "grey";
    public const string Sprout = "green";
    public const string Growing = "bold green";
    public const string Ready = "bold yellow";
    public const string Empty = "dim grey";
    public const string ProgressFull = "green";
    public const string ProgressEmpty = "grey";
    public const string SelectedBorder = "bold white";
}

public static class Tui
{
    private const int CellInnerWidth = 3;
    private const int CellGap = 1;

    private static int FieldWidth(int n) =>
        n * (CellInnerWidth + 2) + (n - 1) * CellGap;

    public static int Run(TuiSnapshot snapshot)
    {
        if (Console.WindowWidth < TuiText.MinWidth || Console.WindowHeight < TuiText.MinHeight)
        {
            Console.WriteLine(TuiText.TooSmall);
            return 1;
        }

        var selected = 0;
        var shopSelected = 0;
        var screen = TuiScreen.Main;
        var quit = false;

        AnsiConsole.AlternateScreen(() =>
        {
            AnsiConsole.Cursor.Hide();
            try
            {
                AnsiConsole.Live(Render(snapshot, selected, shopSelected, screen)).Start(ctx =>
                {
                    ctx.Refresh();
                    var lastSize = (Console.WindowWidth, Console.WindowHeight);
                    while (!quit)
                    {
                        var size = (Console.WindowWidth, Console.WindowHeight);
                        if (size != lastSize)
                        {
                            lastSize = size;
                            AnsiConsole.Clear();
                            ctx.UpdateTarget(size.Item1 < TuiText.MinWidth || size.Item2 < TuiText.MinHeight
                                ? new Markup(Markup.Escape(TuiText.TooSmall))
                                : Render(snapshot, selected, shopSelected, screen));
                        }
                        while (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(true);
                            var before = (selected, shopSelected, screen);
                            switch (key.Key)
                            {
                                case ConsoleKey.W or ConsoleKey.UpArrow when screen == TuiScreen.Shop:
                                    shopSelected = Math.Max(0, shopSelected - 1);
                                    break;
                                case ConsoleKey.S or ConsoleKey.DownArrow when screen == TuiScreen.Shop:
                                    shopSelected = Math.Min(TuiText.ShopItems.Length - 1, shopSelected + 1);
                                    break;
                                case ConsoleKey.Spacebar when screen == TuiScreen.Shop:
                                    break;
                                case ConsoleKey.A or ConsoleKey.W or ConsoleKey.LeftArrow or ConsoleKey.UpArrow
                                    when screen == TuiScreen.Main:
                                    selected = (selected - 1 + snapshot.Cells.Length) % snapshot.Cells.Length;
                                    break;
                                case ConsoleKey.D or ConsoleKey.S or ConsoleKey.RightArrow or ConsoleKey.DownArrow
                                    when screen == TuiScreen.Main:
                                    selected = (selected + 1) % snapshot.Cells.Length;
                                    break;
                                case ConsoleKey.Tab:
                                    screen = screen == TuiScreen.Shop ? TuiScreen.Main : TuiScreen.Shop;
                                    break;
                                case ConsoleKey.Escape:
                                    quit = true;
                                    break;
                            }
                            if (before != (selected, shopSelected, screen)
                                && Console.WindowWidth >= TuiText.MinWidth && Console.WindowHeight >= TuiText.MinHeight)
                                ctx.UpdateTarget(Render(snapshot, selected, shopSelected, screen));
                        }
                        if (!quit)
                            Thread.Sleep(200);
                    }
                });
            }
            finally
            {
                AnsiConsole.Cursor.Show();
            }
        });
        return 0;
    }

    private static Markup Render(TuiSnapshot s, int selected, int shopSelected, TuiScreen screen)
    {
        var frameWidth = Math.Max(TuiText.MinWidth, Console.WindowWidth - 2);
        var frameHeight = Math.Max(TuiText.MinHeight - 1, Console.WindowHeight - 1);
        var inner = frameWidth - 2;

        var bodyHeight = frameHeight - 4;
        var (title, body, keys) = screen switch
        {
            TuiScreen.Shop => (TuiText.ShopTitle, ShopBody(s, inner, bodyHeight, shopSelected), TuiText.ShopKeysLine),
            _ => (TuiText.GardenTitle, MainBody(s, selected, inner, bodyHeight), TuiText.MainKeysLine)
        };

        var lines = new List<string> { TopBorder(title, s.Gold, frameWidth) };
        lines.AddRange(body);
        var filler = bodyHeight - body.Count;
        for (var i = 0; i < filler; i++)
            lines.Add(Framed("", inner));
        lines.Add(Separator(frameWidth));
        lines.Add(Framed(keys, inner));
        lines.Add($"[{TuiColors.Muted}]└{new string('─', frameWidth - 2)}┘[/]");
        return new Markup(string.Join("\n", lines));
    }

    private const int FieldSideMargin = 3;

    private static List<string> MainBody(TuiSnapshot s, int selected, int inner, int bodyHeight)
    {
        var cell = s.Cells[selected];
        var body = new List<string>();
        var columnRows = Math.Max(5, bodyHeight);
        var leftWidth = FieldWidth(s.Cells.Length) + FieldSideMargin * 2;
        var panelWidth = Math.Max(3, inner - leftWidth - 1);
        var left = FieldLines(s.Cells, selected, leftWidth).ToList();
        left.Insert(0, "");
        var panel = InfoPanel(cell, panelWidth);
        panel.Insert(0, "");

        for (var row = 0; row < columnRows; row++)
        {
            var l = row < left.Count ? left[row] : "";
            var r = row < panel.Count ? panel[row] : "";
            body.Add(Framed(PadTo(l, leftWidth) + $"[{TuiColors.Muted}]│[/]" + r, inner));
        }
        return body;
    }

    private static List<string> InfoPanel(TuiCell cell, int panelWidth)
    {
        var panelInner = Math.Max(1, panelWidth - 2);
        if (cell.Glyph == TuiText.EmptyGlyph)
            return [$" [{TuiColors.Muted}]{TuiText.EmptyLabel}[/]"];

        var infoParts = cell.Info.Split('│', 2);
        var panel = new List<string> { $" [bold]{Markup.Escape(Trunc(infoParts[0].Trim(), panelInner))}[/]" };
        if (infoParts.Length > 1)
        {
            var stageParts = infoParts[1].Trim().Split(' ', 2);
            var label = stageParts.Length > 1 ? Trunc(stageParts[1], panelInner - 2) : "";
            panel.Add($" [{GlyphColor(cell.Glyph)}]{Markup.Escape(stageParts[0])}[/] [{TuiColors.Muted}]{Markup.Escape(label)}[/]");
        }
        panel.Add("");

        var progressParts = cell.Progress.Split(' ', 2);
        panel.Add(" " + ProgressBarMarkup(progressParts[0], Math.Min(TuiText.BarWidth, panelInner)));
        if (progressParts.Length > 1 && progressParts[1].Trim() is { Length: > 0 } text && text != "-")
            panel.Add(" " + StatusMarkup(Trunc(text, panelInner)));
        return panel;
    }

    private static string ProgressBarMarkup(string steps, int barWidth)
    {
        var filledSteps = steps.Count(c => c == TuiText.StepFilled);
        var totalSteps = filledSteps + steps.Count(c => c == TuiText.StepEmpty);
        if (totalSteps == 0)
            return "";
        var filled = (int)Math.Round((double)filledSteps / totalSteps * barWidth);
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
            "done" => TuiColors.Done,
            _ when t.Contains("ready") => TuiColors.Ready,
            _ when t.Contains("water") => TuiColors.Dry,
            _ => TuiColors.Muted
        };
        return $"[{color}]{Markup.Escape(t)}[/]";
    }

    private static string PadTo(string markup, int width) =>
        markup + new string(' ', Math.Max(0, width - VisibleWidth(markup)));

    private static List<string> ShopBody(TuiSnapshot s, int inner, int bodyHeight, int shopSelected)
    {
        var leftWidth = FieldWidth(s.Cells.Length) + FieldSideMargin * 2;
        var panelWidth = Math.Max(3, inner - leftWidth - 1);
        var left = new List<string> { "" };
        left.AddRange(TuiText.ShopItems.Select((item, i) =>
        {
            var name = Markup.Escape(item.Name.PadRight(22));
            return i == shopSelected
                ? $" [bold]> {name}[/][{TuiColors.Gold}]{item.Price}[/]"
                : $"   {name}[{TuiColors.Gold}]{item.Price}[/]";
        }));

        var item = TuiText.ShopItems[shopSelected];
        var panelInner = Math.Max(1, panelWidth - 2);
        var panel = new List<string>
        {
            "",
            $" [bold]{Markup.Escape(Trunc(item.Name, panelInner))}[/]",
            $" [{TuiColors.Gold}]{item.Price}[/]",
            "",
            $" [{TuiColors.Muted}]{Markup.Escape(Trunc(item.Desc, panelInner))}[/]"
        };

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

    private static int VisibleWidth(string markup) =>
        Regex.Replace(markup.Replace("[[", "\0").Replace("]]", "\0"), @"\[[^\]]*\]", "").Length;

    private static string GlyphColor(char glyph) => glyph switch
    {
        TuiText.SeedGlyph => TuiColors.Seed,
        TuiText.SproutGlyph => TuiColors.Sprout,
        TuiText.GrowingGlyph => TuiColors.Growing,
        TuiText.ReadyGlyph => TuiColors.Ready,
        _ => TuiColors.Empty
    };

    private static IEnumerable<string> FieldLines(TuiCell[] cells, int selected, int inner)
    {
        var n = cells.Length;
        var indent = new string(' ', Math.Max(0, (inner - FieldWidth(n)) / 2));
        var gap = new string(' ', CellGap);

        string Row(Func<int, string> box) =>
            indent + string.Join(gap, Enumerable.Range(0, n).Select(box));

        string Border(int i, char heavyL, char heavyR, char lightL, char lightR) => i == selected
            ? $"[{TuiColors.SelectedBorder}]{heavyL}{new string('━', CellInnerWidth)}{heavyR}[/]"
            : $"[{TuiColors.Muted}]{lightL}{new string('─', CellInnerWidth)}{lightR}[/]";

        yield return Row(i => Border(i, '┏', '┓', '┌', '┐'));
        yield return Row(i =>
        {
            var pad = CellInnerWidth / 2;
            var content =
                $"{new string(' ', pad)}[{GlyphColor(cells[i].Glyph)}]{cells[i].Glyph}[/]{new string(' ', CellInnerWidth - pad - 1)}";
            return i == selected
                ? $"[{TuiColors.SelectedBorder}]┃[/]{content}[{TuiColors.SelectedBorder}]┃[/]"
                : $"[{TuiColors.Muted}]│[/]{content}[{TuiColors.Muted}]│[/]";
        });
        yield return Row(i => Border(i, '┗', '┛', '└', '┘'));
    }
}
