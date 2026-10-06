using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using SwebKit.Core.Configuration;
using SwebKit.Sidecar.Services;
using SwebKit.Sidecar.Services.Acp;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Covers the release-mode discovery contract: <see cref="SidecarBindUrls"/> preferring the
/// stable port when the Tauri spawn asks for an ephemeral one, and
/// <see cref="SidecarEndpointFile"/> publishing/cleaning up the bound address so external
/// MCP clients can find the sidecar without scraping logs.
/// </summary>
public sealed class SidecarEndpointDiscoveryTests
{
    // ── SidecarBindUrls.Resolve ──────────────────────────────────────────────

    [Fact]
    public void Resolve_NullConfigured_ReturnsStablePort()
    {
        Assert.Equal($"http://127.0.0.1:{SidecarBindUrls.StablePort}", SidecarBindUrls.Resolve(null));
    }

    [Fact]
    public void Resolve_FixedPort_IsReturnedUnchanged()
    {
        // The dev launcher and Playwright deliberately pick ports — even when the stable
        // port is free, an explicit value must win.
        Assert.Equal("http://127.0.0.1:5999", SidecarBindUrls.Resolve("http://127.0.0.1:5999", _ => true));
    }

    [Fact]
    public void Resolve_PortZero_ReturnsStablePortWhenFree()
    {
        Assert.Equal(
            $"http://127.0.0.1:{SidecarBindUrls.StablePort}",
            SidecarBindUrls.Resolve("http://127.0.0.1:0", _ => true));
    }

    [Fact]
    public void Resolve_PortZero_KeepsEphemeralWhenStablePortTaken()
    {
        Assert.Equal("http://127.0.0.1:0", SidecarBindUrls.Resolve("http://127.0.0.1:0", _ => false));
    }

    [Fact]
    public void Resolve_MultiplePortZeroUrls_RewritesOnlyTheFirst()
    {
        // A second rewrite would collide with the first on the same preferred port at bind time.
        Assert.Equal(
            $"http://127.0.0.1:{SidecarBindUrls.StablePort};http://127.0.0.1:0",
            SidecarBindUrls.Resolve("http://127.0.0.1:0;http://127.0.0.1:0", _ => true));
    }

    // ── SidecarEndpointFile ──────────────────────────────────────────────────

    /// <summary>Minimal <see cref="IServer"/> stub carrying a bound address in its features,
    /// the same shape Kestrel exposes after startup.</summary>
    private sealed class StubServer : IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();

        public static StubServer ListeningAt(string address)
        {
            var server = new StubServer();
            server.Features.Set<IServerAddressesFeature>(new ServerAddressesFeature());
            server.Features.Get<IServerAddressesFeature>()!.Addresses.Add(address);
            return server;
        }

        public void Dispose() { }
        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) where TContext : notnull => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubServices(object? server) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IServer) ? server : null;
    }

    [Fact]
    public void Write_PublishesBoundAddressPortPidAndMcpEndpoint()
    {
        using var sandbox = new AppDataSandbox();
        var services = new StubServices(StubServer.ListeningAt("http://127.0.0.1:5123"));

        SidecarEndpointFile.Write(services);

        var path = AppDataPaths.SidecarEndpointJson;
        Assert.True(File.Exists(path));
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("http://127.0.0.1:5123", root.GetProperty("url").GetString());
        Assert.Equal(5123, root.GetProperty("port").GetInt32());
        Assert.Equal(Environment.ProcessId, root.GetProperty("pid").GetInt32());
        Assert.Equal(
            $"http://127.0.0.1:5123{SwebKitToolsMcpBridge.EndpointPath}",
            root.GetProperty("mcpEndpoint").GetString());
    }

    [Fact]
    public void Write_WithoutBoundAddress_LeavesNoFile()
    {
        using var sandbox = new AppDataSandbox();

        SidecarEndpointFile.Write(new StubServices(server: null));

        Assert.False(File.Exists(AppDataPaths.SidecarEndpointJson));
    }

    [Fact]
    public void Delete_RemovesFileWrittenByThisProcess()
    {
        using var sandbox = new AppDataSandbox();
        SidecarEndpointFile.Write(new StubServices(StubServer.ListeningAt("http://127.0.0.1:5123")));

        SidecarEndpointFile.Delete();

        Assert.False(File.Exists(AppDataPaths.SidecarEndpointJson));
    }

    [Fact]
    public void Delete_LeavesAFileOwnedByAnotherPid()
    {
        using var sandbox = new AppDataSandbox();
        // Simulate a second sidecar instance's file: any PID that isn't ours.
        var foreignPid = Environment.ProcessId == 1 ? 2 : 1;
        File.WriteAllText(
            AppDataPaths.SidecarEndpointJson,
            JsonSerializer.Serialize(new { url = "http://127.0.0.1:5123", pid = foreignPid }));

        SidecarEndpointFile.Delete();

        Assert.True(File.Exists(AppDataPaths.SidecarEndpointJson));
    }
}
