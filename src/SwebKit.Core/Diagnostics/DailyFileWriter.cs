using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SwebKit.Core.Diagnostics;

/// <summary>
/// Writes NDJSON log lines for a single feature bucket to <c>&lt;directory&gt;/&lt;feature&gt;-yyyy-MM-dd.log</c>
/// (local date), auto-rolling to a new file at midnight and enforcing a per-day soft size cap.
/// Takes an explicit directory (never calls <c>AppDataPaths</c> itself) so it is fully unit-testable.
/// </summary>
public sealed class DailyFileWriter : IDisposable
{
    public const long DefaultMaxDailyFileSizeBytes = 20 * 1024 * 1024; // 20 MB

    private readonly string _directory;
    private readonly string _feature;
    private readonly long _maxDailyFileSizeBytes;
    private readonly Func<DateTime> _clock;
    private readonly object _sync = new();

    private DateOnly _currentDate;
    private FileStream? _stream;
    private StreamWriter? _writer;
    private long _bytesWrittenToday;
    private bool _capReached;
    private bool _disposed;

    /// <param name="clock">
    /// Optional local-time provider, defaulting to <see cref="DateTime.Now"/>. Exists solely so unit tests
    /// can simulate date rollover deterministically; production callers should not need to pass this.
    /// </param>
    public DailyFileWriter(string directory, string feature, long maxDailyFileSizeBytes = DefaultMaxDailyFileSizeBytes, Func<DateTime>? clock = null)
    {
        _directory = directory;
        _feature = feature;
        _maxDailyFileSizeBytes = maxDailyFileSizeBytes;
        _clock = clock ?? (() => DateTime.Now);
        Directory.CreateDirectory(_directory);
    }

    /// <summary>Buffers the write; callers are expected to have already decided whether an immediate flush is required.</summary>
    public Task AppendAsync(LogEntry entry)
    {
        lock (_sync)
        {
            AppendCore(entry, flushImmediately: entry.Level >= LogLevel.Warning);
        }

        return Task.CompletedTask;
    }

    /// <summary>Synchronous write + flush, used by the crash-safe emergency path (bypasses the channel entirely).</summary>
    public void WriteAndFlush(LogEntry entry)
    {
        lock (_sync)
        {
            AppendCore(entry, flushImmediately: true);
        }
    }

    /// <summary>Flushes any buffered (Information/Debug/Trace) writes for the currently open day-file, if any.</summary>
    public void Flush()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            FlushStream();
        }
    }

    private void AppendCore(LogEntry entry, bool flushImmediately)
    {
        if (_disposed)
            return;

        EnsureCurrentDayStream();

        if (_capReached)
            return;

        var line = SerializeLine(entry);
        var lineByteCount = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;

        if (_bytesWrittenToday + lineByteCount > _maxDailyFileSizeBytes)
        {
            _capReached = true;
            WriteLine(SerializeLine(BuildCapNotice()));
            FlushStream();
            return;
        }

        WriteLine(line);
        _bytesWrittenToday += lineByteCount;

        if (flushImmediately)
        {
            FlushStream();
        }
    }

    private LogEntry BuildCapNotice() => new(
        Timestamp: DateTimeOffset.Now,
        Level: LogLevel.Warning,
        Category: nameof(DailyFileWriter),
        Feature: _feature,
        EventId: 0,
        Message: $"daily size cap reached ({_maxDailyFileSizeBytes} bytes), further {_feature} entries suppressed until next day");

    private void EnsureCurrentDayStream()
    {
        var today = DateOnly.FromDateTime(_clock());
        if (_writer is not null && today == _currentDate)
            return;

        CloseCurrentStream();

        _currentDate = today;
        _capReached = false;

        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{_feature}-{today:yyyy-MM-dd}.log");

        // FileShare.ReadWrite: no cross-process locking is required for this app (single-instance,
        // see docs/features/active/structured-file-logging/decisions.md D5); this also lets callers
        // (e.g. "Open logs folder", tests) read the file concurrently while it's still being written.
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _bytesWrittenToday = stream.Length;
        _stream = stream;
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = false };
    }

    private void WriteLine(string line)
    {
        _writer?.Write(line);
        _writer?.Write(Environment.NewLine);
    }

    private void FlushStream()
    {
        _writer?.Flush();
        _stream?.Flush(flushToDisk: true);
    }

    private void CloseCurrentStream()
    {
        if (_writer is not null)
        {
            _writer.Flush();
            _writer.Dispose();
            _writer = null;
        }

        _stream?.Dispose();
        _stream = null;
    }

    private static string SerializeLine(LogEntry entry)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("ts", entry.Timestamp);
            writer.WriteString("level", entry.Level.ToString());
            writer.WriteString("category", entry.Category);
            writer.WriteString("feature", entry.Feature);
            writer.WriteNumber("eventId", entry.EventId);
            writer.WriteString("message", entry.Message);

            if (!string.IsNullOrEmpty(entry.Exception))
            {
                writer.WriteString("exception", entry.Exception);
            }

            if (entry.ScopeState is { Count: > 0 })
            {
                writer.WriteStartObject("scopeState");
                foreach (var kvp in entry.ScopeState)
                {
                    writer.WriteString(kvp.Key, kvp.Value);
                }
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            CloseCurrentStream();
        }
    }
}
