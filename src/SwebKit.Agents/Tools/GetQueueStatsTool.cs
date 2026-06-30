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
    private readonly IServiceBusClientFactory _sbFactory;
    private readonly AppStateService _appState;
    private readonly ICredentialStore _credentialStore;

    public GetQueueStatsTool(
        IServiceBusClientFactory sbFactory,
        AppStateService appState,
        ICredentialStore credentialStore)
    {
        _sbFactory = sbFactory;
        _appState = appState;
        _credentialStore = credentialStore;
    }

    public string Name => "get_queue_stats";

    public string Description =>
        "Returns statistics for a Service Bus queue including active message count, dead-letter count, " +
        "scheduled message count, and last update time. If no queue specified, returns stats for all queues.";

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "queue_name": {
              "type": "string",
              "description": "Name of the specific queue to get stats for. If omitted, returns stats for all queues."
            }
          },
          "required": []
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        // Use the first configured Service Bus namespace
        var namespaces = _appState.ServiceBusNamespaces;
        if (namespaces.Count == 0)
        {
            return JsonSerializer.Serialize(new { error = "Service Bus not configured. Add a namespace in settings." });
        }

        var ns = namespaces[0];
        var connectionString = _credentialStore.Get(ns.CredentialKey);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return JsonSerializer.Serialize(new
            {
                error = "Service Bus connection string not available for namespace: " + ns.Alias
            });
        }

        var queueName = arguments.TryGetProperty("queue_name", out var qnEl)
            ? qnEl.GetString()
            : null;

        IServiceBusClient? client = null;
        try
        {
            client = _sbFactory.Create(connectionString);

            if (string.IsNullOrWhiteSpace(queueName))
            {
                // Get stats for all queues
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
            else
            {
                // Get stats for specific queue
                var entityPath = "queues/" + queueName;
                var stats = await client.GetEntityStatsAsync(entityPath, ct);

                if (stats == null)
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = $"Queue '{queueName}' not found",
                        queue_name = queueName
                    });
                }

                return JsonSerializer.Serialize(new
                {
                    namespace_name = ns.FullyQualifiedNamespace,
                    namespace_alias = ns.Alias,
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
        finally
        {
            // IServiceBusClient implements IAsyncDisposable, not IDisposable
            // so we don't use 'using' but we should still dispose it
            if (client is IAsyncDisposable asyncDisp)
            {
                await asyncDisp.DisposeAsync();
            }
        }
    }
}
