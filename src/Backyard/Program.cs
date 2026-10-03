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
                AnsiConsole.MarkupLine("[green]backyard[/] — hello world");
                return 0;
        }
    }
}

public record State(int Coins);

[JsonSerializable(typeof(State))]
[JsonSerializable(typeof(TranscriptLine))]
public partial class BackyardJson : JsonSerializerContext;
