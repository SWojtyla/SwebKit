using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.DevOps.IncidentTimeline;

namespace SwebKit.DevOps.Tests;

public sealed class DevOpsReleaseTimelineSignalSourceTests
{
    [Fact]
    public async Task FetchAsync_ReturnsDemoReleaseAndPipelineRunEvidenceForMappedPipeline()
    {
        using var appDataRoot = new TemporaryAppDataRoot();
        var appState = CreateAppState(config =>
        {
            config.IncidentTimeline.WorkloadMappings.Add(new IncidentTimelineWorkloadMapping
            {
                Namespace = "prd-phonotif",
                WorkloadKind = IncidentWorkloadKind.Deployment,
                WorkloadName = "order-api",
                DevOps = new IncidentTimelineDevOpsMapping
                {
                    Pipelines =
                    [
                        new IncidentTimelinePipelineBinding
                        {
                            ProjectName = "ecommerce-platform",
                            PipelineId = 101,
                            Alias = "order-api-ci-cd",
                        },
                    ],
                },
            });
        });
        await appState.SetDemoModeAsync(true);

        var source = new DevOpsReleaseTimelineSignalSource(
            appState,
            new DummyFactory(),
            new ReleaseRepository(),
            new DemoDevOpsClient());

        var result = await source.FetchAsync(new IncidentTimelineQuery
        {
            Scope = new IncidentWorkloadScope("ctx", "prd-phonotif", IncidentWorkloadKind.Deployment, "order-api"),
            Window = new TimeRange(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow),
            SelectedSources = [IncidentTimelineSource.Releases],
            MaxItems = 20,
            MaxItemsPerSource = 20,
        });

        Assert.Equal(IncidentTimelineSourceCoverageState.Loaded, result.CoverageState);
        Assert.Contains(result.Items, item => item.Title.Contains("Release created", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Items, item => item.Title.Contains("Pipeline run", StringComparison.OrdinalIgnoreCase));
        Assert.All(result.Items, item => Assert.Equal(IncidentLinkRelevance.Contextual, item.PrimaryRelevance));
    }

    private static AppStateService CreateAppState(Action<AppConfig> configure)
    {
        var config = new AppConfig { Name = "Test" };
        configure(config);

        var repository = new ProfileRepository();
        repository.ReplaceProfileData(new ProfileData
        {
            Config = config,
        });

        return new AppStateService(repository, new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance));
    }

    private sealed class DummyFactory : IDevOpsClientFactory
    {
        public IDevOpsClient Create(DevOpsConfig config) => throw new NotSupportedException();
    }

    private sealed class TemporaryAppDataRoot : IDisposable
    {
        private readonly string? _previousRoot;

        public TemporaryAppDataRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "SwebKit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            _previousRoot = Environment.GetEnvironmentVariable("SWEBKIT_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", _previousRoot);
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}