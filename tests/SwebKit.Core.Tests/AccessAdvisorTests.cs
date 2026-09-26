using SwebKit.Core.Security;

namespace SwebKit.Core.Tests;

/// <summary>
/// AccessAdvisor is duck-typed by design (Core takes no Azure/SQL/Redis package refs),
/// so these fakes mimic the shape — type name + Status/Reason/Number/StatusCode members —
/// of the real SDK exceptions.
/// </summary>
public sealed class AccessAdvisorTests
{
    [Fact]
    public void AzureSdk403_IsDenied()
    {
        var ex = new RequestFailedException(403);
        Assert.True(AccessAdvisor.IsAccessDenied(ex));
    }

    [Fact]
    public void AzureSdk401_IsDenied()
    {
        Assert.True(AccessAdvisor.IsAccessDenied(new RequestFailedException(401)));
    }

    [Fact]
    public void AzureSdk500_IsNotDenied()
    {
        Assert.False(AccessAdvisor.IsAccessDenied(new RequestFailedException(500)));
    }

    [Fact]
    public void ServiceBusUnauthorizedReason_IsDenied()
    {
        Assert.True(AccessAdvisor.IsAccessDenied(new ServiceBusException("UnauthorizedAccess")));
    }

    [Fact]
    public void ServiceBusCommunicationError_IsNotDenied()
    {
        Assert.False(AccessAdvisor.IsAccessDenied(new ServiceBusException("ServiceCommunicationProblem")));
    }

    [Fact]
    public void SqlPermissionErrors229And230_AreDenied()
    {
        Assert.True(AccessAdvisor.IsAccessDenied(new SqlException(229)));
        Assert.True(AccessAdvisor.IsAccessDenied(new SqlException(230)));
    }

    [Fact]
    public void SqlSyntaxError_IsNotDenied()
    {
        Assert.False(AccessAdvisor.IsAccessDenied(new SqlException(102)));
    }

    [Fact]
    public void Http403_IsDenied()
    {
        Assert.True(AccessAdvisor.IsAccessDenied(new HttpRequestException("forbidden", null, System.Net.HttpStatusCode.Forbidden)));
    }

    [Fact]
    public void Kubernetes403_IsDenied()
    {
        var response = new { StatusCode = 403 };
        Assert.True(AccessAdvisor.IsAccessDenied(new HttpOperationException(response)));
    }

    [Fact]
    public void InnerDenial_IsFoundThroughWrappers()
    {
        var inner = new RequestFailedException(403);
        var outer = new InvalidOperationException("the operation failed", inner);
        Assert.True(AccessAdvisor.IsAccessDenied(outer));
    }

    [Fact]
    public void RedisNoPermMessage_IsDenied()
    {
        Assert.True(AccessAdvisor.IsAccessDenied(new RedisServerException("NOPERM this user has no permissions to run the 'scan' command")));
    }

    [Fact]
    public void PlainError_IsNotDenied()
    {
        Assert.False(AccessAdvisor.IsAccessDenied(new InvalidOperationException("something else went wrong")));
    }

    [Fact]
    public void MessageFallback_CatchesUnambiguousAuthzText()
    {
        Assert.True(AccessAdvisor.IsAccessDenied(new InvalidOperationException("AuthorizationFailed: the client does not have authorization to perform action")));
    }

    [Fact]
    public void TryCreateDenial_MapsFeatureAreaToLeastPrivilegeFix()
    {
        Assert.True(AccessAdvisor.TryCreateDenial(new RequestFailedException(403), "ServiceBus", out var denial));
        Assert.Equal("service-bus.data", denial.Capability);
        Assert.Equal("Azure Service Bus Data Receiver", denial.RequiredAccess);
        Assert.Contains("Service Bus namespace", denial.Guidance);
    }

    [Fact]
    public void TryCreateDenial_UnknownArea_GetsGenericRemedyButStillTrue()
    {
        Assert.True(AccessAdvisor.TryCreateDenial(new RequestFailedException(403), "ApiClient", out var denial));
        Assert.Equal("read access", denial.RequiredAccess);
    }

    [Fact]
    public void TryCreateDenial_NonDenial_ReturnsFalse()
    {
        Assert.False(AccessAdvisor.TryCreateDenial(new InvalidOperationException("boom"), "ServiceBus", out _));
    }

    // The classifier duck-types on Type.Name + public members, so these fakes must carry the
    // exact SDK type names (nested classes keep their simple names).
    private sealed class RequestFailedException(int status) : Exception($"Request failed: {status}")
    {
        public int Status { get; } = status;
    }

    private sealed class ServiceBusException(string reason) : Exception($"ServiceBus failed: {reason}")
    {
        public string Reason { get; } = reason;
    }

    private sealed class SqlException(int number) : Exception($"Sql error {number}")
    {
        public int Number { get; } = number;
    }

    private sealed class HttpOperationException(object response) : Exception("k8s op failed")
    {
        public object Response { get; } = response;
    }

    private sealed class RedisServerException(string message) : Exception(message);
}
