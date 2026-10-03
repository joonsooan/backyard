using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

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
            case "open":
                return RunOpen();
            case "register":
                return RunRegister();
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmd);

    private const int SwRestore = 9;

    private static int RunOpen()
    {
        if (OperatingSystem.IsWindows())
        {
            var hwnd = FindWindowW(null, Tui.WindowTitle);
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SwRestore);
                SetForegroundWindow(hwnd);
                return 0;
            }
        }
        var exe = Environment.ProcessPath ?? "backyard";
        try
        {
            Process.Start(new ProcessStartInfo("wt")
            {
                ArgumentList = { "-w", "new", "--size", $"{TuiText.ResizeWidth},{TuiText.ResizeHeight}", "nt", exe }
            });
        }
        catch (Exception)
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        return 0;
    }

    private static int RunRegister()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("register is windows-only. on other platforms the statusline link needs no setup if your terminal maps backyard:// yourself.");
            return 1;
        }
        var exe = Environment.ProcessPath ?? "backyard";
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\backyard");
        key.SetValue("", "URL:backyard");
        key.SetValue("URL Protocol", "");
        using var cmd = key.CreateSubKey(@"shell\open\command");
        cmd.SetValue("", $"\"{exe}\" open");
        Console.WriteLine("registered backyard:// — ctrl+click the statusline growth text to open the farm.");
        return 0;
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
        if (OperatingSystem.IsWindows())
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\backyard", throwOnMissingSubKey: false);
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
            save: f =>
            {
                try
                {
                    WithStateLock(() =>
                    {
                        var storage = new Storage(Storage.DefaultPath);
                        var saved = storage.Load() ?? new SavedState();
                        saved.CopyFrom(f);
                        storage.Save(saved);
                        return 0;
                    });
                }
                catch (Exception)
                {
                }
            },
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
