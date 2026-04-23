using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class IncidentInvestigationSeedResolverTests
{
    private static readonly TimeRange AnyRange =
        new(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 6, 1, 1, 0, 0, TimeSpan.Zero));

    private static IncidentInvestigationSeed ObsSeed(
        string? resourceId = null,
        string? exceptionType = null,
        IncidentWorkloadScope? candidateScope = null,
        IReadOnlyList<IncidentTimelineSource>? suggestedSources = null) =>
        new()
        {
            SourceArea = IncidentInvestigationSourceArea.Observability,
            LaunchedAtUtc = DateTimeOffset.UtcNow,
            SelectedRange = AnyRange,
            EvidenceRef = new IncidentSeedEvidenceRef
            {
                ResourceId = resourceId,
                ExceptionType = exceptionType
            },
            CandidateScope = candidateScope,
            SuggestedSources = suggestedSources
        };

    private static IncidentInvestigationSeed SbSeed(
        string? entityPath = null,
        string? messageId = null,
        IncidentWorkloadScope? candidateScope = null) =>
        new()
        {
            SourceArea = IncidentInvestigationSourceArea.ServiceBus,
            LaunchedAtUtc = DateTimeOffset.UtcNow,
            SelectedRange = AnyRange,
            EvidenceRef = new IncidentSeedEvidenceRef
            {
                EntityPath = entityPath,
                MessageId = messageId
            },
            CandidateScope = candidateScope
        };

    private static IncidentInvestigationSeed PipelinesSeed(
        int? pipelineId = null,
        string? projectName = null,
        IncidentWorkloadScope? candidateScope = null) =>
        new()
        {
            SourceArea = IncidentInvestigationSourceArea.Pipelines,
            LaunchedAtUtc = DateTimeOffset.UtcNow,
            SelectedRange = AnyRange,
            EvidenceRef = new IncidentSeedEvidenceRef
            {
                PipelineId = pipelineId,
                ProjectName = projectName
            },
            CandidateScope = candidateScope
        };

    private static IncidentTimelineConfig ConfigWithMapping(
        string ns = "my-ns",
        string workload = "my-api",
        IncidentWorkloadKind kind = IncidentWorkloadKind.Deployment,
        string? resourceId = null,
        string? sbEntityPath = null,
        int? pipelineId = null) =>
        new()
        {
            WorkloadMappings =
            [
                new IncidentTimelineWorkloadMapping
                {
                    Namespace = ns,
                    WorkloadName = workload,
                    WorkloadKind = kind,
                    Observability = resourceId is not null
                        ? new IncidentTimelineObservabilityMapping { ResourceId = resourceId }
                        : null,
                    ServiceBusEntities = sbEntityPath is not null
                        ? [new SbEntityLink { EntityPath = sbEntityPath }]
                        : [],
                    DevOps = pipelineId is not null
                        ? new IncidentTimelineDevOpsMapping
                          {
                              Pipelines = [new IncidentTimelinePipelineBinding { PipelineId = pipelineId.Value, ProjectName = "my-project" }]
                          }
                        : null
                }
            ]
        };

    private static IncidentTimelineConfig EmptyConfig() => new() { WorkloadMappings = [] };

    // ── Candidate scope ────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_CandidateScope_WithMatchingMapping_ResolvesScopeFromMapping()
    {
        var scope = new IncidentWorkloadScope(null, "my-ns", IncidentWorkloadKind.Deployment, "my-api");
        var seed = ObsSeed(candidateScope: scope);
        var config = ConfigWithMapping();

        var draft = new IncidentInvestigationSeedResolver().Resolve(seed, config);

        Assert.Equal(scope, draft.ResolvedScope);
        Assert.True(draft.ScopeFromMapping);
        Assert.Empty(draft.PendingAssumptions);
    }

    [Fact]
    public void Resolve_CandidateScope_WithNoMapping_UsesScopeWithAssumption()
    {
        var scope = new IncidentWorkloadScope(null, "unknown-ns", IncidentWorkloadKind.Deployment, "some-api");
        var seed = ObsSeed(candidateScope: scope);
        var config = EmptyConfig();

        var draft = new IncidentInvestigationSeedResolver().Resolve(seed, config);

        Assert.Equal(scope, draft.ResolvedScope);
        Assert.False(draft.ScopeFromMapping);
        Assert.Single(draft.PendingAssumptions);
    }

    // ── Service Bus entity path matching ──────────────────────────────────────

    [Fact]
    public void Resolve_SbSeed_WithMatchingEntityPath_ResolvesScope()
    {
        var seed = SbSeed(entityPath: "orders");
        var config = ConfigWithMapping(sbEntityPath: "orders");

        var draft = new IncidentInvestigationSeedResolver().Resolve(seed, config);

        Assert.NotNull(draft.ResolvedScope);
        Assert.Equal("my-ns", draft.ResolvedScope!.Namespace);
        Assert.Equal("my-api", draft.ResolvedScope.WorkloadName);
        Assert.True(draft.ScopeFromMapping);
        Assert.Empty(draft.PendingAssumptions);
    }

    [Fact]
    public void Resolve_SbSeed_EntityPathMatchIsCaseInsensitive()
    {
        var seed = SbSeed(entityPath: "ORDERS");
        var config = ConfigWithMapping(sbEntityPath: "orders");

        var draft = new IncidentInvestigationSeedResolver().Resolve(seed, config);

        Assert.NotNull(draft.ResolvedScope);
        Assert.True(draft.ScopeFromMapping);
    }

    [Fact]
    public void Resolve_SbSeed_NoMatchingEntityPath_NullScope_HasAssumption()
    {
        var seed = SbSeed(entityPath: "unknown-queue");
        var config = ConfigWithMapping(sbEntityPath: "orders");

        var draft = new IncidentInvestigationSeedResolver().Resolve(seed, config);

        Assert.Null(draft.ResolvedScope);
        Assert.False(draft.ScopeFromMapping);
        Assert.Single(draft.PendingAssumptions);
    }

    // ── Observability resourceId matching ─────────────────────────────────────

    [Fact]
    public void Resolve_ObsSeed_WithMatchingResourceId_ResolvesScope()
    {
        const string rid = "/subscriptions/abc/providers/microsoft.insights/components/myapp";
        var seed = ObsSeed(resourceId: rid);
        var config = ConfigWithMapping(resourceId: rid);

        var draft = new IncidentInvestigationSeedResolver().Resolve(seed, config);

        Assert.NotNull(draft.ResolvedScope);
        Assert.Equal("my-ns", draft.ResolvedScope!.Namespace);
        Assert.True(draft.ScopeFromMapping);
        Assert.Empty(draft.PendingAssumptions);
    }

    [Fact]
    public void Resolve_ObsSeed_NoMatchingResourceId_NullScope_HasAssumption()
    {
        var seed = ObsSeed(resourceId: "/subscriptions/other");
        var config = ConfigWithMapping(resourceId: "/subscriptions/mine");

        var draft = new IncidentInvestigationSeedResolver().Resolve(seed, config);

        Assert.Null(draft.ResolvedScope);
        Assert.False(draft.ScopeFromMapping);
        Assert.Single(draft.PendingAssumptions);
    }

    // ── Pipeline ID matching ───────────────────────────────────────────────────

    [Fact]
    public void Resolve_PipelinesSeed_WithMatchingPipelineId_ResolvesScope()
    {
        var seed = PipelinesSeed(pipelineId: 42);
        var config = ConfigWithMapping(pipelineId: 42);

        var draft = new IncidentInvestigationSeedResolver().Resolve(seed, config);

        Assert.NotNull(draft.ResolvedScope);
        Assert.Equal("my-ns", draft.ResolvedScope!.Namespace);
        Assert.True(draft.ScopeFromMapping);
        Assert.Empty(draft.PendingAssumptions);
    }

    [Fact]
    public void Resolve_PipelinesSeed_NoMatchingPipelineId_NullScope_HasAssumption()
    {
        var seed = PipelinesSeed(pipelineId: 99);
        var config = ConfigWithMapping(pipelineId: 42);

        var draft = new IncidentInvestigationSeedResolver().Resolve(seed, config);

        Assert.Null(draft.ResolvedScope);
        Assert.Single(draft.PendingAssumptions);
    }

    // ── Suggested sources override ─────────────────────────────────────────────

    [Fact]
    public void Resolve_SuggestedSources_OverrideDefaultPreselection()
    {
        var explicit_ = new List<IncidentTimelineSource>
        {
            IncidentTimelineSource.ServiceBus,
            IncidentTimelineSource.Observability
        };
        var seed = ObsSeed(suggestedSources: explicit_);

        var draft = new IncidentInvestigationSeedResolver().Resolve(seed, EmptyConfig());

        Assert.Equal(explicit_, draft.PreselectedSources);
    }

    // ── Default source biasing per SourceArea ──────────────────────────────────

    [Fact]
    public void Resolve_ObsSeed_NoSuggestedSources_DefaultsIncludeObservabilityAndAks()
    {
        var draft = new IncidentInvestigationSeedResolver().Resolve(ObsSeed(), EmptyConfig());

        Assert.Contains(IncidentTimelineSource.Observability, draft.PreselectedSources);
        Assert.Contains(IncidentTimelineSource.Aks, draft.PreselectedSources);
        Assert.DoesNotContain(IncidentTimelineSource.ServiceBus, draft.PreselectedSources);
    }

    [Fact]
    public void Resolve_SbSeed_NoSuggestedSources_DefaultsIncludeServiceBusAndAks()
    {
        var draft = new IncidentInvestigationSeedResolver().Resolve(SbSeed(), EmptyConfig());

        Assert.Contains(IncidentTimelineSource.ServiceBus, draft.PreselectedSources);
        Assert.Contains(IncidentTimelineSource.Aks, draft.PreselectedSources);
        Assert.DoesNotContain(IncidentTimelineSource.Observability, draft.PreselectedSources);
    }

    [Fact]
    public void Resolve_PipelinesSeed_NoSuggestedSources_DefaultsIncludeAksAndReleases()
    {
        var draft = new IncidentInvestigationSeedResolver().Resolve(PipelinesSeed(), EmptyConfig());

        Assert.Contains(IncidentTimelineSource.Aks, draft.PreselectedSources);
        Assert.Contains(IncidentTimelineSource.Releases, draft.PreselectedSources);
        Assert.DoesNotContain(IncidentTimelineSource.ServiceBus, draft.PreselectedSources);
    }

    // ── Provenance summary ─────────────────────────────────────────────────────

    [Fact]
    public void Resolve_ProvenanceSummary_IncludesSourceAreaLabel()
    {
        var seed = ObsSeed(exceptionType: "System.NullReferenceException");
        var draft = new IncidentInvestigationSeedResolver().Resolve(seed, EmptyConfig());

        Assert.Contains("Observability", draft.ProvenanceSummary);
    }

    [Fact]
    public void Resolve_ProvenanceSummary_IncludesExceptionType_WhenPresent()
    {
        var seed = ObsSeed(exceptionType: "ArgumentNullException");
        var draft = new IncidentInvestigationSeedResolver().Resolve(seed, EmptyConfig());

        Assert.Contains("ArgumentNullException", draft.ProvenanceSummary);
    }

    [Fact]
    public void Resolve_SeedCarriedsItself_InDraft()
    {
        var seed = ObsSeed(resourceId: "r1");
        var draft = new IncidentInvestigationSeedResolver().Resolve(seed, EmptyConfig());

        Assert.Same(seed, draft.Seed);
    }
}
