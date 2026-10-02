using System.Text.Json;

namespace Backyard.Tests;

public class StateTests
{
    [Fact]
    public void State_RoundTripsThroughSourceGeneratedJson()
    {
        var json = JsonSerializer.Serialize(new State(22), BackyardJson.Default.State);
        Assert.Equal(22, JsonSerializer.Deserialize(json, BackyardJson.Default.State)!.Coins);
    }
}
