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
                return RunTui();
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
        return WithStateLock(() =>
        {
            var (storage, saved, farm) = SyncFarm(now);
            storage.Save(saved);
            return StatusLine.Compose(farm, now, color: true);
        });
    }

    private static int RunTui()
    {
        var farm = WithStateLock(() => SyncFarm(DateTimeOffset.Now)).Farm;
        return Tui.Run(farm,
            save: f => WithStateLock(() =>
            {
                var storage = new Storage(Storage.DefaultPath);
                var saved = storage.Load() ?? new SavedState();
                saved.CopyFrom(f);
                storage.Save(saved);
                return 0;
            }),
            resync: () => WithStateLock(() =>
            {
                var (storage, saved, synced) = SyncFarm(DateTimeOffset.Now);
                storage.Save(saved);
                return synced;
            }));
    }

    private static T WithStateLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, "backyard-state");
        var owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }
            return action();
        }
        finally
        {
            if (owned)
                mutex.ReleaseMutex();
        }
    }

    private static (Storage Storage, SavedState Saved, FarmState Farm) SyncFarm(DateTimeOffset now)
    {
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
        return (storage, saved, farm);
    }
}
