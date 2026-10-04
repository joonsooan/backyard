using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
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
                try
                {
                    Console.WriteLine(RunStatus());
                }
                catch (Exception)
                {
                    Console.WriteLine("! error");
                }
                return 0;
            case "install":
                return RunInstall();
            case "open":
                return RunOpen();
            case "register":
                return RunRegister();
            case "reset":
                return RunReset();
            case "disable":
                Console.WriteLine("backyard runs only when your statusline calls it.");
                Console.WriteLine(InstallText.DisableHint);
                Console.WriteLine("your farm state is kept; re-add the statusline to resume.");
                return 0;
            case "uninstall":
                return RunUninstall();
            case "doctor":
                return RunDoctor();
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
        var storage = new Storage(Storage.DefaultPath);
        File.Delete(storage.FilePath);
        File.Delete(storage.BackupPath);
        File.Delete(storage.CorruptPath);
        Console.WriteLine("farm reset. a new farm starts on the next run.");
        return 0;
    }

    private static int RunInstall()
    {
        var path = ClaudeSettingsFile.DefaultPath;
        try
        {
            var original = File.Exists(path) ? File.ReadAllText(path) : "";
            var updated = ClaudeSettingsFile.AddStatusline(original, ClaudeSettingsFile.CommandFor(Environment.ProcessPath));
            if (ReferenceEquals(updated, original))
                Console.WriteLine(InstallText.AlreadyInstalled);
            else
            {
                WriteSettings(path, original, updated);
                Console.WriteLine(InstallText.Registered);
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.WriteLine(string.Format(InstallText.UpdateFailed, path, e.Message));
            return 1;
        }

        if (OperatingSystem.IsWindows() && !IsLinkRegistered())
        {
            try
            {
                RunRegister();
            }
            catch (Exception)
            {
                Console.WriteLine(InstallText.LinkFailed);
            }
        }
        Console.WriteLine(InstallText.Done);
        return 0;
    }

    private static void WriteSettings(string path, string original, string updated)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
            File.Copy(path, path + ".bak", overwrite: true);
        var tmp = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(tmp, updated);
        File.Move(tmp, path, overwrite: true);
    }

    private static bool IsLinkRegistered()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        using var cmd = Registry.CurrentUser.OpenSubKey(@"Software\Classes\backyard\shell\open\command");
        var exe = Environment.ProcessPath;
        return exe is not null && cmd?.GetValue("") is string value && value.Contains(exe, StringComparison.OrdinalIgnoreCase);
    }

    private static int RunUninstall()
    {
        var settingsPath = ClaudeSettingsFile.DefaultPath;
        try
        {
            if (File.Exists(settingsPath))
            {
                var original = File.ReadAllText(settingsPath);
                var updated = ClaudeSettingsFile.RemoveStatusline(original, ClaudeSettingsFile.CommandFor(Environment.ProcessPath));
                if (!ReferenceEquals(updated, original))
                {
                    WriteSettings(settingsPath, original, updated);
                    Console.WriteLine(InstallText.Removed);
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.WriteLine(string.Format(InstallText.UpdateFailed, settingsPath, e.Message));
        }
        var dir = Path.GetDirectoryName(Storage.DefaultPath)!;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        if (OperatingSystem.IsWindows())
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\backyard", throwOnMissingSubKey: false);
        Console.WriteLine($"removed {dir}");
        Console.WriteLine(InstallText.UninstallDone);
        return 0;
    }

    private static int RunDoctor()
    {
        var now = DateTimeOffset.Now;
        var statePath = Storage.DefaultPath;
        SavedState saved;
        string stateDetail;
        try
        {
            saved = WithStateLock(() => SyncFarm(now));
            stateDetail = statePath;
        }
        catch (Exception e) when (e is JsonException or TimeoutException)
        {
            saved = new SavedState(now);
            stateDetail = e is JsonException ? string.Format(DoctorText.StateCorrupt, statePath) : DoctorText.StateLocked;
        }
        var stateOk = File.Exists(statePath) && stateDetail == statePath;

        var transcriptsOk = Directory.Exists(ProjectsRoot);
        var jsonlCount = transcriptsOk
            ? Directory.EnumerateFiles(ProjectsRoot, "*.jsonl", SearchOption.AllDirectories).Count()
            : 0;

        var parseOk = saved.Diag.ParseErrors == 0;
        var statuslineOk = IsStatuslineRegistered();
        var speed = SpeedEnv is { Length: > 0 } s ? s : DoctorText.DefaultSpeed;

        var table = new Table().Border(TableBorder.Square).BorderColor(Color.Grey);
        foreach (var header in DoctorText.Headers)
            table.AddColumn(new TableColumn($"[{TuiColors.Muted}]{header}[/]"));

        void Row(string item, bool ok, string detail) =>
            table.AddRow(item, ok ? DoctorText.Ok : $"[{TuiColors.Dry}]{DoctorText.Bad}[/]", Markup.Escape(detail));

        Row(DoctorText.State, stateOk, File.Exists(statePath) ? stateDetail : DoctorText.StateMissing);
        Row(DoctorText.Transcripts, transcriptsOk, transcriptsOk ? $"{ProjectsRoot} ({jsonlCount} jsonl files)" : DoctorText.TranscriptsMissing);
        Row(DoctorText.LastEvent, true, FormatAgo(saved.Diag.LastDetectedAt, now));
        Row(DoctorText.Parse, parseOk, $"{saved.Diag.ParseErrors} errors, {saved.Diag.UnknownLines} unknown lines");
        Row(DoctorText.Statusline, statuslineOk, statuslineOk ? DoctorText.StatuslineRegistered : DoctorText.StatuslineMissing);
        Row(DoctorText.Speed, true, speed);

        AnsiConsole.Write(table);
        return stateOk && transcriptsOk && parseOk && statuslineOk ? 0 : 1;
    }

    private static string FormatAgo(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is null)
            return DoctorText.Never;
        var span = now - at.Value;
        if (span.TotalMinutes < 60)
            return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24)
            return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }

    private static bool IsStatuslineRegistered()
    {
        try
        {
            var settings = JsonSerializer.Deserialize(File.ReadAllText(ClaudeSettingsFile.DefaultPath), BackyardJson.Default.ClaudeSettings);
            var command = settings?.StatusLine?.Command?.Trim();
            return command is not null && command.Contains("backyard") && command.EndsWith("status");
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string RunStatus()
    {
        var now = DateTimeOffset.Now;
        return WithStateLock(() =>
        {
            var farm = SyncFarm(now);
            return StatusLine.Compose(farm, now, color: true);
        });
    }

    private static int RunTui()
    {
        var farm = WithStateLock(() => SyncFarm(DateTimeOffset.Now));
        return Tui.Run(farm,
            save: f =>
            {
                try
                {
                    WithStateLock(() =>
                    {
                        var storage = new Storage(Storage.DefaultPath);
                        var saved = (SavedState)f;
                        if (storage.Load() is { } onDisk)
                        {
                            saved.WatcherCursors = onDisk.WatcherCursors;
                            saved.Diag = onDisk.Diag;
                        }
                        storage.Save(saved);
                        return 0;
                    });
                }
                catch (Exception)
                {
                }
            },
            resync: () => WithStateLock(() => (FarmState)SyncFarm(DateTimeOffset.Now)));
    }

    private static string ProjectsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

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
            if (!owned)
                throw new TimeoutException("another backyard process holds the state lock");
            return action();
        }
        finally
        {
            if (owned)
                mutex.ReleaseMutex();
        }
    }

    private static string? SpeedEnv =>
