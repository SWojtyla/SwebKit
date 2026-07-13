using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Azure.ServiceBus.IncidentTimeline;

public sealed class ServiceBusEvidenceSignalSource : IIncidentTimelineSignalSource
{
    private readonly AppStateService _appState;
    private readonly IServiceBusNamespaceBootstrapper _bootstrapper;

    public ServiceBusEvidenceSignalSource(AppStateService appState, IServiceBusNamespaceBootstrapper bootstrapper)
    {
        _appState = appState;
        _bootstrapper = bootstrapper;
    }

    public IncidentTimelineSource Source => IncidentTimelineSource.ServiceBus;

    public async Task<IncidentTimelineSourceResult> FetchAsync(IncidentTimelineQuery query, CancellationToken ct = default)
    {
        var mapping = _appState.Config.IncidentTimeline.FindWorkloadMapping(query.Scope);
        if (mapping is null || mapping.ServiceBusEntities.Count == 0)
        {
            return IncidentTimelineSourceResult.Unmapped(Source, "No Service Bus entity mapping exists for the selected workload.");
        }

        var items = new List<IncidentTimelineItem>();
        var errors = new List<string>();
        var configuredNamespaces = _appState.ServiceBusNamespaces.ToDictionary(static ns => ns.Id);

        foreach (var bindingGroup in mapping.ServiceBusEntities.GroupBy(static binding => binding.NamespaceId))
        {
            if (!configuredNamespaces.TryGetValue(bindingGroup.Key, out var serviceBusNamespace))
            {
                errors.Add($"Mapped namespace {bindingGroup.Key} is not configured.");
                continue;
            }

            var connection = await _bootstrapper.ConnectAsync(serviceBusNamespace, ct).ConfigureAwait(false);
            if (connection.Client is null)
            {
                errors.Add(connection.ConnectionError ?? $"Failed to connect to namespace {serviceBusNamespace.Alias}.");
                continue;
            }

            try
            {
                foreach (var binding in bindingGroup)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        items.AddRange(await BuildEntityItemsAsync(query, serviceBusNamespace, binding, connection.Client, ct).ConfigureAwait(false));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{serviceBusNamespace.Alias}/{binding.EntityPath}: {ex.Message}");
                    }
                }
            }
            finally
            {
                if (connection.Client is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        var cappedItems = items
            .OrderByDescending(static item => item.TimestampUtc)
            .ThenBy(static item => item.ItemId, StringComparer.Ordinal)
            .ToList();
        var wasTruncated = cappedItems.Count > query.GetMaxItemsPerSource();
        if (wasTruncated)
        {
            cappedItems = cappedItems.Take(query.GetMaxItemsPerSource()).ToList();
        }

        if (cappedItems.Count == 0 && errors.Count == 0)
        {
            return IncidentTimelineSourceResult.Loaded(Source, [], statusMessage: "No mapped Service Bus symptoms were visible inside the selected window.");
        }

        if (cappedItems.Count == 0)
        {
            return IncidentTimelineSourceResult.Failed(Source, string.Join(" ", errors));
        }

        return errors.Count == 0
            ? IncidentTimelineSourceResult.Loaded(Source, cappedItems, wasTruncated)
            : IncidentTimelineSourceResult.Partial(
                Source,
                cappedItems,
                string.Join(" ", errors),
                wasTruncated,
                "Some mapped Service Bus entities could not be loaded.");
    }

    private static async Task<IReadOnlyList<IncidentTimelineItem>> BuildEntityItemsAsync(
        IncidentTimelineQuery query,
        ServiceBusNamespace serviceBusNamespace,
        SbEntityLink binding,
        IServiceBusClient client,
        CancellationToken ct)
    {
        var items = new List<IncidentTimelineItem>();
        var window = query.GetUtcWindow();
        var entityLabel = string.IsNullOrWhiteSpace(binding.Alias) ? binding.EntityPath : binding.Alias;
        var stats = await client.GetEntityStatsAsync(binding.EntityPath, ct).ConfigureAwait(false);

        IReadOnlyList<SbMessage> deadLetterMessages = [];
        try
        {
            deadLetterMessages = await client.PeekDeadLetterAsync(binding.EntityPath, 25, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            deadLetterMessages = [];
        }

        var windowDeadLetters = deadLetterMessages
            .Where(message => IsInWindow(message.EnqueuedAt, window))
            .OrderByDescending(static message => message.EnqueuedAt)
            .ToList();

        if (windowDeadLetters.Count > 0)
        {
            var latest = windowDeadLetters[0];
            items.Add(new IncidentTimelineItem
            {
                ItemId = $"sb:dlq:{serviceBusNamespace.Id}:{binding.EntityPath}:{latest.SequenceNumber ?? 0}",
                TimestampUtc = latest.EnqueuedAt.ToUniversalTime(),
                Source = IncidentTimelineSource.ServiceBus,
                Severity = IncidentTimelineSeverity.Error,
                Title = $"Service Bus DLQ activity on {entityLabel}",
                Summary = $"{windowDeadLetters.Count} dead-lettered message(s) were visible for the mapped entity inside the selected window. Latest reason: {latest.DeadLetterReason ?? "Unknown"}.",
                ResourceRef = new IncidentResourceRef("ServiceBusEntity", entityLabel, query.Scope.Namespace, serviceBusNamespace.Alias),
                LinkReasons =
                [
                    CreateDirectReason(query.Scope, $"Linked because Service Bus entity {binding.EntityPath} is explicitly mapped to the selected workload and showed dead-letter activity inside the selected window.")
                ],
                Metadata = new Dictionary<string, string?>
                {
                    ["namespaceAlias"] = serviceBusNamespace.Alias,
                    ["entityPath"] = binding.EntityPath,
                    ["deadLetterCount"] = windowDeadLetters.Count.ToString(),
                    ["latestDeadLetterReason"] = latest.DeadLetterReason,
                    ["latestCorrelationId"] = latest.CorrelationId,
                },
            });
        }

        IReadOnlyList<SbMessage> activeMessages = [];
        try
        {
            activeMessages = await client.PeekMessagesAsync(binding.EntityPath, 25, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            activeMessages = [];
        }

        var windowActiveMessages = activeMessages
            .Where(message => IsInWindow(message.EnqueuedAt, window))
            .OrderByDescending(static message => message.EnqueuedAt)
            .ToList();

        if (windowActiveMessages.Count > 0 && stats.ActiveMessageCount > 0)
        {
            var latest = windowActiveMessages[0];
            items.Add(new IncidentTimelineItem
            {
                ItemId = $"sb:active:{serviceBusNamespace.Id}:{binding.EntityPath}:{latest.SequenceNumber ?? 0}",
                TimestampUtc = latest.EnqueuedAt.ToUniversalTime(),
                Source = IncidentTimelineSource.ServiceBus,
                Severity = IncidentTimelineSeverity.Warning,
                Title = $"Service Bus backlog visible on {entityLabel}",
                Summary = $"The mapped entity reported {stats.ActiveMessageCount} active message(s); {windowActiveMessages.Count} sampled message(s) were enqueued inside the selected window.",
                ResourceRef = new IncidentResourceRef("ServiceBusEntity", entityLabel, query.Scope.Namespace, serviceBusNamespace.Alias),
                LinkReasons =
                [
                    CreateDirectReason(query.Scope, $"Linked because Service Bus entity {binding.EntityPath} is explicitly mapped to the selected workload and had active messages enqueued inside the selected window.")
                ],
                Metadata = new Dictionary<string, string?>
                {
                    ["namespaceAlias"] = serviceBusNamespace.Alias,
                    ["entityPath"] = binding.EntityPath,
                    ["activeMessageCount"] = stats.ActiveMessageCount.ToString(),
                    ["sampledMessagesInWindow"] = windowActiveMessages.Count.ToString(),
                    ["latestCorrelationId"] = latest.CorrelationId,
                },
            });
        }

        if (items.Count == 0
            && stats.UpdatedAt is { } statsUpdatedAt
            && IsInWindow(statsUpdatedAt, window)
            && (stats.ActiveMessageCount > 0 || stats.DeadLetterMessageCount > 0))
        {
            items.Add(new IncidentTimelineItem
            {
                ItemId = $"sb:stats:{serviceBusNamespace.Id}:{binding.EntityPath}:{statsUpdatedAt.UtcTicks}",
                TimestampUtc = statsUpdatedAt.ToUniversalTime(),
                Source = IncidentTimelineSource.ServiceBus,
                Severity = stats.DeadLetterMessageCount > 0 ? IncidentTimelineSeverity.Error : IncidentTimelineSeverity.Warning,
                Title = $"Service Bus entity pressure on {entityLabel}",
                Summary = $"Runtime properties reported {stats.ActiveMessageCount} active and {stats.DeadLetterMessageCount} dead-letter messages for the mapped entity during the selected window.",
                ResourceRef = new IncidentResourceRef("ServiceBusEntity", entityLabel, query.Scope.Namespace, serviceBusNamespace.Alias),
                LinkReasons =
                [
                    CreateDirectReason(query.Scope, $"Linked because Service Bus entity {binding.EntityPath} is explicitly mapped to the selected workload and its runtime properties changed inside the selected window.")
                ],
                Metadata = new Dictionary<string, string?>
                {
                    ["namespaceAlias"] = serviceBusNamespace.Alias,
                    ["entityPath"] = binding.EntityPath,
                    ["activeMessageCount"] = stats.ActiveMessageCount.ToString(),
                    ["deadLetterMessageCount"] = stats.DeadLetterMessageCount.ToString(),
                },
            });
        }

        return items;
    }

    private static IncidentLinkReason CreateDirectReason(IncidentWorkloadScope scope, string explanation) =>
        new(IncidentLinkReasonType.Topology, IncidentLinkRelevance.Direct, explanation.Replace("selected workload", $"selected {scope.WorkloadKind} {scope.WorkloadName}"));

    private static bool IsInWindow(DateTimeOffset timestamp, TimeRange window)
    {
        var utcTimestamp = timestamp.ToUniversalTime();
        return utcTimestamp >= window.Start && utcTimestamp <= window.End;
    }
}