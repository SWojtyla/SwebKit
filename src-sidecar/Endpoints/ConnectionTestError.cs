namespace SwebKit.Sidecar.Endpoints;

/// <summary>
/// Maps an exception from a connection-test/probe endpoint (AKS/Redis/Service Bus/Storage "test
/// connection" and context-switch handlers) to a message safe to return directly to the client.
/// The underlying SDK exception can contain connection strings, kubeconfig paths, resource IDs, or
/// other detail that shouldn't reach the browser for what's ultimately just a boolean
/// connected/not-connected signal — callers should log the real exception server-side via
/// <see cref="Microsoft.Extensions.Logging.ILogger"/> and return only this classification in the
/// response body.
/// </summary>
internal static class ConnectionTestError
{
    public static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Authentication failed",
        TimeoutException or OperationCanceledException => "Connection timed out",
        System.Net.Sockets.SocketException => "Could not reach the server",
        // ServiceBusException.Message embeds the entity path and AMQP detail — return the
        // classified reason instead, phrased so the user knows what to do next. (global:: because
        // SwebKit.Azure shadows the Azure root namespace.)
        global::Azure.Messaging.ServiceBus.ServiceBusException sb => sb.Reason switch
        {
            global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.ServiceTimeout
                or global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.ServiceBusy
                or global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.ServiceCommunicationProblem =>
                "Service Bus is busy or timed out — try again in a moment",
            global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.MessagingEntityNotFound =>
                "Queue or subscription not found — it may have been renamed or deleted",
            global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.MessagingEntityDisabled =>
                "The queue or subscription is disabled",
            global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.QuotaExceeded =>
                "Service Bus quota exceeded",
            _ => "Service Bus request failed",
        },
        _ => "Connection failed",
    };
}
