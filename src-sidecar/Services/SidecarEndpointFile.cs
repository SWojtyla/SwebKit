using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.Core.Configuration;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Publishes the sidecar's bound address to <c>sidecar-endpoint.json</c> under the app-data
/// root so external MCP clients (Claude Desktop, other agents) can find the current URL
/// without scraping logs — the contract for "what port is SwebKit on" whenever the stable
/// port wasn't available (<see cref="SidecarBindUrls"/>). Removed on graceful shutdown,
/// guarded by PID so one instance never deletes a still-running sibling's file.
/// Both methods are best-effort and never throw: discovery is a hint, not a precondition.
/// </summary>
public static class SidecarEndpointFile
{
    public static void Write(IServiceProvider services)
    {
        try
        {
            var addresses = services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>()?.Addresses;
            var url = addresses?.FirstOrDefault(a => a.StartsWith("http://", StringComparison.Ordinal));
            if (url is null) return;

            var path = AppDataPaths.SidecarEndpointJson;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var payload = JsonSerializer.Serialize(new
            {
                url,
                port = new Uri(url).Port,
                pid = Environment.ProcessId,
                mcpEndpoint = $"{url.TrimEnd('/')}{Acp.SwebKitToolsMcpBridge.EndpointPath}",
                startedUtc = DateTime.UtcNow,
            });

            // Temp + move so a mid-write read never hands a truncated file to a consumer
            // (the same atomicity the JSON stores use — see pitfalls CS-4).
            var temp = path + ".tmp";
            File.WriteAllText(temp, payload);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            // A discovery hint must never take startup down.
        }
    }

    public static void Delete()
    {
        try
        {
            var path = AppDataPaths.SidecarEndpointJson;
            if (!File.Exists(path)) return;

            // A second sidecar overwrites the file with its own PID; deleting unconditionally
            // would orphan the still-running sibling. An unreadable/foreign file is left alone.
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("pid", out var pid) && pid.GetInt32() == Environment.ProcessId)
                File.Delete(path);
        }
        catch
        {
        }
    }
}
