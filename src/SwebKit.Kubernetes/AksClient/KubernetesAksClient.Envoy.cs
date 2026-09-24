using System.Net;
using System.Text.Json;
using k8s;
using SwebKit.Core.Models;

namespace SwebKit.Kubernetes.AksClient;

/// <summary>
/// Envoy Gateway (<c>gateway.envoyproxy.io</c>) support: lists the CRDs Envoy Gateway installs
/// (traffic policies, security policies, Backends, EnvoyProxy, …) and flattens each spec into a
/// small set of highlights so the UI can show "maxConnections: 1024" without parsing YAML.
/// Same shape as the Gateway API helpers — version-probe cache + CustomObjects, with absent CRDs
/// degrading to an empty list rather than an error.
/// </summary>
public partial class KubernetesAksClient
{
    private const string EnvoyApiGroup = "gateway.envoyproxy.io";
    private static readonly string[] EnvoyApiVersions = ["v1alpha1"];

    public async Task<IReadOnlyList<EnvoyResourceInfo>> GetEnvoyResourcesAsync(string ns, string plural, CancellationToken ct = default)
    {
        if (!EnvoyGatewayKinds.PluralToKind.TryGetValue(plural, out var kind))
            return [];

        return await WithAuthRetryAsync(async () =>
        {
            // EnvoyProxy is cluster-scoped; every other Envoy Gateway CRD is namespaced.
            var result = plural == EnvoyGatewayKinds.ProxyPlural
                ? await ListClusterEnvoyCustomObjectsAsync(plural, ct).ConfigureAwait(false)
                : await ListEnvoyCustomObjectsAsync(ns, plural, ct).ConfigureAwait(false);
            if (result is null)
                return [];

            var json = JsonSerializer.Serialize(result);
            using var doc = JsonDocument.Parse(json);
            return MapEnvoyResources(doc.RootElement, kind, ns);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a single Envoy Gateway resource for the YAML view. <paramref name="plural"/> is the
    /// same segment the list endpoint accepts; cluster-scoped kinds are resolved through the map.
    /// </summary>
    private async Task<object> ReadEnvoyCustomObjectAsync(string ns, string plural, string name, CancellationToken ct)
    {
        var clusterScoped = plural == EnvoyGatewayKinds.ProxyPlural;
        foreach (var version in EnvoyApiVersions)
        {
            try
            {
                return clusterScoped
                    ? await _client.CustomObjects.GetClusterCustomObjectAsync(
                        EnvoyApiGroup, version, plural, name, cancellationToken: ct).ConfigureAwait(false)
                    : await _client.CustomObjects.GetNamespacedCustomObjectAsync(
                        EnvoyApiGroup, version, ns, plural, name, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }

        throw new InvalidOperationException(
            $"Envoy Gateway resource '{plural}/{name}' is not available in namespace '{ns}'.");
    }

    private async Task<object?> ListEnvoyCustomObjectsAsync(string ns, string plural, CancellationToken ct)
    {
        // Same probe cache as the Gateway API helpers — plural keys don't overlap between groups,
        // so one cache safely serves both.
        if (_gatewayApiVersionCache.IsKnownUnavailable(plural))
            return null;

        if (_gatewayApiVersionCache.TryGetWorkingVersion(plural) is { } knownVersion)
        {
            try
            {
                return await _client.CustomObjects.ListNamespacedCustomObjectAsync(
                    EnvoyApiGroup, knownVersion, ns, plural, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                _gatewayApiVersionCache.ForgetWorkingVersion(plural);
            }
        }

        foreach (var version in EnvoyApiVersions)
        {
            try
            {
                var result = await _client.CustomObjects.ListNamespacedCustomObjectAsync(
                    EnvoyApiGroup, version, ns, plural, cancellationToken: ct).ConfigureAwait(false);
                _gatewayApiVersionCache.MarkWorking(plural, version);
                return result;
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }

        _gatewayApiVersionCache.MarkUnavailable(plural);
        return null;
    }

    private async Task<object?> ListClusterEnvoyCustomObjectsAsync(string plural, CancellationToken ct)
    {
        if (_gatewayApiVersionCache.IsKnownUnavailable(plural))
            return null;

        if (_gatewayApiVersionCache.TryGetWorkingVersion(plural) is { } knownVersion)
        {
            try
            {
                return await _client.CustomObjects.ListClusterCustomObjectAsync(
                    EnvoyApiGroup, knownVersion, plural, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                _gatewayApiVersionCache.ForgetWorkingVersion(plural);
            }
        }

        foreach (var version in EnvoyApiVersions)
        {
            try
            {
                var result = await _client.CustomObjects.ListClusterCustomObjectAsync(
                    EnvoyApiGroup, version, plural, cancellationToken: ct).ConfigureAwait(false);
                _gatewayApiVersionCache.MarkWorking(plural, version);
                return result;
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }

        _gatewayApiVersionCache.MarkUnavailable(plural);
        return null;
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    internal static List<EnvoyResourceInfo> MapEnvoyResources(JsonElement root, string kind, string fallbackNamespace)
    {
        if (!TryGetProperty(root, "items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var resources = new List<EnvoyResourceInfo>();
        foreach (var item in items.EnumerateArray())
        {
            var name = GetMetadataName(item);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            TryGetProperty(item, "spec", out var spec);
            var resourceNamespace = GetMetadataNamespace(item, fallbackNamespace);
            resources.Add(new EnvoyResourceInfo
            {
                Kind = kind,
                Name = name,
                Namespace = resourceNamespace,
                TargetRefs = GetEnvoyTargetRefs(spec, resourceNamespace),
                Highlights = GetEnvoyHighlights(kind, item, spec),
                Labels = GetMetadataLabels(item)
            });
        }

        return resources
            .OrderBy(resource => resource.Namespace, StringComparer.Ordinal)
            .ThenBy(resource => resource.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Envoy policies attach via <c>spec.targetRefs[]</c> (newer API) or <c>spec.targetRef</c>
    /// (older). Both produce "Kind/name" strings, with a namespace prefix when cross-namespace.
    /// </summary>
    private static List<string> GetEnvoyTargetRefs(JsonElement spec, string resourceNamespace)
    {
        if (spec.ValueKind != JsonValueKind.Object)
            return [];

        var refs = new List<string>();
        if (TryGetProperty(spec, "targetRefs", out var targetRefs) && targetRefs.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in targetRefs.EnumerateArray())
            {
                var formatted = FormatEnvoyRef(entry, resourceNamespace);
                if (!string.IsNullOrWhiteSpace(formatted))
                    refs.Add(formatted);
            }
        }

        if (TryGetProperty(spec, "targetRef", out var targetRef) && targetRef.ValueKind == JsonValueKind.Object)
        {
            var formatted = FormatEnvoyRef(targetRef, resourceNamespace);
            if (!string.IsNullOrWhiteSpace(formatted))
                refs.Add(formatted);
        }

        return refs.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string FormatEnvoyRef(JsonElement reference, string resourceNamespace)
    {
        var name = GetStringProperty(reference, "name");
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var kind = GetStringProperty(reference, "kind");
        var ns = GetStringProperty(reference, "namespace");
        var kindPrefix = string.IsNullOrWhiteSpace(kind) ? string.Empty : $"{kind}/";
        var nsPrefix = !string.IsNullOrWhiteSpace(ns)
            && !string.Equals(ns, resourceNamespace, StringComparison.Ordinal)
            ? $"{ns}/"
            : string.Empty;
        return $"{kindPrefix}{nsPrefix}{name}";
    }

    /// <summary>
    /// Kind-specific headline settings. Everything is probed defensively — a field that isn't in
    /// the spec simply doesn't produce a highlight, so schema drift across Envoy Gateway versions
    /// can only ever reduce the summary, never break the listing.
    /// </summary>
    private static List<EnvoyHighlight> GetEnvoyHighlights(string kind, JsonElement item, JsonElement spec)
    {
        var highlights = new List<EnvoyHighlight>();
        if (spec.ValueKind != JsonValueKind.Object)
            return highlights;

        switch (kind)
        {
            case "Backend":
                AddBackendHighlights(highlights, spec);
                break;
            case "BackendTrafficPolicy":
                AddBackendTrafficPolicyHighlights(highlights, spec);
                break;
            case "ClientTrafficPolicy":
                AddClientTrafficPolicyHighlights(highlights, spec);
                break;
            case "SecurityPolicy":
                AddSecurityPolicyHighlights(highlights, spec);
                break;
            case "EnvoyExtensionPolicy":
                AddArrayCountHighlight(highlights, spec, "Ext proc", "extProc");
                AddArrayCountHighlight(highlights, spec, "WASM", "wasm");
                AddPresenceHighlight(highlights, spec, "Ext auth", "extAuth");
                break;
            case "EnvoyPatchPolicy":
                AddScalarHighlight(highlights, spec, "Type", "type");
                AddArrayCountHighlight(highlights, spec, "Patches", "jsonPatches");
                break;
            case "EnvoyProxy":
                AddScalarHighlight(highlights, spec, "Provider", "provider", "type");
                break;
            case "HTTPRouteFilter":
                AddPresenceHighlight(highlights, spec, "URL rewrite", "urlRewrite");
                AddPresenceHighlight(highlights, spec, "Direct response", "directResponse");
                AddPresenceHighlight(highlights, spec, "Credential injection", "credentialInjection");
                AddScalarHighlight(highlights, spec, "Extension ref", "extensionRef", "name");
                break;
        }

        // Status conditions are common to all Envoy CRDs — surface a non-Accepted state so a
        // policy the controller rejected stands out in the table.
        if (HasTopLevelCondition(item, "Accepted"))
        {
            highlights.Insert(0, new EnvoyHighlight { Label = "Status", Value = "Accepted" });
        }
        else if (TryGetFirstTopLevelFailingCondition(item, out var failingCondition))
        {
            highlights.Insert(0, new EnvoyHighlight { Label = "Status", Value = failingCondition });
        }

        return highlights;
    }

    private static void AddBackendHighlights(List<EnvoyHighlight> highlights, JsonElement spec)
    {
        AddScalarHighlight(highlights, spec, "Type", "type");

        if (!TryGetProperty(spec, "endpoints", out var endpoints) || endpoints.ValueKind != JsonValueKind.Array)
            return;

        var formatted = endpoints.EnumerateArray()
            .Select(FormatBackendEndpoint)
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint))
            .Select(endpoint => endpoint!)
            .ToList();
        if (formatted.Count > 0)
        {
            var shown = string.Join(", ", formatted.Take(3));
            highlights.Add(new EnvoyHighlight
            {
                Label = $"Endpoints ({formatted.Count})",
                Value = formatted.Count > 3 ? $"{shown} +{formatted.Count - 3} more" : shown
            });
        }
    }

    private static string? FormatBackendEndpoint(JsonElement endpoint)
    {
        if (TryGetProperty(endpoint, "fqdn", out var fqdn))
        {
            var host = GetStringProperty(fqdn, "hostname");
            var port = TryGetIntProperty(fqdn, "port");
            return string.IsNullOrWhiteSpace(host) ? null : port.HasValue ? $"{host}:{port}" : host;
        }

        if (TryGetProperty(endpoint, "ip", out var ip))
        {
            var address = GetStringProperty(ip, "address");
            var port = TryGetIntProperty(ip, "port");
            return string.IsNullOrWhiteSpace(address) ? null : port.HasValue ? $"{address}:{port}" : address;
        }

        if (TryGetProperty(endpoint, "unix", out var unix))
            return GetStringProperty(unix, "path");

        return null;
    }

    private static void AddBackendTrafficPolicyHighlights(List<EnvoyHighlight> highlights, JsonElement spec)
    {
        AddScalarHighlight(highlights, spec, "Max connections", "circuitBreaker", "maxConnections");
        AddScalarHighlight(highlights, spec, "Max pending", "circuitBreaker", "maxPendingRequests");
        AddScalarHighlight(highlights, spec, "Max requests", "circuitBreaker", "maxRequests");
        AddScalarHighlight(highlights, spec, "Max retries", "circuitBreaker", "maxRetries");
        AddScalarHighlight(highlights, spec, "Buffer limit", "connection", "bufferLimit");
        AddScalarHighlight(highlights, spec, "Connect timeout", "timeout", "tcp", "connectTimeout");
        AddScalarHighlight(highlights, spec, "Request timeout", "timeout", "http", "requestTimeout");
        AddScalarHighlight(highlights, spec, "Retries", "retry", "numRetries");
        AddScalarHighlight(highlights, spec, "Retry timeout", "retry", "perRetryTimeout");
        AddScalarHighlight(highlights, spec, "Load balancer", "loadBalancer", "type");

        if (TryGetProperty(spec, "rateLimit", out var rateLimit) && rateLimit.ValueKind == JsonValueKind.Object)
        {
            var rateLimitType = GetStringProperty(rateLimit, "type") ?? "enabled";
            var rules = TryGetProperty(rateLimit, "global", out var global) && TryGetProperty(global, "rules", out var globalRules) && globalRules.ValueKind == JsonValueKind.Array
                ? globalRules.GetArrayLength()
                : TryGetProperty(rateLimit, "local", out var local) && TryGetProperty(local, "rules", out var localRules) && localRules.ValueKind == JsonValueKind.Array
                    ? localRules.GetArrayLength()
                    : (int?)null;
            highlights.Add(new EnvoyHighlight
            {
                Label = "Rate limit",
                Value = rules.HasValue ? $"{rateLimitType} ({rules.Value} rules)" : rateLimitType
            });
        }

        if (TryGetProperty(spec, "healthCheck", out var healthCheck) && healthCheck.ValueKind == JsonValueKind.Object)
        {
            var parts = new List<string>();
            if (TryGetProperty(healthCheck, "active", out _)) parts.Add("active");
            if (TryGetProperty(healthCheck, "passive", out _)) parts.Add("passive");
            if (parts.Count > 0)
                highlights.Add(new EnvoyHighlight { Label = "Health check", Value = string.Join(" + ", parts) });
        }

        AddScalarHighlight(highlights, spec, "TLS SNI", "tls", "sni");
        AddPresenceHighlight(highlights, spec, "HTTP/2", "http2");
        AddPresenceHighlight(highlights, spec, "Fault injection", "faultInjection");
    }

    private static void AddClientTrafficPolicyHighlights(List<EnvoyHighlight> highlights, JsonElement spec)
    {
        AddScalarHighlight(highlights, spec, "TLS min version", "tls", "minVersion");
        AddScalarHighlight(highlights, spec, "TLS max version", "tls", "maxVersion");
        AddScalarHighlight(highlights, spec, "SNI", "tls", "sni");
        AddScalarHighlight(highlights, spec, "Buffer limit", "connection", "bufferLimit");
        AddScalarHighlight(highlights, spec, "Connect timeout", "connection", "connectTimeout");
        AddScalarHighlight(highlights, spec, "Keepalive idle", "tcpKeepalive", "idleTime");
        AddPresenceHighlight(highlights, spec, "HTTP/2", "http2");
        AddPresenceHighlight(highlights, spec, "HTTP/3", "http3");
        AddPresenceHighlight(highlights, spec, "X-Forwarded-For", "clientIPDetection");
        AddPresenceHighlight(highlights, spec, "Health check", "healthCheck");
    }

    private static void AddSecurityPolicyHighlights(List<EnvoyHighlight> highlights, JsonElement spec)
    {
        if (TryGetProperty(spec, "jwt", out var jwt) && jwt.ValueKind == JsonValueKind.Object
            && TryGetProperty(jwt, "providers", out var providers) && providers.ValueKind == JsonValueKind.Array)
        {
            var issuers = providers.EnumerateArray()
                .Select(provider => GetStringProperty(provider, "issuer"))
                .Where(issuer => !string.IsNullOrWhiteSpace(issuer))
                .ToList();
            var count = providers.GetArrayLength();
            highlights.Add(new EnvoyHighlight
            {
                Label = $"JWT ({count})",
                Value = issuers.Count > 0 ? string.Join(", ", issuers.Take(2)) : $"{count} provider(s)"
            });
        }

        if (TryGetProperty(spec, "oidc", out var oidc) && oidc.ValueKind == JsonValueKind.Object)
        {
            var clientId = GetStringProperty(oidc, "clientID");
            highlights.Add(new EnvoyHighlight { Label = "OIDC", Value = clientId ?? "configured" });
        }

        AddPresenceHighlight(highlights, spec, "API key", "apiKeyAuth");
        AddPresenceHighlight(highlights, spec, "Basic auth", "basicAuth");
        AddPresenceHighlight(highlights, spec, "Ext auth", "extAuth");

        if (TryGetProperty(spec, "authorization", out var authorization) && authorization.ValueKind == JsonValueKind.Object)
        {
            var action = GetStringProperty(authorization, "defaultAction") ?? "Deny";
            var rules = TryGetProperty(authorization, "rules", out var authzRules) && authzRules.ValueKind == JsonValueKind.Array
                ? authzRules.GetArrayLength()
                : 0;
            highlights.Add(new EnvoyHighlight { Label = "Authorization", Value = $"{action} ({rules} rules)" });
        }

        if (TryGetProperty(spec, "cors", out var cors) && cors.ValueKind == JsonValueKind.Object
            && TryGetProperty(cors, "allowOrigins", out var origins) && origins.ValueKind == JsonValueKind.Array)
        {
            highlights.Add(new EnvoyHighlight { Label = "CORS", Value = $"{origins.GetArrayLength()} origins" });
        }
    }

    /// <summary>Adds "label: value" when the scalar at <paramref name="path"/> exists.</summary>
    private static void AddScalarHighlight(List<EnvoyHighlight> highlights, JsonElement root, string label, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!TryGetProperty(current, segment, out var next))
                return;
            current = next;
        }

        var value = current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => current.ToString(),
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(value))
            highlights.Add(new EnvoyHighlight { Label = label, Value = value });
    }

    /// <summary>Adds "label: N" when the array at <paramref name="path"/> exists.</summary>
    private static void AddArrayCountHighlight(List<EnvoyHighlight> highlights, JsonElement root, string label, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!TryGetProperty(current, segment, out var next))
                return;
            current = next;
        }

        if (current.ValueKind == JsonValueKind.Array)
            highlights.Add(new EnvoyHighlight { Label = label, Value = current.GetArrayLength().ToString() });
    }

    /// <summary>Adds "label: yes" when an object/array exists at <paramref name="path"/>.</summary>
    private static void AddPresenceHighlight(List<EnvoyHighlight> highlights, JsonElement root, string label, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!TryGetProperty(current, segment, out var next))
                return;
            current = next;
        }

        if (current.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            highlights.Add(new EnvoyHighlight { Label = label, Value = "yes" });
    }
}
