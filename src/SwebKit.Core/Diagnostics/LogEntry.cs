using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SwebKit.Core.Diagnostics;

/// <summary>
/// Immutable, already-redacted representation of a single structured log line.
/// </summary>
/// <remarks>
/// <paramref name="Exception"/> is a pre-serialized (type/message/stack) string, already
/// passed through <see cref="LogRedactor"/> — never a live <see cref="System.Exception"/> instance.
/// </remarks>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Feature,
    int EventId,
    string Message,
    string? Exception = null,
    IReadOnlyDictionary<string, string>? ScopeState = null)
{
    /// <summary>
    /// Builds a <see cref="LogLevel.Critical"/> entry for the global crash handlers
    /// (<c>AppDomain.CurrentDomain.UnhandledException</c> / <c>TaskScheduler.UnobservedTaskException</c>).
    /// </summary>
    public static LogEntry ForCrash(Exception? exception, bool isTerminating)
    {
        var message = isTerminating
            ? "Unhandled exception - application is terminating"
            : "Unobserved task exception";

        var exceptionText = exception is null ? null : SerializeAndRedactException(exception);

        return new LogEntry(
            Timestamp: DateTimeOffset.Now,
            Level: LogLevel.Critical,
            Category: "CrashHandler",
            Feature: "general",
            EventId: 0,
            Message: LogRedactor.Redact(message) ?? message,
            Exception: exceptionText,
            ScopeState: null);
    }

    internal static string SerializeAndRedactException(Exception exception)
    {
        var serialized = JsonSerializer.Serialize(new
        {
            type = exception.GetType().FullName,
            message = exception.Message,
            stack = exception.StackTrace
        });

        return LogRedactor.Redact(serialized) ?? serialized;
    }
}
