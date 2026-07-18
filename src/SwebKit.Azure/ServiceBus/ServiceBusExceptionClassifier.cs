using Azure;

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
            if (e is UnauthorizedAccessException)
                return true;

            if (e is RequestFailedException rfe && rfe.Status is 401 or 403)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when the exception is an <see cref="OperationCanceledException"/> that should be re-thrown
    /// without further processing.
    /// </summary>
    public static bool IsCancellation(OperationCanceledException ex, CancellationToken ct) =>
        ex is OperationCanceledException && ct.IsCancellationRequested;
}
