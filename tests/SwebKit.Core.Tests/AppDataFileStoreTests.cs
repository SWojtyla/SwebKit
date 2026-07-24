using SwebKit.Core.Configuration;

namespace SwebKit.Core.Tests;

/// <summary>
/// Tests for <see cref="AppDataFileStore.PreserveUnreadableFile"/> — the safety net that stops a
/// repository's load-failure fallback from silently destroying the user's data on the next save.
/// </summary>
public sealed class AppDataFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "swebkit-appdatafilestore-tests", Guid.NewGuid().ToString("N"));

    public AppDataFileStoreTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void PreserveUnreadableFile_CopiesPrimaryFile_ToFixedSnapshotPath()
    {
        var filePath = Path.Combine(_root, "environments.json");
        File.WriteAllText(filePath, "{ \"old\": \"shape\" }");

        AppDataFileStore.PreserveUnreadableFile(filePath);

        var snapshotPath = AppDataFileStore.GetUnreadableSnapshotPath(filePath);
        Assert.True(File.Exists(snapshotPath));
        Assert.Equal("{ \"old\": \"shape\" }", File.ReadAllText(snapshotPath));
    }

    [Fact]
    public void PreserveUnreadableFile_AlsoCopiesBackupFile_WhenPresent()
    {
        var filePath = Path.Combine(_root, "environments.json");
        var backupPath = AppDataFileStore.GetBackupPath(filePath);
        File.WriteAllText(filePath, "primary content");
        File.WriteAllText(backupPath, "backup content");

        AppDataFileStore.PreserveUnreadableFile(filePath);

        Assert.Equal("primary content", File.ReadAllText(AppDataFileStore.GetUnreadableSnapshotPath(filePath)));
        Assert.Equal("backup content", File.ReadAllText(AppDataFileStore.GetUnreadableSnapshotPath(backupPath)));
    }

    [Fact]
    public void PreserveUnreadableFile_SurvivesSubsequentOverwriteOfTheOriginal()
    {
        // This is the core guarantee: even after the caller resets its store and re-saves an
        // empty one over the original file, the pre-failure content remains recoverable.
        var filePath = Path.Combine(_root, "collections.json");
        File.WriteAllText(filePath, "the only copy of the user's real data");

        AppDataFileStore.PreserveUnreadableFile(filePath);
        File.WriteAllText(filePath, "{}"); // simulates the repository's next SaveAsync()

        Assert.Equal("the only copy of the user's real data", File.ReadAllText(AppDataFileStore.GetUnreadableSnapshotPath(filePath)));
        Assert.Equal("{}", File.ReadAllText(filePath));
    }

    [Fact]
    public void PreserveUnreadableFile_DoesNotThrow_WhenNeitherFileExists()
    {
        var filePath = Path.Combine(_root, "does-not-exist.json");

        var exception = Record.Exception(() => AppDataFileStore.PreserveUnreadableFile(filePath));

        Assert.Null(exception);
        Assert.False(File.Exists(AppDataFileStore.GetUnreadableSnapshotPath(filePath)));
    }

    [Fact]
    public void PreserveUnreadableFile_OverwritesPriorSnapshot_RatherThanAccumulating()
    {
        var filePath = Path.Combine(_root, "environments.json");
        File.WriteAllText(filePath, "first failed attempt");
        AppDataFileStore.PreserveUnreadableFile(filePath);

        File.WriteAllText(filePath, "second failed attempt");
        AppDataFileStore.PreserveUnreadableFile(filePath);

        var snapshotPath = AppDataFileStore.GetUnreadableSnapshotPath(filePath);
        Assert.Equal("second failed attempt", File.ReadAllText(snapshotPath));
        Assert.Single(Directory.GetFiles(_root, "*.unreadable"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
