using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SwebKit.Core.Diagnostics;

/// <summary>
/// Deletes expired <c>&lt;feature&gt;-yyyy-MM-dd.log</c> files from the logs directory.
/// Safe to run on every startup — see <c>docs/features/active/structured-file-logging/decisions.md</c> D8.
/// </summary>
public sealed class LogRetentionCleanupService : ILogRetentionCleanupService
{
    public const int DefaultMaxAgeDays = 7;

    private static readonly Regex FeatureDateFileRegex = new(
        @"^(?<feature>.+)-(?<date>\d{4}-\d{2}-\d{2})\.log$",
        RegexOptions.Compiled);

    private readonly string _logsDirectory;
    private readonly int _maxAgeDays;

    public LogRetentionCleanupService(string logsDirectory, int maxAgeDays = DefaultMaxAgeDays)
    {
        _logsDirectory = logsDirectory;
        _maxAgeDays = maxAgeDays;
    }

    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(_logsDirectory))
                return Task.CompletedTask;

            var cutoff = DateOnly.FromDateTime(DateTime.Now).AddDays(-_maxAgeDays);

            var canonicalLogsDirectory = Path.GetFullPath(_logsDirectory);

            foreach (var file in Directory.EnumerateFiles(canonicalLogsDirectory, "*.log"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeleteIfExpired(file, cutoff, canonicalLogsDirectory);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LogRetentionCleanupService: cleanup pass failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static void DeleteIfExpired(string file, DateOnly cutoff, string canonicalLogsDirectory)
    {
        try
        {
            // Validate the leaf filename itself before ever re-deriving a path from it — reject any
            // path-traversal-shaped segment ("..", "/", "\") even though `file` already comes straight
            // from Directory.EnumerateFiles over a fixed, non-user-controlled directory.
            var fileName = Path.GetFileName(file);
            if (string.IsNullOrEmpty(fileName) ||
                fileName.Contains("..", StringComparison.Ordinal) ||
                fileName.Contains('/') ||
                fileName.Contains('\\'))
                return;

            var match = FeatureDateFileRegex.Match(fileName);
            if (!match.Success)
                return;

            if (!DateOnly.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", out var fileDate))
                return;

            if (fileDate >= cutoff)
                return;

            // Re-derive the delete target from the known-safe logs directory + the validated leaf
            // filename, rather than trusting the enumerated path string directly, then re-confirm
            // containment as a final defense-in-depth check before deleting.
            var safePath = Path.GetFullPath(Path.Combine(canonicalLogsDirectory, fileName));
            if (!safePath.StartsWith(canonicalLogsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return;

            File.Delete(safePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LogRetentionCleanupService: failed to process '{file}': {ex.Message}");
        }
    }
}
