using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Retrieves messages from a Service Bus queue or dead-letter queue.
/// </summary>
public sealed class GetQueueMessagesTool : IAgentTool
{
    private readonly IServiceBusConnectionPool _pool;
    private readonly AppStateService _appState;

    public GetQueueMessagesTool(IServiceBusConnectionPool pool, AppStateService appState)
    {
        _pool = pool;
        _appState = appState;
    }

    public string Name => "get_queue_messages";

    public string Description =>
        "Retrieves messages from a Service Bus queue, including dead-letter messages. " +
        "Omit namespace to use the namespace selected in the UI.";

    public FeatureArea FeatureArea => FeatureArea.ServiceBus;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "queue_name": {
              "type": "string",
              "description": "Name of the queue"
            },
            "count": {
              "type": "integer",
              "description": "Number of messages to retrieve (default: 10, max: 100)",
              "minimum": 1,
              "maximum": 100
            },
            "peek_dead_letter": {
              "type": "boolean",
              "description": "If true, retrieves messages from the dead-letter queue instead of the main queue (default: false)"
            },
            "namespace": {
              "type": "string",
              "description": "Configured namespace alias, FQDN, or id. Omit to use the namespace selected in the UI."
            }
          },
          "required": ["queue_name"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        // Use DemoServiceBusClient in demo mode
        if (_appState.UseDemoData)
        {
            var demoClient = DemoServiceBusClient.OrdersDev();
            return await GetMessagesFromDemoClientAsync(arguments, demoClient, ct);
        }

        var requested = arguments.TryGetProperty("namespace", out var nsEl) ? nsEl.GetString() : null;
        var resolution = ServiceBusToolContext.ResolveNamespace(_appState, requested);
        if (!resolution.IsSuccess)
            return JsonSerializer.Serialize(new { error = resolution.Error });

        var ns = resolution.Namespace!;
        var queueName = arguments.GetProperty("queue_name").GetString()!;
        var count = arguments.TryGetProperty("count", out var countEl) && countEl.TryGetInt32(out var c)
            ? Math.Clamp(c, 1, 100)
            : 10;
        var peekDeadLetter = arguments.TryGetProperty("peek_dead_letter", out var dlEl) && dlEl.GetBoolean();

        try
        {
            var client = _pool.GetOrCreate(ns);
            var entityPath = "queues/" + queueName;
            var messages = peekDeadLetter
                ? await client.PeekDeadLetterAsync(entityPath, count, ct)
                : await client.PeekMessagesAsync(entityPath, count, ct);
            var messageList = messages.Select(ServiceBusToolProjections.Message).ToList();

            return JsonSerializer.Serialize(new
            {
                namespace_name = ns.FullyQualifiedNamespace,
                namespace_alias = ns.Alias,
                queue_name = queueName,
                peek_dead_letter = peekDeadLetter,
                messages_returned = messageList.Count,
                messages = messageList
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = ex.Message,
                queue_name = queueName,
                peek_dead_letter = peekDeadLetter
            });
        }
    }

    private async Task<string> GetMessagesFromDemoClientAsync(JsonElement arguments, IServiceBusClient demoClient, CancellationToken ct)
    {
        var queueName = arguments.GetProperty("queue_name").GetString()!;
        var count = arguments.TryGetProperty("count", out var countEl) && countEl.TryGetInt32(out var c)
            ? Math.Clamp(c, 1, 100)
            : 10;

        var peekDeadLetter = arguments.TryGetProperty("peek_dead_letter", out var dlEl)
            ? dlEl.GetBoolean()
            : false;

        try
        {
            var entityPath = "queues/" + queueName;

            IReadOnlyList<SbMessage> messages;
            if (peekDeadLetter)
            {
                messages = await demoClient.PeekDeadLetterAsync(entityPath, count, ct);
            }
            else
            {
                messages = await demoClient.PeekMessagesAsync(entityPath, count, ct);
            }

            var messageList = messages.Select(ServiceBusToolProjections.Message).ToList();

            return JsonSerializer.Serialize(new
            {
                namespace_name = "demo-servicebus",
                namespace_alias = "demo",
                queue_name = queueName,
                peek_dead_letter = peekDeadLetter,
                messages_returned = messageList.Count,
                messages = messageList
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = ex.Message,
                queue_name = queueName,
                peek_dead_letter = peekDeadLetter
            });
        }
    }
}