namespace SwebKit.Core.Diagnostics;

/// <summary>Startup, best-effort cleanup of expired feature-bucket log files.</summary>
public interface ILogRetentionCleanupService
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
