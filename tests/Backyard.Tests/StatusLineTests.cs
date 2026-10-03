namespace Backyard.Tests;

public class StatusLineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static FarmCell Cell(string crop, int stage, bool watered = false) =>
        new() { Crop = crop, Stage = stage, Watered = watered, StageStartedAt = T0 };

    [Theory]
    [InlineData("carrot", 0, '.')]
    [InlineData("carrot", 1, ',')]
    [InlineData("carrot", 2, 'Y')]
    [InlineData("potato", 0, '.')]
    [InlineData("potato", 1, ',')]
    [InlineData("potato", 2, 'v')]
    [InlineData("potato", 3, 'Y')]
    public void Glyph_MapsStageProgressToFourChars(string crop, int stage, char expected)
    {
        Assert.Equal(expected, StatusLine.Glyph(Cell(crop, stage)));
    }

    [Fact]
    public void Glyph_EmptyCellIsUnderscore()
    {
        Assert.Equal('_', StatusLine.Glyph(null));
    }

    [Fact]
    public void FieldSegment_SingleRowListsAllCells()
    {
        var cells = new FarmCell?[10];
        cells[0] = Cell("carrot", 0);
        cells[1] = Cell("carrot", 1);
        cells[2] = Cell("potato", 2);
        cells[3] = Cell("potato", 3);

        Assert.Equal("[. , v Y _ _ _ _ _ _]", StatusLine.FieldSegment(cells, 1));
    }

    [Fact]
    public void FieldSegment_MultiRowSummarizesAndSkipsZeroCounts()
    {
        var cells = new FarmCell?[20];
        cells[0] = Cell("potato", 3);
        cells[1] = Cell("potato", 3);
        cells[2] = Cell("potato", 3);
        cells[3] = Cell("potato", 2);
        cells[4] = Cell("potato", 2);

        Assert.Equal("Y:3 v:2 _:15", StatusLine.FieldSegment(cells, 2));
    }

    private static FarmState Farm(params FarmCell?[] cells)
    {
        var padded = new FarmCell?[10];
        cells.CopyTo(padded, 0);
        return new FarmState { Coins = 12, Rows = 1, Cells = padded, FirstRunAt = T0 };
    }

    [Fact]
    public void Compose_GrowingShowsMinutesUntilNextStage()
    {
        var farm = Farm(Cell("carrot", 1, watered: true));
        var line = StatusLine.Compose(farm, T0 + TimeSpan.FromMinutes(6.5));

        Assert.Equal("[, _ _ _ _ _ _ _ _ _] │ 12G │ ~ 4m", line);
    }

    [Fact]
    public void Compose_GrowingShowsAtLeastOneMinute()
    {
        var farm = Farm(Cell("carrot", 1, watered: true));
        var line = StatusLine.Compose(farm, T0 + TimeSpan.FromMinutes(9.9));

        Assert.EndsWith("│ ~ 1m", line);
    }

    [Fact]
    public void Compose_UnwateredShowsDry()
    {
        var line = StatusLine.Compose(Farm(Cell("carrot", 0)), T0);

        Assert.Equal("[. _ _ _ _ _ _ _ _ _] │ 12G │ waiting", line);
    }

    [Fact]
    public void FieldSegment_MultiRowGroupWithAnyUnwateredIsTan()
    {
        var cells = new FarmCell?[20];
        cells[0] = Cell("carrot", 1, watered: true);
        cells[1] = Cell("carrot", 1);

        var segment = StatusLine.FieldSegment(cells, 2, color: true);

        Assert.Contains("\x1b[38;5;180m,\x1b[0m:2", segment);
    }

    [Fact]
    public void Compose_AllRipeShowsHarvestMark()
    {
        var line = StatusLine.Compose(Farm(Cell("carrot", 2), Cell("potato", 3)), T0);

        Assert.Equal("[Y Y _ _ _ _ _ _ _ _] │ 12G │ harvest!", line);
    }

    [Fact]
    public void Compose_AnyRipeShowsHarvestMarkOverGrowing()
    {
        var farm = Farm(Cell("carrot", 2), Cell("carrot", 1, watered: true));
        var line = StatusLine.Compose(farm, T0 + TimeSpan.FromMinutes(5));

        Assert.EndsWith("│ harvest!", line);
    }

    [Fact]
    public void Compose_EmptyFieldOmitsGrowthSegment()
    {
        Assert.Equal("[_ _ _ _ _ _ _ _ _ _] │ 12G", StatusLine.Compose(Farm(), T0));
    }

    [Fact]
    public void Compose_WithColor_WrapsGlyphsCoinsAndGrowthInAnsi()
    {
        var line = StatusLine.Compose(Farm(Cell("carrot", 2)), T0, color: true);

        Assert.Contains("\x1b[93mY\x1b[0m", line);
        Assert.Contains("\x1b[90m_\x1b[0m", line);
        Assert.Contains("\x1b[93m12G\x1b[0m", line);
        Assert.EndsWith("\x1b]8;;backyard://open\x1b\\\x1b[96mharvest!\x1b[0m\x1b]8;;\x1b\\", line);
    }

    [Fact]
    public void Compose_WithColor_WrapsGrowingMinutesInHyperlink()
    {
        var farm = Farm(Cell("carrot", 1, watered: true));
        var line = StatusLine.Compose(farm, T0 + TimeSpan.FromMinutes(6.5), color: true);

        Assert.EndsWith("\x1b]8;;backyard://open\x1b\\\x1b[32m~ 4m\x1b[0m\x1b]8;;\x1b\\", line);
    }

    [Fact]
    public void Compose_WithoutColor_HasNoHyperlink()
    {
        var line = StatusLine.Compose(Farm(Cell("carrot", 2)), T0);

        Assert.DoesNotContain("\x1b]8", line);
    }
}
