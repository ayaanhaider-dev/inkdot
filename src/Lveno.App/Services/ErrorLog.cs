namespace Lveno.App.Services;

/// <summary>
/// Unexpected errors go here instead of closing the app.
/// File: ~/Library/Application Support/InkDot/Logs/lveno-app-errors.log
/// </summary>
public static class ErrorLog
{
    public static string FilePath => Path.Combine(DriverService.AppDataDirectory, "Logs", "lveno-app-errors.log");

    public static void Write(Exception? ex, string where = "")
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"--- {DateTime.Now:O} {where}\n{ex}\n\n");
            Console.Error.WriteLine($"Lveno error ({where}): {ex}");
        }
        catch
        {
            // Never let logging itself crash the app.
        }
    }
}
