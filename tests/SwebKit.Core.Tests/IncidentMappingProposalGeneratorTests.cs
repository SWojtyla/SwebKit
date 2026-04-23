using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class IncidentMappingProposalGeneratorTests
{
    private static readonly IncidentWorkloadScope DefaultScope =
        new(null, "production", IncidentWorkloadKind.Deployment, "order-api");

    private static readonly TimeRange DefaultWindow =
        new(new DateTimeOffset(2025, 1, 10, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 1, 10, 9, 0, 0, TimeSpan.Zero));

    private static IncidentTimelinePage MakePage(IReadOnlyList<IncidentTimelineSourceStatus> statuses) =>
        new()
        {
            Query = new IncidentTimelineQuery { Scope = DefaultScope, Window = DefaultWindow },
            Items = [],
            SourceStatuses = statuses,
        };

    private static IncidentTimelineConfig EmptyConfig() => new();

    private readonly IncidentMappingProposalGenerator _sut = new();

    // ── No proposals for fully mapped sources ─────────────────────────────────

    [Fact]
    public void Generate_ReturnsEmpty_WhenAllSourcesLoaded()
    {
        var statuses = new List<IncidentTimelineSourceStatus>
        {
            new(IncidentTimelineSource.Observability, IncidentTimelineSourceOutcome.Loaded,
                IncidentTimelineSourceCoverageState.Loaded, 150, 5, false, null, null),
            new(IncidentTimelineSource.ServiceBus, IncidentTimelineSourceOutcome.Loaded,
                IncidentTimelineSourceCoverageState.NoData, 80, 0, false, null, null),
        };
        var proposals = _sut.Generate(MakePage(statuses), EmptyConfig());
        Assert.Empty(proposals);
    }

    [Fact]
    public void Generate_ReturnsEmpty_WhenAllSourcesFailed_NotUnmapped()
    {
        var statuses = new List<IncidentTimelineSourceStatus>
        {
            new(IncidentTimelineSource.Observability, IncidentTimelineSourceOutcome.Failed,
                IncidentTimelineSourceCoverageState.Failed, 200, 0, false, "error", null),
        };
        var proposals = _sut.Generate(MakePage(statuses), EmptyConfig());
        // Failed sources do not produce mapping proposals.
        Assert.Empty(proposals);
    }

    // ── Proposals for Unmapped sources ────────────────────────────────────────

    [Fact]
    public void Generate_ReturnsProposal_ForUnmappedObservability()
    {
        var statuses = new List<IncidentTimelineSourceStatus>
        {
            new(IncidentTimelineSource.Observability, IncidentTimelineSourceOutcome.Loaded,
                IncidentTimelineSourceCoverageState.Unmapped, 10, 0, false, null, null),
        };
        var proposals = _sut.Generate(MakePage(statuses), EmptyConfig());
        Assert.Single(proposals);
        var p = proposals[0];
        Assert.Equal("Observability", p.SourceArea);
        Assert.Equal("production", p.Namespace);
        Assert.Equal("order-api", p.WorkloadName);
        Assert.Equal(IncidentProposalStatus.Candidate, p.Status);
    }

    [Fact]
    public void Generate_ReturnsProposal_ForUnmappedServiceBus()
    {
        var statuses = new List<IncidentTimelineSourceStatus>
        {
            new(IncidentTimelineSource.ServiceBus, IncidentTimelineSourceOutcome.Loaded,
                IncidentTimelineSourceCoverageState.Unmapped, 10, 0, false, null, null),
        };
        var proposals = _sut.Generate(MakePage(statuses), EmptyConfig());
        Assert.Single(proposals);
        Assert.Equal("ServiceBus", proposals[0].SourceArea);
    }

    [Fact]
    public void Generate_ReturnsProposal_ForUnmappedReleases()
    {
        var statuses = new List<IncidentTimelineSourceStatus>
        {
            new(IncidentTimelineSource.Releases, IncidentTimelineSourceOutcome.Loaded,
                IncidentTimelineSourceCoverageState.Unmapped, 10, 0, false, null, null),
        };
        var proposals = _sut.Generate(MakePage(statuses), EmptyConfig());
        Assert.Single(proposals);
        Assert.Equal("Pipelines", proposals[0].SourceArea);
    }

    // ── Proposals for NotConfigured sources ───────────────────────────────────

    [Fact]
    public void Generate_ReturnsProposal_ForNotConfiguredObservability()
    {
        var statuses = new List<IncidentTimelineSourceStatus>
        {
            new(IncidentTimelineSource.Observability, IncidentTimelineSourceOutcome.Loaded,
                IncidentTimelineSourceCoverageState.NotConfigured, 10, 0, false, null, null),
        };
        var proposals = _sut.Generate(MakePage(statuses), EmptyConfig());
        Assert.Single(proposals);
        Assert.Equal("Observability", proposals[0].SourceArea);
        Assert.False(string.IsNullOrWhiteSpace(proposals[0].Rationale));
    }

    // ── Multiple sources ──────────────────────────────────────────────────────

    [Fact]
    public void Generate_ReturnsSeparateProposal_ForEachUnmappedSource()
    {
        var statuses = new List<IncidentTimelineSourceStatus>
        {
            new(IncidentTimelineSource.Observability, IncidentTimelineSourceOutcome.Loaded,
                IncidentTimelineSourceCoverageState.Unmapped, 10, 0, false, null, null),
            new(IncidentTimelineSource.ServiceBus, IncidentTimelineSourceOutcome.Loaded,
                IncidentTimelineSourceCoverageState.Unmapped, 10, 0, false, null, null),
        };
        var proposals = _sut.Generate(MakePage(statuses), EmptyConfig());
        Assert.Equal(2, proposals.Count);
    }

    // ── Proposal content quality ──────────────────────────────────────────────

    [Fact]
    public void Generate_ProposalRationale_IsNonEmpty()
    {
        var statuses = new List<IncidentTimelineSourceStatus>
        {
            new(IncidentTimelineSource.Observability, IncidentTimelineSourceOutcome.Loaded,
                IncidentTimelineSourceCoverageState.Unmapped, 10, 0, false, null, null),
        };
        var proposals = _sut.Generate(MakePage(statuses), EmptyConfig());
        Assert.False(string.IsNullOrWhiteSpace(proposals[0].Rationale));
    }

    [Fact]
    public void Generate_ProposalRationale_ContainsWorkloadName()
    {
        var statuses = new List<IncidentTimelineSourceStatus>
        {
            new(IncidentTimelineSource.Observability, IncidentTimelineSourceOutcome.Loaded,
                IncidentTimelineSourceCoverageState.Unmapped, 10, 0, false, null, null),
        };
        var proposals = _sut.Generate(MakePage(statuses), EmptyConfig());
        Assert.Contains("order-api", proposals[0].Rationale, StringComparison.OrdinalIgnoreCase);
    }

    // ── Advisory-only: proposals never mutate state ───────────────────────────

    [Fact]
    public void Generate_DoesNotMutateConfig()
    {
        var config = new IncidentTimelineConfig
        {
            WorkloadMappings = new List<IncidentTimelineWorkloadMapping>
            {
                new() { Namespace = "production", WorkloadKind = IncidentWorkloadKind.Deployment, WorkloadName = "order-api" }
            }
        };
        var countBefore = config.WorkloadMappings.Count;
        var statuses = new List<IncidentTimelineSourceStatus>
        {
            new(IncidentTimelineSource.Observability, IncidentTimelineSourceOutcome.Loaded,
                IncidentTimelineSourceCoverageState.Unmapped, 10, 0, false, null, null),
        };
        _sut.Generate(MakePage(statuses), config);
        Assert.Equal(countBefore, config.WorkloadMappings.Count);
    }

    [Fact]
    public void Generate_AllProposalsAreStatusCandidate()
    {
        var statuses = new List<IncidentTimelineSourceStatus>
        {
            new(IncidentTimelineSource.Observability, IncidentTimelineSourceOutcome.Loaded,
                IncidentTimelineSourceCoverageState.Unmapped, 10, 0, false, null, null),
            new(IncidentTimelineSource.ServiceBus, IncidentTimelineSourceOutcome.Loaded,
                IncidentTimelineSourceCoverageState.Unmapped, 10, 0, false, null, null),
        };
        var proposals = _sut.Generate(MakePage(statuses), EmptyConfig());
        Assert.All(proposals, p => Assert.Equal(IncidentProposalStatus.Candidate, p.Status));
    }
}
