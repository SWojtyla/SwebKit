using SwebKit.Core.Configuration;
using SwebKit.Core.Models;

namespace SwebKit.Core.Tests;

public class AlertRuleRepositoryTests
{
    // ── GetAllAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsEmpty_WhenFileDoesNotExist()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();

        var rules = await repo.GetAllAsync();

        Assert.Empty(rules);
    }

    [Fact]
    public async Task SaveAllAsync_Then_GetAllAsync_RoundTrips()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();

        var rule = new MonitoringAlertRule
        {
            Name = "Prod pod health",
            Source = AlertRuleSource.AksPodHealth,
            Severity = AlertSeverity.Warning,
            IntervalSeconds = 30,
            CooldownMinutes = 10,
            AksPodParams = new AksPodAlertParams { Namespace = "production" },
        };

        await repo.SaveAllAsync([rule]);
        var loaded = await repo.GetAllAsync();

        Assert.Single(loaded);
        Assert.Equal(rule.Id, loaded[0].Id);
        Assert.Equal("Prod pod health", loaded[0].Name);
        Assert.Equal(AlertRuleSource.AksPodHealth, loaded[0].Source);
        Assert.Equal(AlertSeverity.Warning, loaded[0].Severity);
        Assert.Equal(30, loaded[0].IntervalSeconds);
        Assert.Equal("production", loaded[0].AksPodParams?.Namespace);
    }

    [Fact]
    public async Task SaveAllAsync_OverwritesPreviousContent()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();

        await repo.SaveAllAsync([new MonitoringAlertRule { Name = "Old" }]);
        await repo.SaveAllAsync([
            new MonitoringAlertRule { Name = "New A" },
            new MonitoringAlertRule { Name = "New B" },
        ]);

        var loaded = await repo.GetAllAsync();
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, r => r.Name == "New A");
        Assert.Contains(loaded, r => r.Name == "New B");
    }

    // ── UpsertAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertAsync_AddsNewRule_WhenIdNotPresent()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();

        var rule = new MonitoringAlertRule { Name = "DLQ alert" };
        await repo.UpsertAsync(rule);

        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("DLQ alert", all[0].Name);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingRule_WhenIdMatches()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();

        var rule = new MonitoringAlertRule { Name = "Original" };
        await repo.UpsertAsync(rule);

        rule.Name = "Updated";
        await repo.UpsertAsync(rule);

        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("Updated", all[0].Name);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesRule_WhenIdExists()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();

        var a = new MonitoringAlertRule { Name = "A" };
        var b = new MonitoringAlertRule { Name = "B" };
        await repo.SaveAllAsync([a, b]);

        await repo.DeleteAsync(a.Id);

        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("B", all[0].Name);
    }

    [Fact]
    public async Task DeleteAsync_IsIdempotent_WhenIdNotFound()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();

        await repo.SaveAllAsync([new MonitoringAlertRule { Name = "A" }]);

        // Should not throw
        await repo.DeleteAsync("nonexistent-id");

        var all = await repo.GetAllAsync();
        Assert.Single(all);
    }

    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ReturnsRule_WhenIdExists()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();

        var rule = new MonitoringAlertRule { Name = "Target" };
        await repo.SaveAllAsync([rule, new MonitoringAlertRule { Name = "Other" }]);

        var found = await repo.GetByIdAsync(rule.Id);

        Assert.NotNull(found);
        Assert.Equal("Target", found.Name);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenIdNotFound()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();

        var found = await repo.GetByIdAsync("missing");

        Assert.Null(found);
    }

    // ── Enum serialization ────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAllAsync_SerializesEnumsAsStrings_RoundTrip()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();

        var rule = new MonitoringAlertRule
        {
            Source = AlertRuleSource.ServiceBusDlqDepth,
            Severity = AlertSeverity.Critical,
        };
        await repo.UpsertAsync(rule);

        var json = await File.ReadAllTextAsync(AppDataPaths.MonitoringAlertsJson);
        Assert.Contains("ServiceBusDlqDepth", json);
        Assert.Contains("Critical", json);

        var loaded = await repo.GetAllAsync();
        Assert.Equal(AlertRuleSource.ServiceBusDlqDepth, loaded[0].Source);
        Assert.Equal(AlertSeverity.Critical, loaded[0].Severity);
    }

    // ── Multiple param bags ───────────────────────────────────────────────────

    [Fact]
    public async Task SaveAllAsync_PreservesAllParamBagTypes()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();

        var rules = new List<MonitoringAlertRule>
        {
            new() { Source = AlertRuleSource.AksPodHealth, AksPodParams = new() { Namespace = "dev", RestartThreshold = 3 } },
            new() { Source = AlertRuleSource.ServiceBusDlqDepth, ServiceBusParams = new() { NamespaceConnectionAlias = "orders", EntityPath = "payments", MessageCountThreshold = 5 } },
            new() { Source = AlertRuleSource.RedisMemoryUsage, RedisAlertParams = new() { ConnectionAlias = "cache-1", MemoryUsageThresholdPercent = 90.0 } },
            new() { Source = AlertRuleSource.StorageBlobCount, StorageParams = new() { AccountAlias = "sa1", ContainerName = "uploads", BlobCountThreshold = 500 } },
        };

        await repo.SaveAllAsync(rules);
        var loaded = await repo.GetAllAsync();

        Assert.Equal(4, loaded.Count);
        Assert.Equal("dev", loaded[0].AksPodParams?.Namespace);
        Assert.Equal(3, loaded[0].AksPodParams?.RestartThreshold);
        Assert.Equal("payments", loaded[1].ServiceBusParams?.EntityPath);
        Assert.Equal(5, loaded[1].ServiceBusParams?.MessageCountThreshold);
        Assert.Equal(90.0, loaded[2].RedisAlertParams?.MemoryUsageThresholdPercent);
        Assert.Equal(500, loaded[3].StorageParams?.BlobCountThreshold);
    }
}
