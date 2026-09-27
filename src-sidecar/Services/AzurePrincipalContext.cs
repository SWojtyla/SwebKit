using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Security;
using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Sidecar <see cref="IAzurePrincipalContext"/>: asks the shared
/// <see cref="AzureCredentialFactory"/> credential for an ARM token and decodes the JWT
/// payload's claims (<c>oid</c>, <c>upn</c>, <c>tid</c>, <c>appid</c>, <c>name</c>) — the
/// token already identifies the caller, so this costs no extra consent and no Graph call
/// (access-awareness Phase 3a).
///
/// The resolved principal is cached for the session: the sidecar runs one user identity per
/// process, and re-requesting a token (AzureCliCredential shells out to <c>az</c>) per
/// request-artifact click would be needlessly slow. Failures are *not* cached — a credential
/// that wasn't ready yet (login still in progress) gets retried on the next call.
///
/// Service-principal tokens have no <c>upn</c> — the principal degrades to
/// <see cref="ResolvedPrincipal.ObjectId"/>/<see cref="ResolvedPrincipal.AppId"/> and the
/// caller words artifacts accordingly.
/// </summary>
public sealed class AzurePrincipalContext : IAzurePrincipalContext
{
    private static readonly string[] ArmScopes = ["https://management.azure.com/.default"];

    private readonly TokenCredential _credential;
    private readonly ILogger<AzurePrincipalContext>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile ResolvedPrincipal? _cached;

    /// <summary>Used by DI — resolves through the app-wide default credential.</summary>
    public AzurePrincipalContext()
        : this(AzureCredentialFactory.CreateDefault(), null)
    {
    }

    /// <summary>Test seam: any <see cref="TokenCredential"/> can be substituted.</summary>
    public AzurePrincipalContext(TokenCredential credential, ILogger<AzurePrincipalContext>? logger = null)
    {
        _credential = credential;
        _logger = logger;
    }

    public async Task<ResolvedPrincipal?> GetPrincipalAsync(CancellationToken ct = default)
    {
        if (_cached is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached is not null)
            {
                return _cached;
            }

            var token = await _credential
                .GetTokenAsync(new TokenRequestContext(ArmScopes), ct)
                .ConfigureAwait(false);
            var principal = TryParseJwtPayload(token.Token);
            if (principal is null)
            {
                _logger?.LogWarning("ARM token acquired but its claims could not be decoded.");
                return null;
            }

            _cached = principal;
            return principal;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not signed in / no credential available — a request artifact without a principal
            // is still useful, so degrade to null rather than failing the endpoint.
            _logger?.LogDebug(ex, "Could not resolve the signed-in Azure principal.");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Decodes the JWT payload segment (no signature validation — the token came straight from
    /// the credential, it's not an untrusted input). Returns null on malformed input. Claim
    /// fallbacks: <c>upn</c> → <c>preferred_username</c> → <c>unique_name</c>; SPs land on
    /// <c>oid</c>+<c>appid</c> alone.
    /// </summary>
    internal static ResolvedPrincipal? TryParseJwtPayload(string token)
    {
        var segments = token.Split('.');
        if (segments.Length < 2 || segments[1].Length == 0)
        {
            return null;
        }

        try
        {
            var payload = Base64UrlDecode(segments[1]);
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            string? Claim(params string[] names)
            {
                foreach (var name in names)
                {
                    if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
                    {
                        var value = el.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }
                return null;
            }

            var objectId = Claim("oid");
            var appId = Claim("appid", "azp");
            if (objectId is null && appId is null)
            {
                // A token with neither an object id nor an app id can't identify anyone.
                return null;
            }

            return new ResolvedPrincipal(
                ObjectId: objectId,
                Upn: Claim("upn", "preferred_username", "unique_name"),
                TenantId: Claim("tid"),
                AppId: appId,
                DisplayName: Claim("name"));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Base64url (JWT) decode — tolerates the missing padding JWT allows.</summary>
    private static byte[] Base64UrlDecode(string segment)
    {
        var s = segment.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => throw new FormatException("Invalid base64url segment length."),
        };
        return Convert.FromBase64String(s);
    }
}
