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

public class ClassifiedErrorTests
{
    [Fact]
    public void Classify_SocketException_KindUnreachable_WithTypeAndMessageInDetail()
    {
        var c = ConnectionTestError.Classify(new System.Net.Sockets.SocketException(10061));

        Assert.Equal("unreachable", c.Kind);
        Assert.Contains("SocketException", c.Detail);
        Assert.NotNull(c.Hint);
    }

    [Fact]
    public void Classify_Timeout_KindTimeout()
    {
        var c = ConnectionTestError.Classify(new TimeoutException("deadline exceeded"));

        Assert.Equal("timeout", c.Kind);
        Assert.Contains("TimeoutException", c.Detail);
    }

    [Fact]
    public void Classify_ServiceBusNotFound_KindNotFound()
    {
        var ex = new global::Azure.Messaging.ServiceBus.ServiceBusException(
            "entity missing",
            global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.MessagingEntityNotFound);

        var c = ConnectionTestError.Classify(ex);

        Assert.Equal("notFound", c.Kind);
        Assert.Contains("ServiceBusException", c.Detail);
    }

    [Fact]
    public void Classify_WrappedAuthFailure_KindAuth()
    {
        var inner = new global::Azure.Identity.AuthenticationFailedException("no token");
        var ex = new global::Azure.Messaging.ServiceBus.ServiceBusException(
            "send failed",
            global::Azure.Messaging.ServiceBus.ServiceBusFailureReason.GeneralError,
            innerException: inner);

        Assert.Equal("auth", ConnectionTestError.Classify(ex).Kind);
    }

    [Fact]
    public void Classify_Detail_IncludesInnerExceptionOneLevelDeep()
    {
        var ex = new InvalidOperationException("outer", new InvalidOperationException("inner cause"));

        var c = ConnectionTestError.Classify(ex);

        Assert.Contains("inner cause", c.Detail);
    }

    [Theory]
    [InlineData("Endpoint=sb://ns.servicebus.windows.net/;SharedAccessKey=abc123", "Endpoint=***;SharedAccessKey=***")]
    [InlineData("host.redis.cache.windows.net:6380,password=hunter2,ssl=True", "host.redis.cache.windows.net:6380,password=***,ssl=True")]
    // Host/server names are diagnostic, not secret — only credential-bearing keys scrub.
    [InlineData("Server=db;Password=p@ss;User Id=u", "Server=db;Password=***;User Id=u")]
    public void Scrub_StripsConnectionStringSegments(string raw, string expected)
    {
        Assert.Equal(expected, ConnectionTestError.Scrub(raw));
    }

    [Fact]
    public void Scrub_LeavesOrdinaryMessagesUntouched()
    {
        const string msg = "pod api-xyz in namespace ecommerce has 3 restarts";

        Assert.Equal(msg, ConnectionTestError.Scrub(msg));
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

/// <summary>Simulates a real client failure whose exception message embeds a secret-shaped segment.</summary>
internal sealed class ThrowingTestConnectionAksClient : DemoAksClient
{
    public override Task<bool> TestConnectionAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException("kubeconfig at C:\\Users\\me\\.kube\\config missing; Endpoint=https://x;SharedAccessKey=SECRET123");
}

public class AksTestConnectionEndpointTests
{
    [Fact]
    public async Task TestConnectionAsync_ClientThrows_ReturnsScrubbedDetail_NeverTheSecret()
    {
        var profile = new ProfileRepository();
        var demo = new DemoModeService();
        var pool = new FakeMonitoringConnectionPool { AksClient = new ThrowingTestConnectionAksClient() };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<Program>.Instance;

        var result = await AksEndpoints.TestConnectionAsync(profile, demo, pool, logger, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.DoesNotContain("SECRET123", json);
        Assert.Contains("SharedAccessKey=***", json);
        // The detail field is where the technical context lives — exception type plus the
        // scrubbed message, so the user sees *what* failed, not just that it did.
        Assert.Contains("InvalidOperationException", json);
        Assert.Contains("Connection failed", json);
    }
}
