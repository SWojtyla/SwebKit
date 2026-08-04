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
    private readonly IServiceBusClientFactory _sbFactory;
    private readonly AppStateService _appState;
    private readonly ICredentialStore _credentialStore;

    public GetQueueMessagesTool(
        IServiceBusClientFactory sbFactory,
        AppStateService appState,
        ICredentialStore credentialStore)
    {
        _sbFactory = sbFactory;
        _appState = appState;
        _credentialStore = credentialStore;
    }

    public string Name => "get_queue_messages";

    public string Description =>
        "Retrieves messages from a Service Bus queue. Supports filtering by message count and optionally peeking at dead-letter messages.";

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

        var queueName = arguments.GetProperty("queue_name").GetString()!;
        var count = arguments.TryGetProperty("count", out var countEl) && countEl.TryGetInt32(out var c)
            ? Math.Clamp(c, 1, 100)
            : 10;

        var peekDeadLetter = arguments.TryGetProperty("peek_dead_letter", out var dlEl)
            ? dlEl.GetBoolean()
            : false;

        IServiceBusClient? client = null;
        try
        {
            client = _sbFactory.Create(connectionString);
            var entityPath = "queues/" + queueName;

            IReadOnlyList<SbMessage> messages;
            if (peekDeadLetter)
            {
                messages = await client.PeekDeadLetterAsync(entityPath, count, ct);
            }
            else
            {
                messages = await client.PeekMessagesAsync(entityPath, count, ct);
            }

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
        finally
        {
            // IServiceBusClient implements IAsyncDisposable, not IDisposable
            if (client is IAsyncDisposable asyncDisp)
            {
                await asyncDisp.DisposeAsync();
            }
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