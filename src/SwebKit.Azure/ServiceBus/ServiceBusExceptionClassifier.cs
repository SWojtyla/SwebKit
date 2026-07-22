using Azure;
using Azure.Identity;

namespace SwebKit.Azure.ServiceBus;

/// <summary>
/// Classifies Service Bus exceptions to determine whether they represent authentication/authorization failures,
/// cancellation, or transient errors. Centralizes exception-handling policy so callers don't repeat the same
/// switch/catch logic.
/// </summary>
public static class ServiceBusExceptionClassifier
{
    /// <summary>
    /// Returns true when the exception represents a credential or authorization problem.
    /// </summary>
    public static bool IsAuthenticationFailure(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is AuthenticationFailedException)
                return true;

            if (e is UnauthorizedAccessException)
                return true;

            if (e is RequestFailedException rfe && rfe.Status is 401 or 403)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when the exception is, or wraps, an <see cref="OperationCanceledException"/>
    /// or the supplied token has been canceled.
    /// </summary>
    public static bool IsCancellation(Exception ex, CancellationToken ct) =>
        ct.IsCancellationRequested || ContainsOperationCanceledException(ex);

    private static bool ContainsOperationCanceledException(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is OperationCanceledException)
                return true;
        }

        return false;
    }
}
