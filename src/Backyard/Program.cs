using System.Text;
using Spectre.Console;

namespace Backyard;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        switch (args.FirstOrDefault())
        {
            case "status":
                Console.WriteLine("idle │ 120G");
                return 0;
            default:
                return Tui.Run(DummySnapshot());
        }
    }

    private static TuiSnapshot DummySnapshot()
    {
        var done = new string('▰', 10);
        var waiting = new string('▰', 3) + new string('▱', 7);
        var empty = new TuiCell(TuiText.EmptyGlyph, "empty", waiting + " -");
        return new TuiSnapshot(12,
        [
            new TuiCell(TuiText.ReadyGlyph, "carrot │ Y ready to harvest", done + " done"),
            new TuiCell(TuiText.SproutGlyph, "carrot │ , sprout", waiting + " 41m"),
            new TuiCell(TuiText.SeedGlyph, "carrot │ . seed", waiting + " waiting for water"),
            new TuiCell(TuiText.SeedGlyph, "potato │ . seed", waiting + " waiting for water"),
            empty, empty, empty, empty, empty, empty
        ]);
    }
}
