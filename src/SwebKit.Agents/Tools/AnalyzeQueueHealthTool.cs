using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Composite tool that analyzes Service Bus queue health by fetching stats and dead-letter
/// messages in parallel, then computing a plain-English health summary.
/// </summary>
public sealed class AnalyzeQueueHealthTool : IAgentTool
{
    private readonly IServiceBusClientFactory _sbFactory;
    private readonly AppStateService _appState;
    private readonly ICredentialStore _credentialStore;

    public AnalyzeQueueHealthTool(
        IServiceBusClientFactory sbFactory,
        AppStateService appState,
        ICredentialStore credentialStore)
    {
        _sbFactory = sbFactory;
        _appState = appState;
        _credentialStore = credentialStore;
    }

    public string Name => "analyze_queue_health";

    public string Description =>
        "Analyzes the health of a Service Bus queue by fetching its statistics and " +
        "dead-letter message sample in parallel. Returns a merged result with a " +
        "plain-English health_summary field (Healthy, Warning, or Critical).";

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "queue_name": { "type": "string", "description": "Service Bus queue name" },
            "namespace_alias": { "type": "string", "description": "Namespace alias configured in SwebKit (optional)" }
          },
          "required": ["queue_name"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var queueName = arguments.GetProperty("queue_name").GetString()!;
        var namespaceAlias = arguments.TryGetProperty("namespace_alias", out var nsEl)
            ? nsEl.GetString()
            : null;

        // Use DemoServiceBusClient in demo mode
        if (_appState.UseDemoData)
        {
            var demoClient = DemoServiceBusClient.OrdersDev();
            return await AnalyzeWithDemoClientAsync(queueName, demoClient, ct);
        }

        // Use the configured Service Bus namespace
        var namespaces = _appState.ServiceBusNamespaces;
        if (namespaces.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                error = "Service Bus not configured. Add a namespace in settings.",
                queue = queueName
            });
        }

        // Find the namespace to use
        ServiceBusNamespace? nsToUse = null;
        if (!string.IsNullOrWhiteSpace(namespaceAlias))
        {
            nsToUse = namespaces.FirstOrDefault(n => n.Alias.Equals(namespaceAlias, StringComparison.OrdinalIgnoreCase));
        }
        if (nsToUse == null)
        {
            nsToUse = namespaces[0];
        }

        var connectionString = _credentialStore.Get(nsToUse.CredentialKey);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return JsonSerializer.Serialize(new
            {
                error = "Service Bus connection string not available for namespace: " + nsToUse.Alias,
                queue = queueName,
                namespace_alias = nsToUse.Alias
            });
        }

        IServiceBusClient? client = null;
        try
        {
            client = _sbFactory.Create(connectionString);
            return await AnalyzeWithClientAsync(queueName, client, nsToUse, ct);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = ex.Message,
                queue = queueName,
                namespace_alias = nsToUse.Alias
            });
        }
        finally
        {
            if (client is IAsyncDisposable asyncDisp)
            {
                await asyncDisp.DisposeAsync();
            }
        }
    }

    private async Task<string> AnalyzeWithDemoClientAsync(string queueName, IServiceBusClient demoClient, CancellationToken ct)
    {
        try
        {
            var entityPath = "queues/" + queueName;

            // Fetch stats and dead-letter messages in parallel
            var statsTask = demoClient.GetEntityStatsAsync(entityPath, ct);
            var dlMessagesTask = demoClient.PeekDeadLetterAsync(entityPath, 10, ct);

            await Task.WhenAll(statsTask, dlMessagesTask);

            var stats = await statsTask;
            var dlMessages = await dlMessagesTask;

            var healthSummary = ComputeHealthSummary(stats, dlMessages.Count);

            var dlMessageList = dlMessages.Select(m => new
            {
                message_id = m.MessageId,
                correlation_id = m.CorrelationId,
                subject = m.Subject,
                content_type = m.ContentType,
                body = m.Body,
                enqueued_at = m.EnqueuedAt.ToString("o"),
                delivery_count = m.DeliveryCount,
                dead_letter_reason = m.DeadLetterReason,
                dead_letter_error = m.DeadLetterErrorDescription,
                sequence_number = m.SequenceNumber,
                session_id = m.SessionId,
                application_properties = m.ApplicationProperties
            }).ToList();

            object statsResult = stats != null
                ? new
                {
                    active_message_count = stats.ActiveMessageCount,
                    dead_letter_message_count = stats.DeadLetterMessageCount,
                    scheduled_message_count = stats.ScheduledMessageCount,
                    transfer_count = stats.TransferCount,
                    updated_at = stats.UpdatedAt?.ToString("o")
                }
                : new { error = "Stats not available" };

            return JsonSerializer.Serialize(new
            {
                queue = queueName,
                namespace_name = "demo-servicebus",
                namespace_alias = "demo",
                stats = statsResult,
                dead_letter_sample = dlMessageList,
                health_summary = healthSummary
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                queue = queueName,
                namespace_name = "demo-servicebus",
                namespace_alias = "demo",
                stats = new { error = ex.Message },
                dead_letter_sample = new { error = ex.Message },
                health_summary = "Critical"
            });
        }
    }

    private async Task<string> AnalyzeWithClientAsync(string queueName, IServiceBusClient client, ServiceBusNamespace ns, CancellationToken ct)
    {
        try
        {
            var entityPath = "queues/" + queueName;

            // Fetch stats and dead-letter messages in parallel
            var statsTask = client.GetEntityStatsAsync(entityPath, ct);
            var dlMessagesTask = client.PeekDeadLetterAsync(entityPath, 10, ct);

            await Task.WhenAll(statsTask, dlMessagesTask);

            var stats = await statsTask;
            var dlMessages = await dlMessagesTask;

            var healthSummary = ComputeHealthSummary(stats, dlMessages.Count);

            var dlMessageList = dlMessages.Select(m => new
            {
                message_id = m.MessageId,
                correlation_id = m.CorrelationId,
                subject = m.Subject,
                content_type = m.ContentType,
                body = m.Body,
                enqueued_at = m.EnqueuedAt.ToString("o"),
                delivery_count = m.DeliveryCount,
                dead_letter_reason = m.DeadLetterReason,
                dead_letter_error = m.DeadLetterErrorDescription,
                sequence_number = m.SequenceNumber,
                session_id = m.SessionId,
                application_properties = m.ApplicationProperties
            }).ToList();

            object statsResult = stats != null
                ? new
                {
                    entity_path = entityPath,
                    active_message_count = stats.ActiveMessageCount,
                    dead_letter_message_count = stats.DeadLetterMessageCount,
                    scheduled_message_count = stats.ScheduledMessageCount,
                    transfer_count = stats.TransferCount,
                    updated_at = stats.UpdatedAt?.ToString("o")
                }
                : new { error = "Stats not available" };

            return JsonSerializer.Serialize(new
            {
                queue = queueName,
                namespace_name = ns.FullyQualifiedNamespace,
                namespace_alias = ns.Alias,
                stats = statsResult,
                dead_letter_sample = dlMessageList,
                health_summary = healthSummary
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                queue = queueName,
                namespace_name = ns.FullyQualifiedNamespace,
                namespace_alias = ns.Alias,
                stats = new { error = ex.Message },
                dead_letter_sample = new { error = ex.Message },
                health_summary = "Critical"
            });
        }
    }

    private static string ComputeHealthSummary(SbEntityStats? stats, int deadLetterSampleCount)
    {
        if (stats == null)
            return "Critical";

        var activeCount = stats.ActiveMessageCount;
        var deadLetterCount = stats.DeadLetterMessageCount;

        // Critical: dead-letter count > 0 or active message count > 1000
        if (deadLetterCount > 0 || activeCount > 1000)
            return "Critical";

        // Warning: active message count > 100
        if (activeCount > 100)
            return "Warning";

        // Healthy
        return "Healthy";
    }
}