#if DEBUG
        Environment.GetEnvironmentVariable("BACKYARD_SPEED");
#else
        null;
#endif

    private static SavedState SyncFarm(DateTimeOffset now)
    {
        if (int.TryParse(SpeedEnv, out var speed) && speed >= 1)
            Balance.Speed = speed;

        var storage = new Storage(Storage.DefaultPath);
        var saved = storage.Load() is { } loaded && loaded.FirstRunAt != default ? loaded : new SavedState(now);

        var watcher = new Watcher(saved.WatcherCursors);
        var events = new List<WatchEvent>();
        if (Directory.Exists(ProjectsRoot))
            events = watcher.Scan(ProjectsRoot);

        saved.Apply(events, now);

        saved.Diag.ParseErrors += watcher.ParseErrorCount;
        saved.Diag.UnknownLines += watcher.UnknownLineCount;
        if (events.Count > 0)
            saved.Diag.LastDetectedAt = watcher.LastEventTimestamp;
        storage.Save(saved);
        return saved;
    }
}

public static class InstallText
{
    public const string AlreadyInstalled = "already installed";
    public const string Registered = "statusline registered";
    public const string Removed = "statusline removed";
    public const string LinkFailed = "link registration failed; ctrl+click will not work. run `backyard register` later.";
    public const string Done = "start a new Claude Code session to see your farm.";
    public const string UpdateFailed = "could not update {0}: {1}";
    public const string UninstallDone = "to finish: dotnet tool uninstall -g backyard-farm";
    public const string DisableHint = "run `backyard uninstall`, or remove 'backyard status' from ~/.claude/settings.json (statusLine).";
}

public static class DoctorText
{
    public static readonly string[] Headers = ["item", "status", "detail"];
    public const string Ok = "ok";
    public const string Bad = "!";
    public const string State = "state";
    public const string Transcripts = "transcripts";
    public const string LastEvent = "last event";
    public const string Parse = "parse";
    public const string Statusline = "statusline";
    public const string Speed = "speed";
    public const string StateMissing = "not created yet (runs on first status)";
    public const string StateCorrupt = "unreadable and no usable .bak; kept as {0}.corrupt. run backyard reset to start over";
    public const string StateLocked = "another backyard process holds the state lock";
    public const string TranscriptsMissing = "~/.claude/projects not found";
    public const string StatuslineRegistered = "registered in ~/.claude/settings.json";
    public const string StatuslineMissing = "not registered in ~/.claude/settings.json";
    public const string Never = "never";
    public const string DefaultSpeed = "1x";
}
