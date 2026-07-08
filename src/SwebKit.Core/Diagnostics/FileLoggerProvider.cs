using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SwebKit.Core.Diagnostics;

/// <summary>
/// Custom, dependency-free <see cref="ILoggerProvider"/> that fans out <see cref="ILogger"/> calls to
/// per-feature-bucket <see cref="DailyFileWriter"/> instances via a bounded, non-blocking channel.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int ChannelCapacity = 2000;
    private static readonly TimeSpan DrainShutdownTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PeriodicFlushInterval = TimeSpan.FromMilliseconds(250);

    private readonly Func<LoggingSettings> _settingsAccessor;
    private readonly string _logsDirectory;
    private readonly long _maxDailyFileSizeBytes;
    private readonly Channel<LogEntry> _channel;
    private readonly ConcurrentDictionary<string, DailyFileWriter> _writers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Task _drainTask;
    private readonly Timer _flushTimer;
    private bool _disposed;

    public FileLoggerProvider(Func<LoggingSettings> settingsAccessor, string logsDirectory, long maxDailyFileSizeBytes = DailyFileWriter.DefaultMaxDailyFileSizeBytes)
    {
        _settingsAccessor = settingsAccessor;
        _logsDirectory = logsDirectory;
        _maxDailyFileSizeBytes = maxDailyFileSizeBytes;

        Directory.CreateDirectory(_logsDirectory);

        _channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        _drainTask = Task.Run(DrainAsync);
        _flushTimer = new Timer(_ => FlushAllWriters(), null, PeriodicFlushInterval, PeriodicFlushInterval);
    }

    internal LoggingSettings Settings => _settingsAccessor();

    public ILogger CreateLogger(string categoryName)
    {
        var feature = LogFeatureBucketResolver.Resolve(categoryName);
        return new FileLogger(this, categoryName, feature);
    }

    /// <summary>Queues an entry for async, batched persistence. Never blocks or throws.</summary>
    internal bool TryEnqueue(LogEntry entry) => _channel.Writer.TryWrite(entry);

    /// <summary>
    /// Crash-safe path: bypasses the channel and background drain task entirely, writing directly
    /// and synchronously to the resolved feature's day-file. Public so the global crash handlers
    /// registered in <c>MauiProgram.cs</c> (a different assembly) can call it directly.
    /// </summary>
    public void EmergencyWriteAndFlush(LogEntry entry)
    {
        try
        {
            var writer = GetOrCreateWriter(entry.Feature);
            writer.WriteAndFlush(entry);
        }
        catch
        {
            // Best-effort by design: an emergency handler must never itself throw.
        }
    }

    private async Task DrainAsync()
    {
        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    var writer = GetOrCreateWriter(entry.Feature);
                    await writer.AppendAsync(entry).ConfigureAwait(false);
                }
                catch
                {
                    // A single bad entry must never stop the drain loop from processing the rest.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
    }

    private DailyFileWriter GetOrCreateWriter(string feature) =>
        _writers.GetOrAdd(feature, f => new DailyFileWriter(_logsDirectory, f, _maxDailyFileSizeBytes));

    /// <summary>
    /// Periodically flushes buffered Information/Debug/Trace writes so entries are eventually durable
    /// without waiting for a Warning+ entry or provider shutdown (backend.md's "time-based" batch flush).
    /// </summary>
    private void FlushAllWriters()
    {
        foreach (var writer in _writers.Values)
        {
            try
            {
                writer.Flush();
            }
            catch
            {
                // A single writer failing to flush must never stop the timer from ticking again.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _flushTimer.Dispose();
        _channel.Writer.TryComplete();

        try
        {
            _drainTask.Wait(DrainShutdownTimeout);
        }
        catch (AggregateException)
        {
            // Swallow: shutdown must never throw regardless of how the drain task ended.
        }

        foreach (var writer in _writers.Values)
        {
            writer.Dispose();
        }
    }
}
