using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class DeploymentAnchorTests
{
    [Fact]
    public async Task GetDeploymentAnchors_ReleaseWithNoSnapshots_ReturnsEmpty()
    {
        using var _ = new AppDataSandbox();
        var repo = new ReleaseRepository();
        await repo.AddReleaseAsync(new ReleaseRecord { Name = "v1.0", Status = ReleaseStatus.Draft });

        var result = ObservabilityExplainerService.GetDeploymentAnchors(repo);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDeploymentAnchors_MultipleReleasesWithSnapshots_OrderedDescendingByAnchorTime()
    {
        using var _ = new AppDataSandbox();
        var repo = new ReleaseRepository();
        var r1 = new ReleaseRecord { Name = "v1.0", Status = ReleaseStatus.Completed };
        var r2 = new ReleaseRecord { Name = "v2.0", Status = ReleaseStatus.Completed };
        await repo.AddReleaseAsync(r1);
        await repo.AddReleaseAsync(r2);
        await repo.AddSnapshotsAsync([
            new DeploymentSnapshot
            {
                ReleaseId = r1.Id,
                ComponentName = "api",
                Environment = "prod",
                DeployedAt = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero),
            },
            new DeploymentSnapshot
            {
                ReleaseId = r2.Id,
                ComponentName = "api",
                Environment = "prod",
                DeployedAt = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
            },
        ]);

        var result = ObservabilityExplainerService.GetDeploymentAnchors(repo);

        Assert.Equal(2, result.Count);
        Assert.Equal(r2.Id, result[0].ReleaseId);
        Assert.Equal(r1.Id, result[1].ReleaseId);
        Assert.True(result[0].AnchorTime > result[1].AnchorTime);
    }

    [Fact]
    public async Task GetDeploymentAnchors_OneReleaseWithSnapshotOneWithout_SkipsReleaseWithNoSnapshot()
    {
        using var _ = new AppDataSandbox();
        var repo = new ReleaseRepository();
        var r1 = new ReleaseRecord { Name = "v1.0", Status = ReleaseStatus.Completed };
        var r2 = new ReleaseRecord { Name = "v2.0", Status = ReleaseStatus.Draft };
        await repo.AddReleaseAsync(r1);
        await repo.AddReleaseAsync(r2);
        await repo.AddSnapshotsAsync([
            new DeploymentSnapshot
            {
                ReleaseId = r1.Id,
                ComponentName = "api",
                Environment = "prod",
                DeployedAt = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero),
            },
        ]);

        var result = ObservabilityExplainerService.GetDeploymentAnchors(repo);

        var anchor = Assert.Single(result);
        Assert.Equal(r1.Id, anchor.ReleaseId);
        Assert.Equal("v1.0", anchor.ReleaseName);
    }
}
