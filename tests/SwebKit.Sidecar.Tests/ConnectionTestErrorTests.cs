using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

public class ConnectionTestErrorTests
{
    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), "Authentication failed")]
    [InlineData(typeof(TimeoutException), "Connection timed out")]
    [InlineData(typeof(OperationCanceledException), "Connection timed out")]
    [InlineData(typeof(InvalidOperationException), "Connection failed")]
    public void Describe_MapsKnownExceptionTypes_ToGenericSafeMessages(Type exceptionType, string expected)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType, "some sensitive connection-string detail")!;

        var message = ConnectionTestError.Describe(ex);

        Assert.Equal(expected, message);
        Assert.DoesNotContain("sensitive", message);
    }

    [Fact]
    public void Describe_SocketException_ReturnsUnreachableMessage()
    {
        var ex = new System.Net.Sockets.SocketException();

        var message = ConnectionTestError.Describe(ex);

        Assert.Equal("Could not reach the server", message);
    }
}

/// <summary>Simulates a real client failure whose exception message would otherwise leak connection detail.</summary>
internal sealed class ThrowingTestConnectionAksClient : DemoAksClient
{
    public override Task<bool> TestConnectionAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException("kubeconfig at C:\\Users\\me\\.kube\\config, context prod-secrets");
}

public class AksTestConnectionEndpointTests
{
    [Fact]
    public async Task TestConnectionAsync_ClientThrows_NeverReturnsRawExceptionMessage()
    {
        var profile = new ProfileRepository();
        var demo = new DemoModeService();
        var pool = new FakeMonitoringConnectionPool { AksClient = new ThrowingTestConnectionAksClient() };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<Program>.Instance;

        var result = await AksEndpoints.TestConnectionAsync(profile, demo, pool, logger, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.DoesNotContain("kubeconfig", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prod-secrets", json);
        Assert.Contains("Connection failed", json);
    }
}
