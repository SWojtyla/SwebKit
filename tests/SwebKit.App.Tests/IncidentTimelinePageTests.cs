using Bunit;
using Bunit.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.Pages;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using IncidentTimelinePageComponent = SwebKit.App.Components.Pages.IncidentTimelinePage;
using IncidentTimelineResultPage = SwebKit.Core.Models.IncidentTimelinePage;

namespace SwebKit.App.Tests;

[Collection("AppDataSerial")]
public sealed class IncidentTimelinePageTests : TestContext
{
    private readonly AppStateService _appState;
    private readonly AppEventBus _eventBus;
    private readonly FakeAksBootstrapper _aksBootstrapper;
    private readonly QueueIncidentTimelineService _timelineService;

    public IncidentTimelinePageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var uiState = new UiStateRepository();

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
        {
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
        }

        Services.AddFluentUIComponents();

        _eventBus = new AppEventBus(NullLogger<AppEventBus>.Instance);
        _appState = new AppStateService(new ProfileRepository(), uiState, _eventBus);
        _appState.InitializeAsync().GetAwaiter().GetResult();
        _appState.Config.Name = "Prod";
        _appState.Config.AksConfig = new AksConfig
        {
            DefaultNamespace = "prd-phonotif",
            KubeconfigContext = "ctx-prod",
            WatchedDeployments = ["phonotif-api"],
        };
        _appState.Config.IncidentTimeline = new IncidentTimelineConfig
        {
            WorkloadMappings =
            [
                new IncidentTimelineWorkloadMapping
                {
                    Namespace = "prd-phonotif",
                    WorkloadKind = IncidentWorkloadKind.Deployment,
                    WorkloadName = "phonotif-api",
                    DisplayName = "Phonotif API",
                }
            ]
        };

        _aksBootstrapper = new FakeAksBootstrapper();
        _timelineService = new QueueIncidentTimelineService();

