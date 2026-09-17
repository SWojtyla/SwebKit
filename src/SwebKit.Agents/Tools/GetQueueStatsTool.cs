using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Retrieves queue statistics including message counts, error rates, and basic metadata.
/// </summary>
public sealed class GetQueueStatsTool : IAgentTool
{
    private readonly IServiceBusConnectionPool _pool;
    private readonly AppStateService _appState;

    public GetQueueStatsTool(IServiceBusConnectionPool pool, AppStateService appState)
    {
        _pool = pool;
        _appState = appState;
    }

    public string Name => "get_queue_stats";

    public string Description =>
        "Returns statistics for a Service Bus queue including active message count, dead-letter count, " +
        "scheduled message count, and last update time. If no queue is specified, returns all queues. " +
        "Omit namespace to use the namespace selected in the UI.";

    public FeatureArea FeatureArea => FeatureArea.ServiceBus;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "queue_name": {
              "type": "string",
              "description": "Name of the specific queue to get stats for. If omitted, returns stats for all queues."
            },
            "namespace": {
              "type": "string",
              "description": "Configured namespace alias, FQDN, or id. Omit to use the namespace selected in the UI."
            }
          },
          "required": []
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        // Use DemoServiceBusClient in demo mode
        if (_appState.UseDemoData)
        {
            var demoClient = DemoServiceBusClient.OrdersDev();
            return await GetStatsFromDemoClientAsync(arguments, demoClient, ct);
        }

        var requested = arguments.TryGetProperty("namespace", out var nsEl) ? nsEl.GetString() : null;
        var resolution = ServiceBusToolContext.ResolveNamespace(_appState, requested);
        if (!resolution.IsSuccess)
            return JsonSerializer.Serialize(new { error = resolution.Error });

        var ns = resolution.Namespace!;
        var queueName = arguments.TryGetProperty("queue_name", out var qnEl) ? qnEl.GetString() : null;
        try
        {
            var client = _pool.GetOrCreate(ns);
            if (string.IsNullOrWhiteSpace(queueName))
            {
                var queues = await client.ListQueuesAsync(ct);
                var queueStats = new List<object>();
                foreach (var queue in queues)
                {
                    var stats = await client.GetEntityStatsAsync(queue.EntityPath, ct);
                    queueStats.Add(new
                    {
                        queue_name = queue.Name,
                        entity_path = queue.EntityPath,
                        active_message_count = stats?.ActiveMessageCount ?? 0,
                        dead_letter_message_count = stats?.DeadLetterMessageCount ?? 0,
                        scheduled_message_count = stats?.ScheduledMessageCount ?? 0,
                        transfer_count = stats?.TransferCount ?? 0,
                        is_disabled = queue.IsDisabled,
                        updated_at = stats?.UpdatedAt?.ToString("o")
                    });
                }
                return JsonSerializer.Serialize(new
                {
                    namespace_name = ns.FullyQualifiedNamespace,
                    namespace_alias = ns.Alias,
                    queue_count = queueStats.Count,
                    queues = queueStats
                });
            }

            var entityPath = "queues/" + queueName;
            var entityStats = await client.GetEntityStatsAsync(entityPath, ct);
            if (entityStats is null)
                return JsonSerializer.Serialize(new { error = "Queue not found: " + queueName, queue_name = queueName });

            return JsonSerializer.Serialize(new
            {
                namespace_name = ns.FullyQualifiedNamespace,
                namespace_alias = ns.Alias,
                queue_name = queueName,
                entity_path = entityPath,
                active_message_count = entityStats.ActiveMessageCount,
                dead_letter_message_count = entityStats.DeadLetterMessageCount,
                scheduled_message_count = entityStats.ScheduledMessageCount,
                transfer_count = entityStats.TransferCount,
                updated_at = entityStats.UpdatedAt?.ToString("o")
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, queue_name = queueName });
        }
    }

    private async Task<string> GetStatsFromDemoClientAsync(JsonElement arguments, IServiceBusClient demoClient, CancellationToken ct)
    {
        var queueName = arguments.TryGetProperty("queue_name", out var qnEl)
            ? qnEl.GetString()
            : null;

        try
        {
            if (string.IsNullOrWhiteSpace(queueName))
            {
                // Get stats for all queues
                var queues = await demoClient.ListQueuesAsync(ct);
                var queueStats = new List<object>();

                foreach (var queue in queues)
                {
                    var stats = await demoClient.GetEntityStatsAsync(queue.EntityPath, ct);
                    queueStats.Add(new
                    {
                        queue_name = queue.Name,
                        entity_path = queue.EntityPath,
                        active_message_count = stats?.ActiveMessageCount ?? 0,
                        dead_letter_message_count = stats?.DeadLetterMessageCount ?? 0,
                        scheduled_message_count = stats?.ScheduledMessageCount ?? 0,
                        transfer_count = stats?.TransferCount ?? 0,
                        is_disabled = queue.IsDisabled,
                        updated_at = stats?.UpdatedAt?.ToString("o")
                    });
                }

                return JsonSerializer.Serialize(new
                {
                    namespace_name = "demo-servicebus",
                    namespace_alias = "demo",
                    queue_count = queueStats.Count,
                    queues = queueStats
                });
            }
            else
            {
                // Get stats for specific queue
                var entityPath = "queues/" + queueName;
                var stats = await demoClient.GetEntityStatsAsync(entityPath, ct);

                if (stats == null)
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = "Queue not found",
                        queue_name = queueName
                    });
                }

                return JsonSerializer.Serialize(new
                {
                    namespace_name = "demo-servicebus",
                    namespace_alias = "demo",
                    queue_name = queueName,
                    entity_path = entityPath,
                    active_message_count = stats.ActiveMessageCount,
                    dead_letter_message_count = stats.DeadLetterMessageCount,
                    scheduled_message_count = stats.ScheduledMessageCount,
                    transfer_count = stats.TransferCount,
                    updated_at = stats.UpdatedAt?.ToString("o")
                });
            }
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, queue_name = queueName });
        }
    }
}