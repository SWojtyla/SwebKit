namespace SwebKit.Core.Abstractions;

/// <summary>
/// Thrown by <see cref="IAksClient"/> implementations when no identity could be established at
/// all (HTTP 401 Unauthorized) — typically because the kubeconfig's exec credential plugin
/// (<c>kubelogin</c>) produced no token, or the cached Azure sign-in has expired. This is the
/// distinct sibling of <see cref="AksAccessDeniedException"/> (HTTP 403), where the identity is
/// valid but lacks RBAC permission for the requested resource.
/// <para>
/// The distinction matters to the UI: a 403 on <c>list namespaces</c> is a normal RBAC shape that
/// still leaves per-namespace access usable, whereas a 401 means nothing will work until the user
/// re-authenticates. <see cref="Exception.Message"/> is deliberately built to be user-facing and
/// free of secrets (see <c>AksExecCredentialDiagnostics</c>), so it is safe to show in the UI and
/// to return over the sidecar HTTP API.
/// </para>
/// </summary>
public sealed class AksAuthenticationException : Exception
{
    public AksAuthenticationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
