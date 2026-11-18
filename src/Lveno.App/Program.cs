using Avalonia;

namespace Lveno.App;

internal static class Program
{
    // Held for the life of the app: a second copy can't take it, so it quits instead of running twice.
    private static FileStream? _singleInstance;

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            Directory.CreateDirectory(Services.DriverService.AppDataDirectory);
            _singleInstance = new FileStream(Path.Combine(Services.DriverService.AppDataDirectory, "app.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            Console.Error.WriteLine("InkDot is already running.");
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = true })
            .LogToTrace();
}
