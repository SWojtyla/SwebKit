using System.Text.RegularExpressions;

namespace SwebKit.Core.Services;

/// <summary>
/// Splits the timestamp Kubernetes prepends to a log line (with <c>timestamps=true</c>) from
/// the message itself.
/// </summary>
/// <remarks>
/// <para>
/// Two wire shapes exist and both must parse. Kubernetes emits RFC3339Nano —
/// <c>2026-09-09T10:22:30.118456789Z message</c> — while <see cref="DemoAksClient"/> emits a
/// space-separated local form, <c>2026-09-09 10:22:30.118  message</c>. The pattern
/// deliberately accepts either separator so one parser serves both clients.
/// </para>
/// <para>
/// Splitting matters beyond display: a text filter applied to the whole line matches the
/// timestamp as readily as the message, so filtering for <c>2026</c> would return every line.
/// Callers filter <see cref="LogLineParts.Message"/>, never the raw line.
/// </para>
/// </remarks>
public static partial class LogLineTimestamp
{
    [GeneratedRegex(
        @"^(?<timestamp>\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?)\s+(?<message>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex PrefixedLineRegex();

    /// <summary>A log line separated into its optional timestamp prefix and its message.</summary>
    /// <param name="Timestamp">The parsed prefix, or <see langword="null"/> when the line carries none.</param>
    /// <param name="Message">The line with any timestamp prefix removed. Never <see langword="null"/>.</param>
    /// <param name="RawTimestamp">The prefix exactly as it appeared, for round-tripping.</param>
    public readonly record struct LogLineParts(DateTimeOffset? Timestamp, string Message, string? RawTimestamp);

    /// <summary>
    /// Splits <paramref name="line"/>. A line with no recognisable prefix comes back whole as
    /// <see cref="LogLineParts.Message"/> with a null timestamp, so this is safe to call on
    /// output from a client that was not asked for timestamps.
    /// </summary>
    public static LogLineParts Split(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return new LogLineParts(null, line ?? string.Empty, null);
        }

        var match = PrefixedLineRegex().Match(line);
        if (!match.Success)
        {
            return new LogLineParts(null, line, null);
        }

        var raw = match.Groups["timestamp"].Value;
        var message = match.Groups["message"].Value;

        // A shape match is not a value match — 2026-13-45T99:99:99Z satisfies the pattern.
        // Without the parse check that would be stripped off and silently lost.
        return DateTimeOffset.TryParse(raw, out var parsed)
            ? new LogLineParts(parsed, message, raw)
            : new LogLineParts(null, line, null);
    }

    /// <summary>The message portion of <paramref name="line"/>, for matching a user's filter against.</summary>
    public static string MessageOf(string? line) => Split(line).Message;

    /// <summary>
    /// True when <paramref name="filter"/> is empty, or when it appears in the line's message.
    /// Deliberately ignores the timestamp prefix.
    /// </summary>
    public static bool MatchesFilter(string? line, string? filter) =>
        string.IsNullOrEmpty(filter) || MessageOf(line).Contains(filter, StringComparison.OrdinalIgnoreCase);
}
