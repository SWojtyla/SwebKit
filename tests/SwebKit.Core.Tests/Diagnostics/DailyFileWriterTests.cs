using Microsoft.Extensions.Logging;
using SwebKit.Core.Diagnostics;

namespace SwebKit.Core.Tests.Diagnostics;

public class DailyFileWriterTests : IDisposable
{
    private readonly string _tempDirectory;

    public DailyFileWriterTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "SwebKit.Tests.Diagnostics", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private static LogEntry MakeEntry(string feature, LogLevel level = LogLevel.Information, string message = "hello") =>
        new(DateTimeOffset.Now, level, "Test.Category", feature, 0, message);

    [Fact]
    public async Task AppendAsync_MultipleEntriesSameDay_AllLandInTodaysFile()
    {
        using var writer = new DailyFileWriter(_tempDirectory, "general");

        await writer.AppendAsync(MakeEntry("general", message: "first"));
        await writer.AppendAsync(MakeEntry("general", message: "second"));
        writer.Dispose();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_tempDirectory, $"general-{today:yyyy-MM-dd}.log");
        var lines = File.ReadAllLines(path);

        Assert.Equal(2, lines.Length);
        Assert.Contains("first", lines[0]);
        Assert.Contains("second", lines[1]);
    }

    [Fact]
    public async Task AppendAsync_AfterLocalDateChanges_NewEntriesLandInNewDateFile_PreviousUntouched()
    {
        var currentTime = new DateTime(2026, 7, 8, 10, 0, 0);
        using var writer = new DailyFileWriter(_tempDirectory, "general", clock: () => currentTime);

        await writer.AppendAsync(MakeEntry("general", message: "day-one"));

        currentTime = new DateTime(2026, 7, 9, 0, 5, 0);
        await writer.AppendAsync(MakeEntry("general", message: "day-two"));
        writer.Dispose();

        var day1Path = Path.Combine(_tempDirectory, "general-2026-07-08.log");
        var day2Path = Path.Combine(_tempDirectory, "general-2026-07-09.log");

        Assert.True(File.Exists(day1Path));
        Assert.True(File.Exists(day2Path));

        var day1Lines = File.ReadAllLines(day1Path);
        var day2Lines = File.ReadAllLines(day2Path);

        Assert.Single(day1Lines);
        Assert.Contains("day-one", day1Lines[0]);
        Assert.Single(day2Lines);
        Assert.Contains("day-two", day2Lines[0]);
    }

    [Fact]
    public async Task AppendAsync_ExceedsMaxDailyFileSize_SuppressesFurtherEntriesAndWritesOneTimeCapLine()
    {
        using var writer = new DailyFileWriter(_tempDirectory, "general", maxDailyFileSizeBytes: 200);

        for (var i = 0; i < 20; i++)
        {
            await writer.AppendAsync(MakeEntry("general", message: $"entry-{i}-padding-to-grow-the-line-size"));
        }
        writer.Dispose();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_tempDirectory, $"general-{today:yyyy-MM-dd}.log");
        var lines = File.ReadAllLines(path);

        var capLines = lines.Count(l => l.Contains("daily size cap reached"));
        Assert.Equal(1, capLines);
        Assert.True(lines.Length < 20 + 1);
    }

    [Fact]
    public void Constructor_FreshDirectory_CreatesDirectoryWithoutThrowing()
    {
        Assert.False(Directory.Exists(_tempDirectory));

        using var writer = new DailyFileWriter(_tempDirectory, "general");

        Assert.True(Directory.Exists(_tempDirectory));
    }

    [Fact]
    public async Task AppendAsync_ConcurrentCalls_NoCorruptionAllEntriesPersisted()
    {
        using var writer = new DailyFileWriter(_tempDirectory, "general");
        const int entryCount = 100;

        var tasks = Enumerable.Range(0, entryCount)
            .Select(i => writer.AppendAsync(MakeEntry("general", message: $"concurrent-{i}")));

        await Task.WhenAll(tasks);
        writer.Dispose();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_tempDirectory, $"general-{today:yyyy-MM-dd}.log");
        var lines = File.ReadAllLines(path);

        Assert.Equal(entryCount, lines.Length);
        Assert.All(lines, line => Assert.True(line.TrimStart().StartsWith('{') && line.TrimEnd().EndsWith('}')));
    }

    [Fact]
    public async Task AppendAsync_WarningLevel_FlushesSynchronouslyImmediately()
    {
        using var writer = new DailyFileWriter(_tempDirectory, "general");

        await writer.AppendAsync(MakeEntry("general", LogLevel.Warning, "warn-now"));

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_tempDirectory, $"general-{today:yyyy-MM-dd}.log");

        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        var content = await reader.ReadToEndAsync();

        Assert.Contains("warn-now", content);
    }

    [Fact]
    public async Task AppendAsync_InformationLevel_IsBufferedNotFlushedImmediately()
    {
        using var writer = new DailyFileWriter(_tempDirectory, "general");

        await writer.AppendAsync(MakeEntry("general", LogLevel.Information, "info-buffered"));

        var today = DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(_tempDirectory, $"general-{today:yyyy-MM-dd}.log");

        using (var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
        {
            var contentBeforeFlush = await reader.ReadToEndAsync();
            Assert.DoesNotContain("info-buffered", contentBeforeFlush);
        }

        writer.Dispose();

        var contentAfterDispose = await File.ReadAllTextAsync(path);
        Assert.Contains("info-buffered", contentAfterDispose);
    }
}
