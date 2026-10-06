using System.Net;
using System.Net.Sockets;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Resolves the URLs Kestrel binds to. The release-mode Tauri launcher passes
/// <c>http://127.0.0.1:0</c> ("any free port") so two app instances never collide — which
/// also made the MCP endpoint URL unguessable for external clients (Claude Desktop etc.)
/// that need a fixed URL in their config. So a port-0 request prefers the stable
/// <see cref="StablePort"/> when it's free and only stays ephemeral when it's taken —
/// best-effort by nature: the probe releases the port before Kestrel binds it, leaving a
/// small race window, which is why the bound address is also published to
/// <c>sidecar-endpoint.json</c> (<see cref="SidecarEndpointFile"/>).
/// </summary>
public static class SidecarBindUrls
{
    /// <summary>The port every SwebKit dev flow already standardizes on.</summary>
    public const int StablePort = 5199;

    /// <summary>
    /// <paramref name="configured"/> is the raw <c>--urls</c>/<c>ASPNETCORE_URLS</c> value.
    /// An explicit fixed port is returned untouched — the dev launcher and Playwright pick
    /// theirs deliberately. <paramref name="isPortFree"/> is injectable for tests.
    /// </summary>
    public static string Resolve(string? configured, Func<int, bool>? isPortFree = null)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return $"http://127.0.0.1:{StablePort}";

        var portZeroIndex = configured.IndexOf(":0", StringComparison.Ordinal);
        if (portZeroIndex < 0 || !(isPortFree ?? IsPortFree)(StablePort))
            return configured;

        // Only the first port-0 entry is rewritten: with multiple URLs a second rewrite would
        // collide on the same preferred port at bind time.
        return string.Concat(configured.AsSpan(0, portZeroIndex), $":{StablePort}", configured.AsSpan(portZeroIndex + 2));
    }

    private static bool IsPortFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
