using System.Text;

namespace Backyard;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        switch (args.FirstOrDefault())
        {
            case "status":
                try
                {
                    Console.WriteLine(RunStatus());
                }
                catch (Exception)
                {
                    Console.WriteLine("! error");
                }
                return 0;
            case "reset":
                return RunReset();
            case "disable":
                Console.WriteLine("backyard runs only when your statusline calls it.");
                Console.WriteLine("remove the 'backyard status' command from ~/.claude/settings.json (statusLine) to disable.");
                Console.WriteLine("your farm state is kept; re-add the statusline to resume.");
                return 0;
            case "uninstall":
                return RunUninstall();
            default:
                return Tui.Run(DummySnapshot());
        }
    }

    private static int RunReset()
    {
        var path = Storage.DefaultPath;
        File.Delete(path);
        File.Delete(path + ".bak");
        Console.WriteLine("farm reset. a new farm starts on the next run.");
        return 0;
    }

    private static int RunUninstall()
    {
        var dir = Path.GetDirectoryName(Storage.DefaultPath)!;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Console.WriteLine($"removed {dir}");
        Console.WriteLine("to finish: remove 'backyard status' from your statusline settings, then delete the backyard binary.");
        return 0;
    }

    private static string RunStatus()
    {
        var now = DateTimeOffset.Now;
        if (int.TryParse(Environment.GetEnvironmentVariable("BACKYARD_SPEED"), out var speed) && speed >= 1)
            Balance.Speed = speed;

        var storage = new Storage(Storage.DefaultPath);
        var saved = storage.Load() ?? new SavedState();
        var farm = saved.FirstRunAt == default ? FarmState.CreateNew(now) : saved.ToFarmState();

        var watcher = new Watcher(saved.WatcherCursors);
        var events = new List<WatchEvent>();
        var projectsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        if (Directory.Exists(projectsRoot))
            events = watcher.Scan(projectsRoot);

        farm.Apply(events, now);

        saved.CopyFrom(farm);
        saved.Diag.ParseErrors += watcher.ParseErrorCount;
        saved.Diag.UnknownLines += watcher.UnknownLineCount;
        if (events.Count > 0)
            saved.Diag.LastDetectedAt = watcher.LastEventTimestamp;
        storage.Save(saved);

        return StatusLine.Compose(farm, now, color: true);
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