        Services.AddSingleton<IAppEventBus>(_eventBus);
        Services.AddSingleton(_appState);
        Services.AddSingleton(uiState);
        Services.AddSingleton<IConnectionStateService, ConnectionStateService>();
        Services.AddSingleton(new CommandRegistry(uiState));
        Services.AddSingleton<IAksClientBootstrapper>(_aksBootstrapper);
        Services.AddSingleton<IIncidentTimelineService>(_timelineService);
        Services.AddSingleton<IIncidentMappingProposalGenerator, IncidentMappingProposalGenerator>();
        Services.AddSingleton<IIncidentInvestigationSeedResolver, IncidentInvestigationSeedResolver>();
        Services.AddSingleton(sp => new IncidentInvestigationLauncher(sp.GetRequiredService<NavigationManager>()));
        Services.AddScoped<OperatorWorkspaceService>();
    }

    [Fact]
    public void InitialLoad_RendersEvidenceList_AndDetailPanel()
    {
        _timelineService.Enqueue((query, _) => Task.FromResult(CreatePage(
            query,
            [
                CreateItem(
                    "aks-restart",
                    IncidentTimelineSource.Aks,
                    "Pod restart count increased",
                    IncidentTimelineSeverity.Error,
                    IncidentLinkRelevance.Direct,
                    "Restart count exceeded the normal baseline."),
            ],
            [
                CreateStatus(IncidentTimelineSource.Aks, IncidentTimelineSourceCoverageState.Loaded, itemCount: 1),
                CreateStatus(IncidentTimelineSource.Observability, IncidentTimelineSourceCoverageState.NoData),
                CreateStatus(IncidentTimelineSource.ServiceBus, IncidentTimelineSourceCoverageState.NoData),
                CreateStatus(IncidentTimelineSource.Releases, IncidentTimelineSourceCoverageState.NoData),
            ])));

        var cut = RenderComponent<IncidentTimelinePageComponent>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Pod restart count increased", cut.Markup);
            var detailPanel = cut.Find("[data-testid='incident-detail-panel']");
            Assert.Contains("Linked because", detailPanel.TextContent);
            Assert.Contains("Pod restart count increased", detailPanel.TextContent);
        });
    }

    [Fact]
    public void SelectingAnotherRow_UpdatesDetailPanel()
    {
        _timelineService.Enqueue((query, _) => Task.FromResult(CreatePage(
            query,
            [
                CreateItem("first-item", IncidentTimelineSource.Aks, "First evidence", IncidentTimelineSeverity.Warning),
                CreateItem("second-item", IncidentTimelineSource.Releases, "Second evidence", IncidentTimelineSeverity.Info),
            ],
            [
                CreateStatus(IncidentTimelineSource.Aks, IncidentTimelineSourceCoverageState.Loaded, itemCount: 1),
                CreateStatus(IncidentTimelineSource.Releases, IncidentTimelineSourceCoverageState.Loaded, itemCount: 1),
            ])));

        var cut = RenderComponent<IncidentTimelinePageComponent>();

        cut.WaitForAssertion(() => Assert.Contains("First evidence", cut.Markup));
        cut.Find("[data-testid='incident-row-second-item']").Click();

        cut.WaitForAssertion(() =>
        {
            var detailPanel = cut.Find("[data-testid='incident-detail-panel']");
            Assert.Contains("Second evidence", detailPanel.TextContent);
            Assert.DoesNotContain("First evidence", detailPanel.TextContent);
        });
    }

    [Fact]
    public void ManualRefresh_UsesUpdatedSourceSelection()
    {
        _timelineService.Enqueue((query, _) => Task.FromResult(CreatePage(
            query,
            [CreateItem("initial", IncidentTimelineSource.Aks, "Initial evidence")],
            [CreateStatus(IncidentTimelineSource.Aks, IncidentTimelineSourceCoverageState.Loaded, itemCount: 1)])));
        _timelineService.Enqueue((query, _) => Task.FromResult(CreatePage(
            query,
            [CreateItem("refreshed", IncidentTimelineSource.Aks, "Refreshed evidence")],
            [CreateStatus(IncidentTimelineSource.Aks, IncidentTimelineSourceCoverageState.Loaded, itemCount: 1)])));

        var cut = RenderComponent<IncidentTimelinePageComponent>();

        cut.WaitForAssertion(() => Assert.Contains("Initial evidence", cut.Markup));
        cut.Find("[data-testid='incident-source-service-bus']").Click();
        cut.Find("[data-testid='incident-refresh-button']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Refreshed evidence", cut.Markup));
        Assert.Equal(2, _timelineService.Queries.Count);
        Assert.DoesNotContain(IncidentTimelineSource.ServiceBus, _timelineService.Queries[1].GetRequestedSources());
    }

    [Fact]
    public void SourceToggles_ShowExplicitOnOffState()
    {
        _timelineService.Enqueue((query, _) => Task.FromResult(CreatePage(
            query,
            [CreateItem("initial", IncidentTimelineSource.Aks, "Initial evidence")],
            [CreateStatus(IncidentTimelineSource.Aks, IncidentTimelineSourceCoverageState.Loaded, itemCount: 1)])));

        var cut = RenderComponent<IncidentTimelinePageComponent>();

        cut.WaitForAssertion(() => Assert.Contains("Initial evidence", cut.Markup));

        var serviceBusToggle = cut.Find("[data-testid='incident-source-service-bus']");
        Assert.Contains("On", serviceBusToggle.TextContent, StringComparison.OrdinalIgnoreCase);

        serviceBusToggle.Click();

        cut.WaitForAssertion(() => Assert.Contains("Off", cut.Find("[data-testid='incident-source-service-bus']").TextContent, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EmptyWindow_RendersEmptyState()
    {
        _timelineService.Enqueue((query, _) => Task.FromResult(CreatePage(
            query,
            [],
            [
                CreateStatus(IncidentTimelineSource.Aks, IncidentTimelineSourceCoverageState.NoData),
                CreateStatus(IncidentTimelineSource.Observability, IncidentTimelineSourceCoverageState.Unmapped),
            ],
            isPartial: true)));

        var cut = RenderComponent<IncidentTimelinePageComponent>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No evidence found in this window", cut.Markup);
            Assert.Contains("Unmapped", cut.Markup);
        });
    }

    [Fact]
    public void FullSourceFailure_RendersErrorState()
    {
        _timelineService.Enqueue((query, _) => Task.FromResult(CreatePage(
            query,
            [],
            [
                CreateStatus(IncidentTimelineSource.Aks, IncidentTimelineSourceCoverageState.Failed, errorMessage: "AKS query failed."),
                CreateStatus(IncidentTimelineSource.Observability, IncidentTimelineSourceCoverageState.TimedOut, errorMessage: "App Insights timed out."),
            ],
            isPartial: true)));

        var cut = RenderComponent<IncidentTimelinePageComponent>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("All selected evidence sources failed", cut.Markup);
            Assert.Contains("AKS query failed.", cut.Markup);
        });
    }

    [Fact]
    public void UnmappedSources_ShowMappingGuidance_AndNavigateToSettings()
    {
        _timelineService.Enqueue((query, _) => Task.FromResult(CreatePage(
            query,
            [],
            [
                CreateStatus(IncidentTimelineSource.Aks, IncidentTimelineSourceCoverageState.NoData),
                CreateStatus(IncidentTimelineSource.Observability, IncidentTimelineSourceCoverageState.Unmapped),
                CreateStatus(IncidentTimelineSource.ServiceBus, IncidentTimelineSourceCoverageState.NotConfigured),
            ],
            isPartial: true)));

        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = RenderComponent<IncidentTimelinePageComponent>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Mapping guidance", cut.Markup);
            Assert.Contains("Open Incident Timeline settings", cut.Markup);
        });

        cut.Find("[data-testid='incident-mapping-settings-button']").Click();

        Assert.Contains("section=incident-timeline", nav.Uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("namespace=prd-phonotif", nav.Uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workloadKind=Deployment", nav.Uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workloadName=phonotif-api", nav.Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PartialAndTruncatedStates_AreVisible()
    {
        _timelineService.Enqueue((query, _) => Task.FromResult(CreatePage(
            query,
            [CreateItem("partial", IncidentTimelineSource.Aks, "Partial evidence")],
            [
                CreateStatus(IncidentTimelineSource.Aks, IncidentTimelineSourceCoverageState.Loaded, itemCount: 1),
                CreateStatus(IncidentTimelineSource.ServiceBus, IncidentTimelineSourceCoverageState.Partial, itemCount: 0, errorMessage: "Queue peek returned partial data."),
            ],
            isPartial: true,
            wasTruncated: true)));

        var cut = RenderComponent<IncidentTimelinePageComponent>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Evidence is partial.", cut.Markup);
            Assert.Contains("Results were capped for this window", cut.Markup);
            Assert.Contains("Partial", cut.Markup);
        });
    }

    [Fact]
    public void LatestRefreshWins_WhenEarlierResponseCompletesLater()
    {
        var staleResponseSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _timelineService.Enqueue((query, _) => Task.FromResult(CreatePage(
            query,
            [CreateItem("initial", IncidentTimelineSource.Aks, "Initial evidence")],
            [CreateStatus(IncidentTimelineSource.Aks, IncidentTimelineSourceCoverageState.Loaded, itemCount: 1)])));

        _timelineService.Enqueue(async (query, _) =>
        {
            await staleResponseSignal.Task;
            return CreatePage(
                query,
                [CreateItem("older", IncidentTimelineSource.Aks, "Older response")],
                [CreateStatus(IncidentTimelineSource.Aks, IncidentTimelineSourceCoverageState.Loaded, itemCount: 1)]);
        });

        _timelineService.Enqueue((query, _) => Task.FromResult(CreatePage(
            query,
            [CreateItem("latest", IncidentTimelineSource.Aks, "Latest response")],
            [CreateStatus(IncidentTimelineSource.Aks, IncidentTimelineSourceCoverageState.Loaded, itemCount: 1)])));

        var cut = RenderComponent<IncidentTimelinePageComponent>();

        cut.WaitForAssertion(() => Assert.Contains("Initial evidence", cut.Markup));

        cut.Find("[data-testid='incident-workload-input']").Input("older-api");
        cut.Find("[data-testid='incident-refresh-button']").Click();

        cut.Find("[data-testid='incident-workload-input']").Input("latest-api");
        cut.Find("[data-testid='incident-refresh-button']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Latest response", cut.Markup));

        staleResponseSignal.SetResult();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Latest response", cut.Markup);
            Assert.DoesNotContain("Older response", cut.Markup);
        });

        Assert.Equal(3, _timelineService.Queries.Count);
        Assert.Equal("older-api", _timelineService.Queries[1].Scope.WorkloadName);
        Assert.Equal("latest-api", _timelineService.Queries[2].Scope.WorkloadName);
    }

    private static AksClientBootstrapResult CreateBootstrapResult() => new(
        AksClientBootstrapStatus.Connected,
        Client: null,
        Contexts:
        [
            new KubeContextInfo { Name = "ctx-prod", IsCurrent = true },
            new KubeContextInfo { Name = "ctx-dr", IsCurrent = false },
        ],
        Namespaces: ["prd-phonotif", "default"],
        ActiveContext: "ctx-prod",
        CurrentNamespace: "prd-phonotif",
        ErrorMessage: null);

    private static IncidentTimelineItem CreateItem(
        string id,
        IncidentTimelineSource source,
        string title,
        IncidentTimelineSeverity severity = IncidentTimelineSeverity.Info,
        IncidentLinkRelevance relevance = IncidentLinkRelevance.Direct,
        string? summary = null) => new()
        {
            ItemId = id,
            TimestampUtc = DateTimeOffset.UtcNow,
            Source = source,
            Severity = severity,
            Title = title,
            Summary = summary,
            LinkReasons =
        [
            new IncidentLinkReason(IncidentLinkReasonType.Topology, relevance, $"{title} is linked to the selected workload mapping."),
        ],
            Metadata = new Dictionary<string, string?>
            {
                ["node"] = "aks-node-01",
                ["pod"] = "phonotif-api-54dd4d",
            },
        };

    private static IncidentTimelineResultPage CreatePage(
        IncidentTimelineQuery query,
        IReadOnlyList<IncidentTimelineItem> items,
        IReadOnlyList<IncidentTimelineSourceStatus> statuses,
        bool isPartial = false,
        bool wasTruncated = false) => new()
        {
            Query = query,
            Items = items,
            SourceStatuses = statuses,
            IsPartial = isPartial,
            WasTruncated = wasTruncated,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
        };

    private static IncidentTimelineSourceStatus CreateStatus(
        IncidentTimelineSource source,
        IncidentTimelineSourceCoverageState coverageState,
        int itemCount = 0,
        bool wasTruncated = false,
        string? errorMessage = null,
        string? statusMessage = null)
    {
        var outcome = coverageState switch
        {
            IncidentTimelineSourceCoverageState.Unmapped or IncidentTimelineSourceCoverageState.NotConfigured => IncidentTimelineSourceOutcome.Skipped,
            IncidentTimelineSourceCoverageState.Failed or IncidentTimelineSourceCoverageState.TimedOut => IncidentTimelineSourceOutcome.Failed,
            _ => IncidentTimelineSourceOutcome.Loaded,
        };

        return new IncidentTimelineSourceStatus(
            source,
            outcome,
            coverageState,
            DurationMs: 12,
            ItemCount: itemCount,
            WasTruncated: wasTruncated,
            ErrorMessage: errorMessage,
            StatusMessage: statusMessage);
    }

    private sealed class FakeAksBootstrapper : IAksClientBootstrapper
    {
        public AksClientBootstrapResult Result { get; set; } = CreateBootstrapResult();

        public Task<AksClientBootstrapResult> BootstrapAsync(AksClientBootstrapRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Result);
        }
    }

    private sealed class QueueIncidentTimelineService : IIncidentTimelineService
    {
        private readonly Queue<Func<IncidentTimelineQuery, CancellationToken, Task<IncidentTimelineResultPage>>> _responses = new();

        public List<IncidentTimelineQuery> Queries { get; } = [];

        public void Enqueue(Func<IncidentTimelineQuery, CancellationToken, Task<IncidentTimelineResultPage>> response) => _responses.Enqueue(response);

        public Task<IncidentTimelineResultPage> GetTimelineAsync(IncidentTimelineQuery query, CancellationToken ct = default)
        {
            Queries.Add(query);

            if (_responses.Count == 0)
            {
                return Task.FromResult(CreatePage(query, [], []));
            }

            return _responses.Dequeue().Invoke(query, ct);
        }
    }
}