using System.Text.Json;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class IncidentSnapshotExporterTests
{
    private static readonly IncidentWorkloadScope DefaultScope = new(null, "my-ns", IncidentWorkloadKind.Deployment, "my-api");
    private static readonly TimeRange DefaultWindow =
        new(new DateTimeOffset(2025, 1, 10, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 1, 10, 9, 0, 0, TimeSpan.Zero));

    private static IncidentTimelinePage MakePage(
        IReadOnlyList<IncidentTimelineItem>? items = null,
        IReadOnlyList<IncidentTimelineSourceStatus>? statuses = null,
        bool isPartial = false,
        bool wasTruncated = false) =>
        new()
        {
            Query = new IncidentTimelineQuery { Scope = DefaultScope, Window = DefaultWindow },
            Items = items ?? [],
            SourceStatuses = statuses ?? BuildDefaultStatuses(),
            IsPartial = isPartial,
            WasTruncated = wasTruncated,
        };

    private static List<IncidentTimelineSourceStatus> BuildDefaultStatuses() =>
    [
        new(IncidentTimelineSource.Observability, IncidentTimelineSourceOutcome.Loaded,
            IncidentTimelineSourceCoverageState.Loaded, 200, 5, false, null, null),
        new(IncidentTimelineSource.ServiceBus, IncidentTimelineSourceOutcome.Loaded,
            IncidentTimelineSourceCoverageState.NoData, 80, 0, false, null, null),
    ];

    private static IncidentTimelineItem MakeItem(
        string itemId = "item-1",
        IReadOnlyDictionary<string, string?>? metadata = null) =>
        new()
        {
            ItemId = itemId,
            TimestampUtc = new DateTimeOffset(2025, 1, 10, 8, 15, 0, TimeSpan.Zero),
            Source = IncidentTimelineSource.Observability,
            Severity = IncidentTimelineSeverity.Error,
            Title = "App Insights exception: NullReferenceException",
            Summary = "Object reference not set to an instance.",
            Metadata = metadata ?? new Dictionary<string, string?>
            {
                ["recordType"] = "exception",
                ["role"] = "my-api",
                ["correlationId"] = "abc-123",
                ["secretKey"] = "do-not-export",
            },
        };

    private readonly IncidentSnapshotExporter _sut = new();

    // ── Build: core structure ─────────────────────────────────────────────────

    [Fact]
    public void Build_PopulatesWorkloadScope()
    {
        var snapshot = _sut.Build(MakePage());
        Assert.Contains("my-ns", snapshot.WorkloadScope);
        Assert.Contains("my-api", snapshot.WorkloadScope);
    }

    [Fact]
    public void Build_PopulatesWindowUtc()
    {
        var snapshot = _sut.Build(MakePage());
        Assert.Contains("2025-01-10", snapshot.WindowUtc);
    }

    [Fact]
    public void Build_IncludesAllItems()
    {
        var page = MakePage(items: [MakeItem("i1"), MakeItem("i2")]);
        var snapshot = _sut.Build(page);
        Assert.Equal(2, snapshot.Items.Count);
    }

    [Fact]
    public void Build_IncludesSourceCoverages()
    {
        var snapshot = _sut.Build(MakePage());
        Assert.Equal(2, snapshot.SourceCoverages.Count);
    }

    [Fact]
    public void Build_DisclaimerIsPresent()
    {
        var snapshot = _sut.Build(MakePage());
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Disclaimer));
        Assert.Contains("evidence summary", snapshot.Disclaimer, StringComparison.OrdinalIgnoreCase);
    }

    // ── Build: metadata allow-list ────────────────────────────────────────────

    [Fact]
    public void Build_ExcludesDisallowedMetadataKeys()
    {
        var item = MakeItem(metadata: new Dictionary<string, string?>
        {
            ["recordType"] = "exception",
            ["secretKey"] = "should-not-appear",
            ["body"] = "message-body-should-not-appear",
        });
        var snapshot = _sut.Build(MakePage(items: [item]));
        var exportedItem = snapshot.Items[0];
        Assert.False(exportedItem.SafeMetadata.ContainsKey("secretKey"));
        Assert.False(exportedItem.SafeMetadata.ContainsKey("body"));
    }

    [Fact]
    public void Build_IncludesAllowedMetadataKeys()
    {
        var item = MakeItem(metadata: new Dictionary<string, string?>
        {
            ["recordType"] = "request",
            ["role"] = "my-api",
            ["correlationId"] = "trace-id-123",
        });
        var snapshot = _sut.Build(MakePage(items: [item]));
        var exportedItem = snapshot.Items[0];
        Assert.True(exportedItem.SafeMetadata.ContainsKey("recordType"));
        Assert.True(exportedItem.SafeMetadata.ContainsKey("role"));
        Assert.True(exportedItem.SafeMetadata.ContainsKey("correlationId"));
    }

    // ── Build: metadata value truncation ─────────────────────────────────────

    [Fact]
    public void Build_TruncatesLongMetadataValues()
    {
        var longValue = new string('x', 300);
        var item = MakeItem(metadata: new Dictionary<string, string?>
        {
            ["operation"] = longValue,
        });
        var snapshot = _sut.Build(MakePage(items: [item]));
        var exportedValue = snapshot.Items[0].SafeMetadata["operation"];
        Assert.NotNull(exportedValue);
        Assert.True(exportedValue.Length <= 220, "Truncated value should be ≤200 chars + truncation suffix");
        Assert.Contains("[truncated]", exportedValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DoesNotTruncateShortMetadataValues()
    {
        var item = MakeItem(metadata: new Dictionary<string, string?>
        {
            ["operation"] = "ShortValue",
        });
        var snapshot = _sut.Build(MakePage(items: [item]));
        Assert.Equal("ShortValue", snapshot.Items[0].SafeMetadata["operation"]);
    }

    // ── Build: coverage label ─────────────────────────────────────────────────

    [Fact]
    public void Build_CoverageIsFull_WhenAllSourcesLoadedOrNoData()
    {
        var page = MakePage(isPartial: false, wasTruncated: false);
        var snapshot = _sut.Build(page);
        Assert.Equal("Full", snapshot.Coverage);
    }

    [Fact]
    public void Build_CoverageIsPartial_WhenIsPartialIsTrue()
    {
        var statuses = new List<IncidentTimelineSourceStatus>
        {
            new(IncidentTimelineSource.Observability, IncidentTimelineSourceOutcome.Loaded,
                IncidentTimelineSourceCoverageState.Partial, 200, 3, false, "partial error", null),
        };
        var page = MakePage(statuses: statuses, isPartial: true, wasTruncated: false);
        var snapshot = _sut.Build(page);
        Assert.Equal("Partial", snapshot.Coverage);
    }

    [Fact]
    public void Build_CoverageIsDegraded_WhenAnySourceFailed()
    {
        var statuses = new List<IncidentTimelineSourceStatus>
        {
            new(IncidentTimelineSource.Observability, IncidentTimelineSourceOutcome.Failed,
                IncidentTimelineSourceCoverageState.Failed, 200, 0, false, "timeout", null),
        };
        var page = MakePage(statuses: statuses, isPartial: true, wasTruncated: false);
        var snapshot = _sut.Build(page);
        Assert.Equal("Degraded", snapshot.Coverage);
    }

    [Fact]
    public void Build_CoverageIsDegraded_WhenAnySourceTimedOut()
    {
        var statuses = new List<IncidentTimelineSourceStatus>
        {
            new(IncidentTimelineSource.ServiceBus, IncidentTimelineSourceOutcome.Failed,
                IncidentTimelineSourceCoverageState.TimedOut, 9000, 0, false, "timed out", null),
        };
        var page = MakePage(statuses: statuses, isPartial: true, wasTruncated: false);
        var snapshot = _sut.Build(page);
        Assert.Equal("Degraded", snapshot.Coverage);
    }

    // ── Build: seed provenance ────────────────────────────────────────────────

    [Fact]
    public void Build_IncludesSeedProvenance_WhenSeedProvided()
    {
        var seed = new IncidentInvestigationSeed
        {
            SourceArea = IncidentInvestigationSourceArea.Observability,
            LaunchedAtUtc = DateTimeOffset.UtcNow,
            SelectedRange = DefaultWindow,
            EvidenceRef = new IncidentSeedEvidenceRef { ExceptionType = "NullReferenceException" },
        };
        var snapshot = _sut.Build(MakePage(), seed);
        Assert.NotNull(snapshot.SeedProvenance);
        Assert.Contains("Observability", snapshot.SeedProvenance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_SeedProvenanceIsNull_WhenNoSeed()
    {
        var snapshot = _sut.Build(MakePage(), seed: null);
        Assert.Null(snapshot.SeedProvenance);
    }

    // ── ToJson ────────────────────────────────────────────────────────────────

    [Fact]
    public void ToJson_ProducesValidJson()
    {
        var snapshot = _sut.Build(MakePage(items: [MakeItem()]));
        var json = _sut.ToJson(snapshot);
        var doc = JsonDocument.Parse(json);    // Should not throw
        Assert.NotNull(doc);
    }

    [Fact]
    public void ToJson_ContainsDisclaimer()
    {
        var snapshot = _sut.Build(MakePage());
        var json = _sut.ToJson(snapshot);
        Assert.Contains("evidence summary", json, StringComparison.OrdinalIgnoreCase);
    }

    // ── ToMarkdown ────────────────────────────────────────────────────────────

    [Fact]
    public void ToMarkdown_ContainsWorkloadScope()
    {
        var snapshot = _sut.Build(MakePage());
        var md = _sut.ToMarkdown(snapshot);
        Assert.Contains("my-ns", md, StringComparison.Ordinal);
        Assert.Contains("my-api", md, StringComparison.Ordinal);
    }

    [Fact]
    public void ToMarkdown_ContainsDisclaimer()
    {
        var snapshot = _sut.Build(MakePage());
        var md = _sut.ToMarkdown(snapshot);
        Assert.Contains("evidence summary", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToMarkdown_ContainsItemTitle_WhenItemsPresent()
    {
        var page = MakePage(items: [MakeItem()]);
        var snapshot = _sut.Build(page);
        var md = _sut.ToMarkdown(snapshot);
        Assert.Contains("NullReferenceException", md, StringComparison.Ordinal);
    }

    // ── GetSuggestedFileName ──────────────────────────────────────────────────

    [Fact]
    public void GetSuggestedFileName_ContainsFormatExtension()
    {
        var snapshot = _sut.Build(MakePage());
        Assert.EndsWith(".json", _sut.GetSuggestedFileName(snapshot, "json"), StringComparison.Ordinal);
        Assert.EndsWith(".md", _sut.GetSuggestedFileName(snapshot, "md"), StringComparison.Ordinal);
    }

    [Fact]
    public void GetSuggestedFileName_ContainsWorkloadScope()
    {
        var snapshot = _sut.Build(MakePage());
        var fileName = _sut.GetSuggestedFileName(snapshot, "json");
        Assert.Contains("my-ns", fileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("my-api", fileName, StringComparison.OrdinalIgnoreCase);
    }
}
