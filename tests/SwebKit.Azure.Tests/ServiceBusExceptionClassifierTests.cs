using SwebKit.Azure.ServiceBus;

namespace SwebKit.Azure.Tests;

public class ServiceBusExceptionClassifierTests
{
    [Fact]
    public void IsCancellation_WhenTokenCanceled_ReturnsTrue()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.True(ServiceBusExceptionClassifier.IsCancellation(new InvalidOperationException("any"), cts.Token));
    }

    [Fact]
    public void IsCancellation_WhenExceptionWrapsOperationCanceled_ReturnsTrue()
    {
        var ex = new InvalidOperationException("Azure.Identity.AuthenticationFailedException", new OperationCanceledException());

        Assert.True(ServiceBusExceptionClassifier.IsCancellation(ex, default));
    }

    [Fact]
    public void IsCancellation_WhenNotCanceledAndNoInnerOperationCanceled_ReturnsFalse()
    {
        Assert.False(ServiceBusExceptionClassifier.IsCancellation(new InvalidOperationException("network"), default));
    }

    [Fact]
    public void IsAuthenticationFailure_RecognizesUnauthorizedAccess()
    {
        Assert.True(ServiceBusExceptionClassifier.IsAuthenticationFailure(new UnauthorizedAccessException()));
    }

    [Fact]
    public void IsAuthenticationFailure_RecognizesRequestFailed401()
    {
        Assert.True(ServiceBusExceptionClassifier.IsAuthenticationFailure(new global::Azure.RequestFailedException(401, "Unauthorized")));
    }

    [Fact]
    public void IsAuthenticationFailure_DoesNotClassifyWrappedCancellation()
    {
        var ex = new InvalidOperationException("auth", new OperationCanceledException());
        Assert.False(ServiceBusExceptionClassifier.IsAuthenticationFailure(ex));
    }
}
