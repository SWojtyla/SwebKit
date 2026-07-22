using SwebKit.Core.Diagnostics;

namespace SwebKit.Core.Tests.Diagnostics;

public class LogRetentionCleanupServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public LogRetentionCleanupServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "SwebKit.Tests.Diagnostics.Retention", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string CreateFile(string feature, DateOnly date)
    {
        var path = Path.Combine(_tempDirectory, $"{feature}-{date:yyyy-MM-dd}.log");
        File.WriteAllText(path, "{}\n");
        return path;
    }

    [Fact]
    public async Task RunAsync_FileOlderThanMaxAgeDays_IsDeleted()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var oldFile = CreateFile("general", today.AddDays(-8));
        var service = new LogRetentionCleanupService(_tempDirectory, maxAgeDays: 7);

        await service.RunAsync();

        Assert.False(File.Exists(oldFile));
    }

    [Fact]
    public async Task RunAsync_FileExactlyAtMaxAgeDays_IsRetained()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var boundaryFile = CreateFile("general", today.AddDays(-7));
        var service = new LogRetentionCleanupService(_tempDirectory, maxAgeDays: 7);

        await service.RunAsync();

        Assert.True(File.Exists(boundaryFile));
    }

    [Fact]
    public async Task RunAsync_FileDatedToday_NeverDeletedAcrossMultipleRuns()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var todayFile = CreateFile("general", today);
        var service = new LogRetentionCleanupService(_tempDirectory, maxAgeDays: 7);

        await service.RunAsync();
        await service.RunAsync();
        await service.RunAsync();

        Assert.True(File.Exists(todayFile));
    }

    [Fact]
    public async Task RunAsync_FilenameDoesNotMatchExpectedPattern_IsSkipped()
    {
        var malformedPath = Path.Combine(_tempDirectory, "not-a-dated-log-file.log");
        File.WriteAllText(malformedPath, "{}\n");
        var service = new LogRetentionCleanupService(_tempDirectory, maxAgeDays: 7);

        await service.RunAsync();

        Assert.True(File.Exists(malformedPath));
    }

    [Fact]
    public async Task RunAsync_MissingLogsDirectory_IsNoOpDoesNotThrow()
    {
        var missingDirectory = Path.Combine(_tempDirectory, "does-not-exist");
        var service = new LogRetentionCleanupService(missingDirectory, maxAgeDays: 7);

        var exception = await Record.ExceptionAsync(() => service.RunAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task RunAsync_RunTwiceInARow_SecondRunIsNoOpBeyondFirst()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var oldFile = CreateFile("general", today.AddDays(-10));
        var service = new LogRetentionCleanupService(_tempDirectory, maxAgeDays: 7);

        await service.RunAsync();
        Assert.False(File.Exists(oldFile));

        var exception = await Record.ExceptionAsync(() => service.RunAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task RunAsync_OneFileLockedDuringDeletion_RemainingFilesStillProcessed()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var lockedFile = CreateFile("service-bus", today.AddDays(-30));
        var otherOldFile = CreateFile("general", today.AddDays(-30));
        var service = new LogRetentionCleanupService(_tempDirectory, maxAgeDays: 7);

        using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var exception = await Record.ExceptionAsync(() => service.RunAsync());
            Assert.Null(exception);
        }

        Assert.False(File.Exists(otherOldFile));
    }
}
