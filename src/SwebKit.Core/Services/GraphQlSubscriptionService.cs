using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Runs a GraphQL subscription using the <c>graphql-ws</c> protocol over a WebSocket connection.
/// Reference: https://github.com/enisdenjo/graphql-ws/blob/master/PROTOCOL.md
/// </summary>
public sealed class GraphQlSubscriptionService(
    IVariableSubstitutionService substitution,
    Func<IWebSocketClientService> webSocketFactory) : IGraphQlSubscriptionService
{
    private const string SubProtocol = "graphql-ws";
    private const string SubscriptionId = "sub-1";

    private IWebSocketClientService? _ws;
    private bool _active;

    public bool IsActive => _active;

    public async Task RunAsync(
        string endpointUrl,
        HttpRequestEntry request,
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        Func<GraphQlSubscriptionMessage, Task> onMessage,
        CancellationToken cancellationToken = default)
    {
        var scope = substitution.BuildScope(collection.Variables, activeEnvironment);
        var resolvedUrl = substitution.Substitute(endpointUrl, scope);

        // Convert HTTP(S) URL to WS(S)
        var wsUrl = resolvedUrl
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase);

        // Dispose any previous WebSocket from a prior run before creating a new one
        if (_ws is not null)
            await _ws.DisposeAsync();
        _ws = webSocketFactory();
        _active = true;

        try
        {
            // Build auth headers to pass as WebSocket upgrade headers
            var upgradeHeaders = new List<(string Name, string Value)>();
            foreach (var h in request.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)))
            {
                upgradeHeaders.Add((h.Key, substitution.Substitute(h.Value ?? string.Empty, scope)));
            }

            await _ws.ConnectAsync(wsUrl, upgradeHeaders, SubProtocol, cancellationToken);

            // ── graphql-ws handshake ─────────────────────────────────────────
            // 1. Send connection_init
            await _ws.SendTextAsync("""{"type":"connection_init"}""", cancellationToken);

            // 2. Wait for connection_ack
            var ackMsg = await _ws.ReadAsync(cancellationToken);
            var ack = ackMsg?.Content;
            if (ack is null)
            {
                await DeliverErrorAsync(onMessage, "Server closed the connection before sending connection_ack.");
                return;
            }

            var ackType = ParseMessageType(ack);
            if (ackType != "connection_ack")
            {
                await DeliverErrorAsync(onMessage, $"Expected connection_ack, received: {ackType ?? "<null>"}");
                return;
            }

            // 3. Send subscribe message
            var query = request.GraphQlQuery ?? string.Empty;
            var variables = ParseVariablesOrNull(request.GraphQlVariables);
            var operationName = string.IsNullOrWhiteSpace(request.GraphQlSelectedOperation)
                ? null
                : request.GraphQlSelectedOperation;

            var subscribeMsg = JsonSerializer.Serialize(new
            {
                id = SubscriptionId,
                type = "subscribe",
                payload = new
                {
                    query,
                    variables,
                    operationName,
                },
            });

            await _ws.SendTextAsync(subscribeMsg, cancellationToken);

            // 4. Receive next/error/complete frames
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await _ws.ReadAsync(cancellationToken);
                if (frame is null) break; // connection closed

                var raw = frame.Content;

                var msgType = ParseMessageType(raw);
                switch (msgType)
                {
                    case "next":
                        var msg = ParseNextMessage(raw);
                        if (msg is not null)
                            await onMessage(msg);
                        break;

                    case "error":
                        var errors = ParseErrors(raw);
                        await onMessage(new GraphQlSubscriptionMessage
                        {
                            Payload = raw,
                            Errors = errors,
                        });
                        return;

                    case "complete":
                        return;

                    // ping/pong — respond if needed
                    case "ping":
                        await _ws.SendTextAsync("""{"type":"pong"}""", cancellationToken);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { /* normal stop */ }
        catch (Exception ex)
        {
            await DeliverErrorAsync(onMessage, ex.Message);
        }
        finally
        {
            _active = false;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _active = false;
        if (_ws is null) return;
        try
        {
            if (_ws.State == WebSocketConnectionState.Connected)
            {
                // Send complete frame
                var completeMsg = JsonSerializer.Serialize(new { id = SubscriptionId, type = "complete" });
                await _ws.SendTextAsync(completeMsg, cancellationToken);
                await _ws.CloseAsync(cancellationToken);
            }
        }
        catch { /* ignore stop errors */ }
    }

    public async ValueTask DisposeAsync()
    {
        _active = false;
        if (_ws is not null)
            await _ws.DisposeAsync();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? ParseMessageType(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var type))
                return type.GetString();
        }
        catch { /* invalid JSON */ }
        return null;
    }

    private static GraphQlSubscriptionMessage? ParseNextMessage(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var payload = node?["payload"];
            if (payload is null) return null;

            var payloadJson = payload.ToJsonString();
            var errorsNode = payload["errors"];
            IReadOnlyList<GraphQlError>? errors = null;
            if (errorsNode is JsonArray arr)
                errors = ParseErrorsFromArray(arr);

            return new GraphQlSubscriptionMessage
            {
                Payload = payloadJson,
                Errors = errors,
            };
        }
        catch
        {
            return new GraphQlSubscriptionMessage { Payload = json };
        }
    }

    private static IReadOnlyList<GraphQlError> ParseErrors(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var payload = node?["payload"];
            if (payload is JsonArray arr)
                return ParseErrorsFromArray(arr);
        }
        catch { /* ignore */ }
        return [];
    }

    private static IReadOnlyList<GraphQlError> ParseErrorsFromArray(JsonArray arr)
    {
        var errors = new List<GraphQlError>();
        foreach (var item in arr)
        {
            var message = item?["message"]?.GetValue<string>() ?? "Unknown GraphQL error";
            errors.Add(new GraphQlError { Message = message });
        }
        return errors;
    }

    private static object? ParseVariablesOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json); }
        catch { return null; }
    }

    private static Task DeliverErrorAsync(Func<GraphQlSubscriptionMessage, Task> onMessage, string errorMsg) =>
        onMessage(new GraphQlSubscriptionMessage
        {
            Payload = string.Empty,
            Errors = [new GraphQlError { Message = errorMsg }],
        });
}
