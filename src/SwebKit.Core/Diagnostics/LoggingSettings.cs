using Microsoft.Extensions.Logging;

namespace SwebKit.Core.Diagnostics;

/// <summary>Persisted diagnostics/logging preference, stored as part of <c>user-settings.json</c>.</summary>
public sealed class LoggingSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Defaults to <see cref="LogLevel.Warning"/> for minimal overhead — only warnings, errors, and
    /// crash entries are captured out of the box. Users can lower this to Information/Debug/Trace
    /// from Settings → Diagnostics when they need deeper history while troubleshooting an issue.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Warning;
}
