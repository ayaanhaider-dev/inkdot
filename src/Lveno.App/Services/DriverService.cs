using System.Diagnostics;
using OpenTabletDriver;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Desktop.RPC;
using OpenTabletDriver.Plugin.Tablet;

namespace Lveno.App.Services;

/// <summary>
/// Starts the OpenTabletDriver daemon (the part that reads the pen), installs Lveno's plugin
/// into it, and talks to it over its local pipe.
/// </summary>
public sealed class DriverService
{
    /// <summary>The daemon's pipe and single-instance name. Fixed by OpenTabletDriver.</summary>
    public const string DaemonName = "OpenTabletDriver.Daemon";

    private static readonly string Support = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");

    /// <summary>InkDot keeps its own settings and plugins, apart from a stock OpenTabletDriver install.</summary>
    public static string AppDataDirectory { get; } = MoveOldFolder(Path.Combine(Support, "InkDot"));

    /// <summary>Settings from before the rename (the "Lveno Tablet" folder) move over once.</summary>
    private static string MoveOldFolder(string folder)
    {
        var old = Path.Combine(Support, "Lveno Tablet");
        try
        {
            if (Directory.Exists(old) && !Directory.Exists(folder))
                Directory.Move(old, folder);
        }
        catch
        {
            // Still in use by an old copy; it moves on the next start.
        }
        return folder;
    }

    public static string PluginFolder => Path.Combine(AppDataDirectory, "Plugins", "Lveno");

    private RpcClient<IDriverDaemon>? _client;
    private Process? _daemon;

    public IDriverDaemon Daemon => _client?.Instance ?? throw new InvalidOperationException("Not connected to the driver.");

    public bool IsConnected => _client?.IsConnected == true;

    /// <summary>True when this app started the driver (so it has read the current screen layout).</summary>
    public bool StartedDaemon { get; private set; }

    /// <summary>Raised on a thread-pool thread when the daemon goes away.</summary>
    public event EventHandler? Disconnected;

    /// <summary>Raised on a thread-pool thread when tablets are plugged in or out.</summary>
    public event EventHandler<IEnumerable<TabletReference>>? TabletsChanged;

    /// <summary>Raw pen reports, only while <c>SetTabletDebug(true)</c> is on. Thread-pool thread.</summary>
    public event EventHandler<DebugReportData>? DeviceReport;

    public async Task ConnectAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        bool running = IsDaemonRunning;

        // In dev mode (scripts/dev.sh) the installed app owns the driver, because it holds the
        // permissions, and plugins are installed only by `dev.sh plugin`.
        bool devMode = Environment.GetEnvironmentVariable("LVENO_DEV") == "1";
        bool pluginChanged = !devMode && InstallPlugin();
        if (!running)
        {
            if (devMode)
                await StartThroughInstalledAppAsync(ct);
            else
                StartDaemon();
            StartedDaemon = true;
        }

        var client = new RpcClient<IDriverDaemon>(DaemonName);
        client.Disconnected += (_, _) => Disconnected?.Invoke(this, EventArgs.Empty);

        var connect = client.Connect();
        if (await Task.WhenAny(connect, Task.Delay(timeout, ct)) != connect)
            throw new TimeoutException("The driver didn't answer in time.");
        await connect;

        _client = client;
        client.Instance!.TabletsChanged += (_, tablets) => TabletsChanged?.Invoke(this, tablets);
        client.Instance!.DeviceReport += (_, report) => DeviceReport?.Invoke(this, report);

        // Mirror the daemon's folders and plugin list in this process, as OpenTabletDriver's own UI does.
        AppInfo.Current = await Daemon.GetApplicationInfo();
        if (running && pluginChanged)
            await Daemon.LoadPlugins();
        AppInfo.PluginManager.Load();
    }

    public static bool IsDaemonRunning =>
        Instance.Exists(DaemonName) || Process.GetProcessesByName(DaemonName).Length > 0;

    /// <summary>
    /// Dev mode: start the driver from the installed app, which has the macOS permissions,
    /// then close that app's window. The driver keeps running.
    /// </summary>
    private static async Task StartThroughInstalledAppAsync(CancellationToken ct)
    {
        const string app = "InkDot";
        Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-a", app, "--args", "--background" }, UseShellExecute = false });
        for (int i = 0; i < 30 && !IsDaemonRunning; i++)
            await Task.Delay(500, ct);
        await Task.Delay(2000, ct);
        Process.Start(new ProcessStartInfo("osascript") { ArgumentList = { "-e", $"tell application \"{app}\" to quit" }, UseShellExecute = false });
    }

    /// <summary>Stops the daemon. The pen stops working until Lveno starts it again.</summary>
    public void StopDaemon()
    {
        foreach (var process in Process.GetProcessesByName(DaemonName))
        {
            try { process.Kill(); } catch { /* already gone */ }
        }
        _daemon = null;
    }

    /// <summary>
    /// Copies Lveno.Plugins.dll into the daemon's plugin folder when it's missing or out of date.
    /// The plugin dll ships next to this app because the app references the plugin project.
    /// </summary>
    private static bool InstallPlugin()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Lveno.Plugins.dll");
        if (!File.Exists(source))
            return false;

        Directory.CreateDirectory(PluginFolder);
        var target = Path.Combine(PluginFolder, "Lveno.Plugins.dll");
        // Newest wins, so a plugin copied in by scripts/dev.sh isn't replaced by an older bundled one.
        if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source))
            return false;

        File.Copy(source, target, overwrite: true);
        return true;
    }

    private void StartDaemon()
    {
        var start = FindDaemon() ?? throw new FileNotFoundException(
            "Couldn't find the driver. Build it with scripts/build.sh, or set LVENO_DAEMON to its path.");

        start.ArgumentList.Add("--appdata");
        start.ArgumentList.Add(AppDataDirectory);
        start.UseShellExecute = false;
        start.WorkingDirectory = Path.GetDirectoryName(start.FileName == "dotnet" ? start.ArgumentList[0] : start.FileName)!;

        Directory.CreateDirectory(AppDataDirectory);
        _daemon = Process.Start(start);
    }

    /// <summary>
    /// Looks for the daemon in this order: $LVENO_DAEMON, next to this app (the packaged .app),
    /// then build/daemon/ in the repo (development).
    /// </summary>
    private static ProcessStartInfo? FindDaemon()
    {
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable("LVENO_DAEMON") is { Length: > 0 } custom)
            candidates.Add(custom);

        candidates.Add(Path.Combine(AppContext.BaseDirectory, DaemonName));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, DaemonName + ".dll"));

        // src/Lveno.App/bin/<config>/net10.0/ → repo root → build/daemon
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        candidates.Add(Path.Combine(repo, "build", "daemon", DaemonName));
        candidates.Add(Path.Combine(repo, "build", "daemon", DaemonName + ".dll"));

        foreach (var path in candidates.Where(File.Exists))
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                var info = new ProcessStartInfo(DotnetHost());
                info.ArgumentList.Add(path);
                return info;
            }
            return new ProcessStartInfo(path);
        }
        return null;
    }

    private static string DotnetHost()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root && File.Exists(Path.Combine(root, "dotnet")))
            return Path.Combine(root, "dotnet");
        var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet");
        return File.Exists(home) ? home : "dotnet";
    }
}
