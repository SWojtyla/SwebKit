using System.Diagnostics;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

public sealed class IncidentTimelineService : IIncidentTimelineService
{
    private readonly IReadOnlyDictionary<IncidentTimelineSource, IIncidentTimelineSignalSource> _sources;

    public IncidentTimelineService(IEnumerable<IIncidentTimelineSignalSource> sources)
    {
        _sources = sources
            .GroupBy(static source => source.Source)
            .ToDictionary(static group => group.Key, static group => group.Last());
    }

    public async Task<IncidentTimelinePage> GetTimelineAsync(IncidentTimelineQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var requestedSources = query.GetRequestedSources();
        var orderLookup = requestedSources
            .Select((source, index) => new { source, index })
            .ToDictionary(static entry => entry.source, static entry => entry.index);
        var executions = await Task.WhenAll(requestedSources.Select(source => ExecuteSourceAsync(source, query, ct)));

        var items = executions
            .SelectMany(static execution => execution.Result.Items)
            .Select(NormalizeItem)
            .OrderByDescending(static item => item.TimestampUtc)
            .ThenBy(static item => item.Source)
            .ThenBy(static item => item.ItemId, StringComparer.Ordinal)
            .ToList();

        var wasTruncated = executions.Any(static execution => execution.Status.WasTruncated);
        var maxItems = query.GetMaxItems();
        if (items.Count > maxItems)
        {
            items = items.Take(maxItems).ToList();
            wasTruncated = true;
        }

        var statuses = executions
            .Select(static execution => execution.Status)
            .OrderBy(status => orderLookup.GetValueOrDefault(status.Source, int.MaxValue))
            .ToList();

        var isPartial = wasTruncated || statuses.Any(static status => status.CoverageState is
            IncidentTimelineSourceCoverageState.Partial or
            IncidentTimelineSourceCoverageState.Unmapped or
            IncidentTimelineSourceCoverageState.NotConfigured or
            IncidentTimelineSourceCoverageState.TimedOut or
            IncidentTimelineSourceCoverageState.Failed);

        return new IncidentTimelinePage
        {
            Query = query,
            Items = items,
            SourceStatuses = statuses,
            IsPartial = isPartial,
            WasTruncated = wasTruncated,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private async Task<SourceExecution> ExecuteSourceAsync(
        IncidentTimelineSource source,
        IncidentTimelineQuery query,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!_sources.TryGetValue(source, out var signalSource))
        {
            stopwatch.Stop();
            var missing = IncidentTimelineSourceResult.NotConfigured(source, "No signal source is registered.");
            return new SourceExecution(missing, BuildStatus(missing, stopwatch.Elapsed));
        }

        try
        {
            IncidentTimelineSourceResult rawResult;
            var timeout = query.GetPerSourceTimeout();

            if (timeout is { } sourceTimeout)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(sourceTimeout);

                try
                {
                    rawResult = await signalSource.FetchAsync(query, timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
                {
                    rawResult = IncidentTimelineSourceResult.TimedOut(
                        source,
                        $"Source did not complete within {sourceTimeout.TotalSeconds:0.#} seconds.");
                }
            }
            else
            {
                rawResult = await signalSource.FetchAsync(query, ct);
            }

            stopwatch.Stop();
            var normalized = NormalizeResult(rawResult, query);
            return new SourceExecution(normalized, BuildStatus(normalized, stopwatch.Elapsed));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var failed = IncidentTimelineSourceResult.Failed(source, ex.Message);
            return new SourceExecution(failed, BuildStatus(failed, stopwatch.Elapsed));
        }
    }

    private static IncidentTimelineSourceResult NormalizeResult(IncidentTimelineSourceResult result, IncidentTimelineQuery query)
    {
        var items = (result.Items ?? [])
            .Select(NormalizeItem)
            .OrderByDescending(static item => item.TimestampUtc)
            .ThenBy(static item => item.Source)
            .ThenBy(static item => item.ItemId, StringComparer.Ordinal)
            .ToList();

        var coverageState = result.CoverageState;
        var wasTruncated = result.WasTruncated;
        var maxItems = query.GetMaxItemsPerSource();
        if (items.Count > maxItems)
        {
            items = items.Take(maxItems).ToList();
            wasTruncated = true;
        }

        if (items.Count == 0 && coverageState == IncidentTimelineSourceCoverageState.Loaded)
        {
            coverageState = IncidentTimelineSourceCoverageState.NoData;
        }

        if (items.Count > 0 && coverageState == IncidentTimelineSourceCoverageState.NoData)
        {
            coverageState = IncidentTimelineSourceCoverageState.Loaded;
        }

        if (items.Count > 0 && coverageState is IncidentTimelineSourceCoverageState.Unmapped or IncidentTimelineSourceCoverageState.NotConfigured)
        {
            coverageState = IncidentTimelineSourceCoverageState.Partial;
        }

        return new IncidentTimelineSourceResult
        {
            Source = result.Source,
            CoverageState = coverageState,
            Items = items,
            WasTruncated = wasTruncated,
            ErrorMessage = result.ErrorMessage,
            StatusMessage = result.StatusMessage,
        };
    }

    private static IncidentTimelineSourceStatus BuildStatus(IncidentTimelineSourceResult result, TimeSpan duration) =>
        new(
            result.Source,
            MapOutcome(result.CoverageState),
            result.CoverageState,
            (long)Math.Round(duration.TotalMilliseconds),
            result.Items.Count,
            result.WasTruncated,
            result.ErrorMessage,
            result.StatusMessage);

    private static IncidentTimelineSourceOutcome MapOutcome(IncidentTimelineSourceCoverageState coverageState) => coverageState switch
    {
        IncidentTimelineSourceCoverageState.Loaded => IncidentTimelineSourceOutcome.Loaded,
        IncidentTimelineSourceCoverageState.Partial => IncidentTimelineSourceOutcome.Loaded,
        IncidentTimelineSourceCoverageState.NoData => IncidentTimelineSourceOutcome.Loaded,
        IncidentTimelineSourceCoverageState.Unmapped => IncidentTimelineSourceOutcome.Skipped,
        IncidentTimelineSourceCoverageState.NotConfigured => IncidentTimelineSourceOutcome.Skipped,
        _ => IncidentTimelineSourceOutcome.Failed,
    };

    private static IncidentTimelineItem NormalizeItem(IncidentTimelineItem item) => new()
    {
        ItemId = item.ItemId,
        TimestampUtc = item.TimestampUtc.ToUniversalTime(),
        Source = item.Source,
        Severity = item.Severity,
        Title = item.Title,
        Summary = item.Summary,
        ResourceRef = item.ResourceRef,
        LinkReasons = item.LinkReasons ?? [],
        Metadata = item.Metadata ?? new Dictionary<string, string?>(),
    };

    private sealed record SourceExecution(
        IncidentTimelineSourceResult Result,
        IncidentTimelineSourceStatus Status);
}