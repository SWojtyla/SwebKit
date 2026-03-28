using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class RedisKeyspaceHealthAnalyzerTests
{
    private readonly RedisKeyspaceHealthAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_WithRiskyKeys_ProducesExpectedFindings()
    {
        var report = _analyzer.Analyze(
        [
            new RedisKeyInfo
            {
                Key = "orders:active:1",
                Type = "string",
                Ttl = null,
                MemoryBytes = 320_000,
                Frequency = 45,
                IdleSeconds = 2,
            },
            new RedisKeyInfo
            {
                Key = "orders:active:2",
                Type = "hash",
                Ttl = TimeSpan.FromMinutes(5),
                MemoryBytes = 90_000,
                IdleSeconds = 8,
            },
            new RedisKeyInfo
            {
                Key = "cache:small",
                Type = "string",
                Ttl = TimeSpan.FromMinutes(1),
                MemoryBytes = 1_000,
            },
        ],
        estimatedKeyCount: 10,
        options: new RedisHealthScanOptions { Separator = ":" });

        Assert.True(report.CriticalCount > 0);
        Assert.True(report.WarningCount > 0);
        Assert.Contains(report.Findings, finding =>
            finding.RiskType == RedisHealthRiskType.NoTtl &&
            finding.Target == "orders:active:1");
        Assert.Contains(report.Findings, finding =>
            finding.RiskType == RedisHealthRiskType.OversizedValue &&
            finding.Target == "orders:active:1");
        Assert.Contains(report.Findings, finding =>
            finding.RiskType == RedisHealthRiskType.PossibleHotKey &&
            finding.Target == "orders:active:1");
        Assert.Contains(report.Findings, finding =>
            finding.RiskType == RedisHealthRiskType.HeavyPrefix &&
            finding.Target == "orders");
        Assert.Equal(3, report.LoadedKeyCount);
        Assert.Equal(10, report.EstimatedKeyCount);
        Assert.True(report.IsPartialCoverage);
        Assert.Equal("Low", report.ConfidenceLabel);
    }

    [Fact]
    public void Analyze_WithoutEstimate_UsesEstimatedConfidence()
    {
        var report = _analyzer.Analyze(
        [
            new RedisKeyInfo
            {
                Key = "k1",
                Type = "string",
                MemoryBytes = 100,
                Ttl = TimeSpan.FromSeconds(30),
            },
        ]);

        Assert.Equal(100, report.CoveragePercent);
        Assert.False(report.IsPartialCoverage);
        Assert.Equal("Estimated", report.ConfidenceLabel);
    }

    [Fact]
    public void Analyze_NoHotSignals_AddsSignalUnavailableFinding()
    {
        var report = _analyzer.Analyze(
        [
            new RedisKeyInfo
            {
                Key = "k1",
                Type = "string",
                MemoryBytes = 10_000,
                Ttl = null,
            },
        ],
        options: new RedisHealthScanOptions
        {
            IncludeSignalUnavailableFinding = true,
        });

        Assert.False(report.HotKeySignalsAvailable);
        Assert.Contains(report.Findings, finding =>
            finding.RiskType == RedisHealthRiskType.HotKeySignalUnavailable &&
            finding.Severity == RedisHealthSeverity.Info);
    }

    [Fact]
    public void Analyze_SignalUnavailableSuppressed_WhenDisabled()
    {
        var report = _analyzer.Analyze(
        [
            new RedisKeyInfo
            {
                Key = "k1",
                Type = "string",
                MemoryBytes = 100,
                Ttl = TimeSpan.FromMinutes(2),
            },
        ],
        options: new RedisHealthScanOptions
        {
            IncludeSignalUnavailableFinding = false,
        });

        Assert.DoesNotContain(report.Findings, finding =>
            finding.RiskType == RedisHealthRiskType.HotKeySignalUnavailable);
    }

    [Fact]
    public void Analyze_MaxFindingsCapsOutput()
    {
        var report = _analyzer.Analyze(
        [
            new RedisKeyInfo { Key = "a:1", Type = "string", Ttl = null, MemoryBytes = 400_000, Frequency = 50, IdleSeconds = 1 },
            new RedisKeyInfo { Key = "a:2", Type = "string", Ttl = null, MemoryBytes = 300_000, Frequency = 40, IdleSeconds = 2 },
            new RedisKeyInfo { Key = "a:3", Type = "string", Ttl = null, MemoryBytes = 250_000, Frequency = 35, IdleSeconds = 3 },
        ],
        options: new RedisHealthScanOptions
        {
            Separator = ":",
            MaxFindings = 2,
        });

        Assert.Equal(2, report.Findings.Count);
    }
}
