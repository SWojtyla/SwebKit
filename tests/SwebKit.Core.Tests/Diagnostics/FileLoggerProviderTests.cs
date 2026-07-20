using Microsoft.Extensions.Logging;
using SwebKit.Core.Diagnostics;

namespace SwebKit.Core.Tests.Diagnostics;

/// <summary>Covers both <see cref="FileLoggerProvider"/> and the internal <c>FileLogger</c> it creates.</summary>
public class FileLoggerProviderTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly LoggingSettings _settings = new() { MinimumLevel = LogLevel.Information };

    public FileLoggerProviderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "SwebKit.Tests.Diagnostics.Provider", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private FileLoggerProvider CreateProvider() => new(() => _settings, _tempDirectory);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > timeout)
                throw new TimeoutException("Condition was not met within the timeout.");

            await Task.Delay(25);
        }
    }

    /// <summary>
    /// Reads a file's text while tolerating a concurrently-open writer handle (the DailyFileWriter
    /// keeps its FileStream open for the lifetime of the day, unlike File.ReadAllText's default share mode).
    /// </summary>
    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void IsEnabled_LoggingDisabled_ReturnsFalseForAllLevels()
    {
        _settings.Enabled = false;
        using var provider = CreateProvider();
        var logger = provider.CreateLogger("Test.Category");

        Assert.False(logger.IsEnabled(LogLevel.Trace));
        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Critical));
    }

    [Fact]
    public void IsEnabled_BelowMinimumLevel_ReturnsFalseEntryNotQueued()
    {
        _settings.MinimumLevel = LogLevel.Warning;
        using var provider = CreateProvider();
        var logger = provider.CreateLogger("Test.Category");

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    [Fact]
    public async Task Log_AtOrAboveMinimumLevel_EntryQueuedAndAppendedToCorrectFile()
    {
        using var provider = CreateProvider();
        var logger = provider.CreateLogger("SwebKit.Redis.RedisClient");

        logger.LogInformation("connected to redis");

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_tempDirectory, $"redis-{today:yyyy-MM-dd}.log");

        await WaitUntilAsync(() => File.Exists(path) && ReadAllTextShared(path).Contains("connected to redis"));

        var content = ReadAllTextShared(path);
        Assert.Contains("connected to redis", content);
        Assert.Contains("\"feature\":\"redis\"", content);
    }

    [Fact]
    public void Log_ChannelAtCapacity_OldestDroppedCallerDoesNotBlockOrThrow()
    {
        using var provider = CreateProvider();
        var logger = provider.CreateLogger("Test.Category");

        var exception = Record.Exception(() =>
        {
            for (var i = 0; i < 5000; i++)
            {
                logger.LogInformation("burst {Index}", i);
            }
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task Log_WithScopeValues_FlattenedIntoScopeStateAndRedacted()
    {
        using var provider = CreateProvider();
        var logger = provider.CreateLogger("Test.Category");

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["Namespace"] = "contoso-sb",
            ["Token"] = "super-secret-token-value"
        }))
        {
            logger.LogInformation("scoped message");
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_tempDirectory, $"general-{today:yyyy-MM-dd}.log");

        await WaitUntilAsync(() => File.Exists(path) && ReadAllTextShared(path).Contains("scoped message"));

        var content = ReadAllTextShared(path);
        Assert.Contains("contoso-sb", content);
        Assert.Contains("***REDACTED***", content);
        Assert.DoesNotContain("super-secret-token-value", content);
    }

    [Fact]
    public async Task Dispose_WithPendingEntries_DrainsWithinTimeoutAndFlushesToDisk()
    {
        var provider = CreateProvider();
        var logger = provider.CreateLogger("Test.Category");

        for (var i = 0; i < 50; i++)
        {
            logger.LogInformation("pending {Index}", i);
        }

        provider.Dispose();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_tempDirectory, $"general-{today:yyyy-MM-dd}.log");

        Assert.True(File.Exists(path));
        var lines = await File.ReadAllLinesAsync(path);
        Assert.NotEmpty(lines);
    }

    [Fact]
    public async Task LogError_WithException_SerializedExceptionPresentInJsonLineRedacted()
    {
        using var provider = CreateProvider();
        var logger = provider.CreateLogger("Test.Category");

        Exception thrown;
        try
        {
            throw new InvalidOperationException("failed with SharedAccessKey=verySecretValue1234567890");
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        logger.LogError(thrown, "operation failed");

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_tempDirectory, $"general-{today:yyyy-MM-dd}.log");

        await WaitUntilAsync(() => File.Exists(path) && ReadAllTextShared(path).Contains("exception"));

        var content = ReadAllTextShared(path);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("SharedAccessKey=***REDACTED***", content);
        Assert.DoesNotContain("verySecretValue1234567890", content);
    }
}
