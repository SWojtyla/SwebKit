using SwebKit.Core.Configuration;

namespace SwebKit.App.Services;

internal static class PerformanceBaselineRecorder
{
    private static readonly object Sync = new();

    public static void Record(string category, string message)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(AppDataPaths.PerformanceBaselineLog);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"{DateTimeOffset.Now:O} [{category}] {message}{Environment.NewLine}";
            lock (Sync)
            {
                File.AppendAllText(AppDataPaths.PerformanceBaselineLog, line);
            }
        }
        catch
        {
        }
    }
}