using Microsoft.Extensions.Logging;
using SwebKit.Core.Diagnostics;

namespace SwebKit.Core.Tests.Diagnostics;

/// <summary>
/// Covers the crash-safe emergency write path (<see cref="FileLoggerProvider.EmergencyWriteAndFlush"/>)
/// described in docs/features/active/structured-file-logging/decisions.md D10 — test-plan.md IDs 40-43.
/// </summary>
public class CrashSafetyTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly LoggingSettings _settings = new();

    public CrashSafetyTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "SwebKit.Tests.Diagnostics.CrashSafety", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private FileLoggerProvider CreateProvider() => new(() => _settings, _tempDirectory);

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // #40 — EmergencyWriteAndFlush bypasses the channel and lands on disk synchronously.
    [Fact]
    public void EmergencyWriteAndFlush_CrashEntry_WritesSynchronouslyWithoutChannel()
    {
        using var provider = CreateProvider();
        var entry = LogEntry.ForCrash(new InvalidOperationException("boom"), isTerminating: true);

        provider.EmergencyWriteAndFlush(entry);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_tempDirectory, $"general-{today:yyyy-MM-dd}.log");

        Assert.True(File.Exists(path));
        var content = ReadAllTextShared(path);
        Assert.Contains("\"level\":\"Critical\"", content);
        Assert.Contains("Unhandled exception", content);
        Assert.Contains("InvalidOperationException", content);
    }

    // #40 — even without a prior Dispose()/flush, the entry is durable immediately (no channel round-trip).
    [Fact]
    public void EmergencyWriteAndFlush_DoesNotRequireProviderDisposeToPersist()
    {
        using var provider = CreateProvider();
        var entry = LogEntry.ForCrash(new InvalidOperationException("no dispose needed"), isTerminating: false);

        provider.EmergencyWriteAndFlush(entry);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_tempDirectory, $"general-{today:yyyy-MM-dd}.log");
        var content = ReadAllTextShared(path);

        Assert.Contains("no dispose needed", content.Replace("\\u0027", "'"));
    }

    // #41 — mirrors the AppDomain.CurrentDomain.UnhandledException handler registered in MauiProgram.cs.
    [Fact]
    public void UnhandledExceptionHandler_BuildsCriticalCrashEntry_AndEmergencyWrites()
    {
        using var provider = CreateProvider();
        Exception? capturedException = new InvalidOperationException("unhandled");

        void Handler(object? _, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            provider.EmergencyWriteAndFlush(LogEntry.ForCrash(ex, e.IsTerminating));
        }

        // Simulate the handler body directly (raising a real AppDomain.UnhandledException would
        // terminate the test process) using the same UnhandledExceptionEventArgs shape MauiProgram.cs consumes.
        var args = new UnhandledExceptionEventArgs(capturedException, isTerminating: true);
        Handler(null, args);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_tempDirectory, $"general-{today:yyyy-MM-dd}.log");
        var content = ReadAllTextShared(path);

        Assert.Contains("\"level\":\"Critical\"", content);
        Assert.Contains("application is terminating", content);
        Assert.Contains("InvalidOperationException", content);
    }

    // #42 — mirrors the TaskScheduler.UnobservedTaskException handler registered in MauiProgram.cs.
    [Fact]
    public async Task UnobservedTaskExceptionHandler_EmergencyWritesAndObservesException()
    {
        using var provider = CreateProvider();

        void Handler(object? _, UnobservedTaskExceptionEventArgs e)
        {
            provider.EmergencyWriteAndFlush(LogEntry.ForCrash(e.Exception, isTerminating: false));
            e.SetObserved();
        }

        var faultedTask = Task.Run(() => throw new InvalidOperationException("unobserved"));
        try { await faultedTask; } catch { /* observed locally so it doesn't also crash xUnit's own handler */ }

        var aggregate = new AggregateException(new InvalidOperationException("unobserved"));
        var args = new UnobservedTaskExceptionEventArgs(aggregate);

        Handler(null, args);

        Assert.True(args.Observed);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_tempDirectory, $"general-{today:yyyy-MM-dd}.log");
        var content = ReadAllTextShared(path);

        Assert.Contains("Unobserved task exception", content);
    }

    // #43 — emergency path resolves/creates its own DailyFileWriter directly; it never awaits or
    // depends on the channel's background drain Task, so it still completes even if that task is stalled.
    [Fact]
    public void EmergencyWriteAndFlush_CompletesEvenWhenBackgroundDrainNeverRuns()
    {
        using var provider = CreateProvider();

        // Flood the bounded channel via normal logging without ever giving the drain task a chance
        // to run in this synchronous test method (no await/yield before the emergency call below).
        var logger = provider.CreateLogger("SwebKit.App.Services.SomeService");
        for (var i = 0; i < 50; i++)
        {
            logger.LogInformation("filler entry {Index}", i);
        }

        var crashEntry = LogEntry.ForCrash(new InvalidOperationException("stalled drain"), isTerminating: true);
        provider.EmergencyWriteAndFlush(crashEntry);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_tempDirectory, $"general-{today:yyyy-MM-dd}.log");

        Assert.True(File.Exists(path));
        var content = ReadAllTextShared(path);
        Assert.Contains("stalled drain", content);
    }

    // #40 — LogEntry.ForCrash itself must always redact the exception it serializes (no bypass of D3).
    [Fact]
    public void ForCrash_RedactsSecretsEmbeddedInExceptionMessage()
    {
        using var provider = CreateProvider();
        var exception = new InvalidOperationException(
            "Connection failed: Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=superSecretValue123");

        provider.EmergencyWriteAndFlush(LogEntry.ForCrash(exception, isTerminating: true));

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_tempDirectory, $"general-{today:yyyy-MM-dd}.log");
        var content = ReadAllTextShared(path);

        Assert.DoesNotContain("superSecretValue123", content);
        Assert.Contains("REDACTED", content);
    }
}
