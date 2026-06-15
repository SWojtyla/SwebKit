using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Manages a GraphQL subscription using the <c>graphql-ws</c> WebSocket subprotocol.
/// A new instance should be created per subscription; dispose it to stop the subscription.
/// </summary>
public interface IGraphQlSubscriptionService : IAsyncDisposable
{
    /// <summary>Whether the subscription is currently active.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Connects to <paramref name="endpointUrl"/>, performs the <c>graphql-ws</c> handshake, and
    /// starts the subscription described by <paramref name="request"/>.
    /// Messages are delivered via <paramref name="onMessage"/>.
    /// Completes when the server sends <c>complete</c>, the connection is lost, or
    /// <paramref name="cancellationToken"/> is cancelled.
    /// Never throws — errors are surfaced through a final <see cref="GraphQlSubscriptionMessage"/>
    /// with a non-null <see cref="GraphQlSubscriptionMessage.Errors"/> list.
    /// </summary>
    Task RunAsync(
        string endpointUrl,
        HttpRequestEntry request,
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        Func<GraphQlSubscriptionMessage, Task> onMessage,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a <c>complete</c> message and closes the WebSocket gracefully.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
