using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
                var json = JsonSerializer.Serialize(new State(Coins: 120), BackyardJson.Default.State);
                var state = JsonSerializer.Deserialize(json, BackyardJson.Default.State)!;
                Console.WriteLine($"idle │ {state.Coins}G");
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

public record State(int Coins);

[JsonSerializable(typeof(State))]
[JsonSerializable(typeof(TranscriptLine))]
public partial class BackyardJson : JsonSerializerContext;
