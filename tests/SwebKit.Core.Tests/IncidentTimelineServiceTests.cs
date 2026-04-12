using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class IncidentTimelineServiceTests
{
    [Fact]
    public async Task GetTimelineAsync_MergesSourcesInDeterministicUtcOrder()
    {
        var timestamp = new DateTimeOffset(2026, 04, 12, 12, 00, 00, TimeSpan.Zero);
        var service = new IncidentTimelineService(
        [
            new StubSource(IncidentTimelineSource.Aks, IncidentTimelineSourceResult.Loaded(IncidentTimelineSource.Aks,
            [
                CreateItem("aks-tie", IncidentTimelineSource.Aks, timestamp),
                CreateItem("aks-earlier", IncidentTimelineSource.Aks, timestamp.AddMinutes(-5)),
            ])),
            new StubSource(IncidentTimelineSource.Observability, IncidentTimelineSourceResult.Loaded(IncidentTimelineSource.Observability,
            [
                CreateItem("obs-later", IncidentTimelineSource.Observability, timestamp.AddMinutes(1)),
                CreateItem("obs-tie", IncidentTimelineSource.Observability, timestamp),
            ])),
        ]);

        var page = await service.GetTimelineAsync(CreateQuery());

        Assert.Collection(
            page.Items,
            item => Assert.Equal("obs-later", item.ItemId),
            item => Assert.Equal("aks-tie", item.ItemId),
            item => Assert.Equal("obs-tie", item.ItemId),
            item => Assert.Equal("aks-earlier", item.ItemId));
    }

    [Fact]
    public async Task GetTimelineAsync_ReturnsPartialResultsWhenASourceFails()
    {
        var service = new IncidentTimelineService(
        [
            new StubSource(IncidentTimelineSource.Aks, IncidentTimelineSourceResult.Loaded(IncidentTimelineSource.Aks,
            [
                CreateItem("aks-item", IncidentTimelineSource.Aks, DateTimeOffset.UtcNow),
            ])),
            new ThrowingSource(IncidentTimelineSource.ServiceBus, "boom"),
        ]);

        var page = await service.GetTimelineAsync(CreateQuery());

        Assert.True(page.IsPartial);
        Assert.Single(page.Items);
        Assert.Contains(page.SourceStatuses, status => status.Source == IncidentTimelineSource.ServiceBus
            && status.CoverageState == IncidentTimelineSourceCoverageState.Failed);
    }

    [Fact]
    public async Task GetTimelineAsync_TruncatesMergedItemsAtGlobalLimit()
    {
        var items = Enumerable.Range(0, 5)
            .Select(index => CreateItem($"aks-{index}", IncidentTimelineSource.Aks, DateTimeOffset.UtcNow.AddMinutes(-index)))
            .ToList();
        var service = new IncidentTimelineService(
        [
            new StubSource(IncidentTimelineSource.Aks, IncidentTimelineSourceResult.Loaded(IncidentTimelineSource.Aks, items)),
        ]);

        var page = await service.GetTimelineAsync(CreateQuery(maxItems: 2, maxItemsPerSource: 10));

        Assert.True(page.WasTruncated);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task GetTimelineAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        var blockingSource = new BlockingSource(IncidentTimelineSource.Aks);
        var service = new IncidentTimelineService([blockingSource]);

        var task = service.GetTimelineAsync(CreateQuery(), cts.Token);
        await blockingSource.Started.Task;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private static IncidentTimelineQuery CreateQuery(int maxItems = 10, int maxItemsPerSource = 10) => new()
    {
        Scope = new IncidentWorkloadScope("Test", "ctx", "prd-phonotif", IncidentWorkloadKind.Deployment, "phonotif-api"),
        Window = new TimeRange(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow),
        SelectedSources =
        [
            IncidentTimelineSource.Aks,
            IncidentTimelineSource.Observability,
            IncidentTimelineSource.ServiceBus,
            IncidentTimelineSource.Releases,
        ],
        MaxItems = maxItems,
        MaxItemsPerSource = maxItemsPerSource,
    };

    private static IncidentTimelineItem CreateItem(string id, IncidentTimelineSource source, DateTimeOffset timestamp) => new()
    {
        ItemId = id,
        TimestampUtc = timestamp,
        Source = source,
        Severity = IncidentTimelineSeverity.Info,
        Title = id,
        LinkReasons =
        [
            new IncidentLinkReason(IncidentLinkReasonType.Topology, IncidentLinkRelevance.Contextual, "test"),
        ],
    };

    private sealed class StubSource : IIncidentTimelineSignalSource
    {
        private readonly IncidentTimelineSourceResult _result;

        public StubSource(IncidentTimelineSource source, IncidentTimelineSourceResult result)
        {
            Source = source;
            _result = result;
        }

        public IncidentTimelineSource Source { get; }

        public Task<IncidentTimelineSourceResult> FetchAsync(IncidentTimelineQuery query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_result);
        }
    }

    private sealed class ThrowingSource : IIncidentTimelineSignalSource
    {
        private readonly string _message;

        public ThrowingSource(IncidentTimelineSource source, string message)
        {
            Source = source;
            _message = message;
        }

        public IncidentTimelineSource Source { get; }

        public Task<IncidentTimelineSourceResult> FetchAsync(IncidentTimelineQuery query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException(_message);
        }
    }

    private sealed class BlockingSource : IIncidentTimelineSignalSource
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingSource(IncidentTimelineSource source)
        {
            Source = source;
        }

        public IncidentTimelineSource Source { get; }

        public async Task<IncidentTimelineSourceResult> FetchAsync(IncidentTimelineQuery query, CancellationToken ct = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return IncidentTimelineSourceResult.Loaded(Source, []);
        }
    }
}