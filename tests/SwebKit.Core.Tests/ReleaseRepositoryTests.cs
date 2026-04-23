using SwebKit.Core.Configuration;
using SwebKit.Core.Models;

namespace SwebKit.Core.Tests;

public sealed class ReleaseRepositoryTests
{
    [Fact]
    public async Task SaveAsync_CreatesBackupFile()
    {
        using var _ = new AppDataSandbox();
        var repository = new ReleaseRepository();

        await repository.AddReleaseAsync(CreateRelease("Sprint 42"));

        Assert.True(File.Exists(AppDataPaths.ReleasesJson));
        Assert.True(File.Exists($"{AppDataPaths.ReleasesJson}.bak"));
    }

    [Fact]
    public async Task LoadAsync_WithCorruptedPrimaryAndBackup_RecoversReleasesAndSnapshots()
    {
        using var _ = new AppDataSandbox();
        var release = CreateRelease("Sprint 42");
        var snapshot = new DeploymentSnapshot
        {
            ReleaseId = release.Id,
            ComponentName = "api",
            Environment = "prod",
            DeployedTag = "2026.04.14.1",
            ApprovedBy = "operator",
        };

        var writer = new ReleaseRepository();
        await writer.AddReleaseAsync(release);
        await writer.AddSnapshotsAsync([snapshot]);

        var backupPath = $"{AppDataPaths.ReleasesJson}.bak";
        Assert.True(File.Exists(backupPath));

        await File.WriteAllTextAsync(AppDataPaths.ReleasesJson, "{ invalid json");

        var reader = new ReleaseRepository();
        await reader.LoadAsync();

        var restoredRelease = Assert.Single(reader.AllReleases);
        Assert.Equal(release.Id, restoredRelease.Id);
        Assert.Equal("Sprint 42", restoredRelease.Name);

        var restoredSnapshot = Assert.Single(reader.GetSnapshots(release.Id));
        Assert.Equal("api", restoredSnapshot.ComponentName);
        Assert.Equal("2026.04.14.1", restoredSnapshot.DeployedTag);
    }

    private static ReleaseRecord CreateRelease(string name) =>
        new()
        {
            Name = name,
            Status = ReleaseStatus.InProgress,
            Components =
            [
                new ComponentScope
                {
                    ComponentName = "api",
                    ProjectName = "SwebKit.App",
                    RepositoryId = "repo-1",
                    PipelineId = 42,
                }
            ]
        };
}