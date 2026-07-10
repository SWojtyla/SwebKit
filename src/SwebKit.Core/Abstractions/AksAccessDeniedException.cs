namespace SwebKit.Core.Abstractions;

/// <summary>
/// Thrown by <see cref="IAksClient"/> implementations when the current identity is
/// authenticated but not authorized (HTTP 403 Forbidden) to perform a request against a
/// specific namespace/resource — a genuine RBAC denial, as opposed to a transient or
/// connectivity failure. Kept separate from client-library-specific exception types
/// (e.g. <c>k8s.Autorest.HttpOperationException</c>) so upper layers (like
/// <see cref="IAksClient"/>'s multi-namespace fan-out) can react to it without depending
/// on any particular Kubernetes client library.
/// </summary>
public sealed class AksAccessDeniedException : Exception
{
    public AksAccessDeniedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
