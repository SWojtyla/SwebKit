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
        _ => "Connection failed",
    };
}
