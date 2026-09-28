namespace SwebKit.Core.Security;

/// <summary>
/// The signed-in Azure identity as decoded from an ARM access token's JWT payload —
/// see docs/features/active/access-awareness-pipeline.md (Phase 3a). Every member is
/// nullable: service-principal/managed-identity tokens carry no <see cref="Upn"/> (use
/// <see cref="ObjectId"/> + <see cref="AppId"/> there), and claim presence varies by
/// credential source.
/// </summary>
public sealed record ResolvedPrincipal(
    string? ObjectId,
    string? Upn,
    string? TenantId,
    string? AppId,
    string? DisplayName);

/// <summary>
/// Resolves the identity behind the app's shared <c>DefaultAzureCredential</c> by
/// acquiring a token for the ARM scope and decoding its claims — no extra Graph call,
/// no extra consent. Implementations cache the result for the session and degrade to
/// <see langword="null"/> when no token can be acquired (not signed in, credential
/// unavailable) rather than throwing.
/// </summary>
public interface IAzurePrincipalContext
{
    /// <summary>The current principal, or null when it can't be resolved. Callers must
    /// handle null — an access-request artifact is still useful without it (the admin
    /// is asked to look the requester up).</summary>
    Task<ResolvedPrincipal?> GetPrincipalAsync(CancellationToken ct = default);
}
