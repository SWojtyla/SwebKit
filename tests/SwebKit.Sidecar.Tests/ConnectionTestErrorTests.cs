using SwebKit.Sidecar.Services;
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

    [Theory]
    [InlineData(global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.ServiceTimeout, "Service Bus is busy or timed out — try again in a moment")]
    [InlineData(global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.ServiceBusy, "Service Bus is busy or timed out — try again in a moment")]
    [InlineData(global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.MessagingEntityNotFound, "Queue or subscription not found — it may have been renamed or deleted")]
    [InlineData(global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.GeneralError, "Service Bus request failed")]
    public void Describe_ServiceBusException_ReturnsReasonMessage_WithoutExceptionDetail(
        global::Azure.Messaging.ServiceBus.ServiceBusFailureReason reason,
        string expected)
    {
        var ex = new global::Azure.Messaging.ServiceBus.ServiceBusException(
            "some sensitive connection-string detail",
            reason);

        var message = ConnectionTestError.Describe(ex);

        Assert.Equal(expected, message);
        Assert.DoesNotContain("sensitive", message);
    }
}

public class ServiceBusEndpointsPeekTests
{
    [Theory]
    [InlineData(50, 50)]
    [InlineData(250, 250)]
    // Beyond the UI's largest page size (200) — a huge count makes the SDK enumerate far more
    // than the list can render and the request hangs until it times out.
    [InlineData(53142, 250)]
    // A zero/negative count would make the SDK throw ArgumentOutOfRange.
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public void ClampCount_ClampsToSupportedRange(int input, int expected)
    {
        Assert.Equal(expected, ServiceBusEndpoints.ClampCount(input));
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
