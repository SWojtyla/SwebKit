using Azure.Core;
using Azure.Identity;
using k8s;
using k8s.KubeConfigModels;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Constants;
using SwebKit.Core.Models;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace SwebKit.Kubernetes.AksClient;

public partial class KubernetesAksClient
{
    public async Task<IngressAnalysis> AnalyzeIngressAsync(string ns, string ingressName, CancellationToken ct = default)
    {
        try
        {
            return await WithAuthRetryAsync(async () =>
            {
                var ingress = await _client.NetworkingV1.ReadNamespacedIngressAsync(ingressName, ns, cancellationToken: ct).ConfigureAwait(false);
                var servicesTask = _client.CoreV1.ListNamespacedServiceAsync(ns, cancellationToken: ct);
                var podsTask = _client.CoreV1.ListNamespacedPodAsync(ns, cancellationToken: ct);
                await Task.WhenAll(servicesTask, podsTask).ConfigureAwait(false);

                var services = (await servicesTask.ConfigureAwait(false)).Items
                    .Where(service => !string.IsNullOrWhiteSpace(service.Metadata?.Name))
                    .ToDictionary(service => service.Metadata.Name, StringComparer.Ordinal);
                var pods = (await podsTask.ConfigureAwait(false)).Items;

                var backends = new List<IngressBackendAnalysis>();
                foreach (var rule in ingress.Spec?.Rules ?? [])
                {
                    foreach (var path in rule.Http?.Paths ?? [])
                    {
                        backends.Add(AnalyzeIngressBackend(
                            ns,
                            rule.Host,
                            path.Path ?? "/",
                            path.PathType,
                            path.Backend?.Service,
                            services,
                            pods));
                    }
                }

                if (ingress.Spec?.DefaultBackend?.Service is { } defaultBackend)
                {
                    backends.Add(AnalyzeIngressBackend(
                        ns,
                        "*",
                        "/",
                        "DefaultBackend",
                        defaultBackend,
                        services,
                        pods));
                }

                var addresses = ingress.Status?.LoadBalancer?.Ingress?
                    .Select(address => address.Ip ?? address.Hostname ?? string.Empty)
                    .Where(address => !string.IsNullOrWhiteSpace(address))
                    .Select(address => address!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? [];

                var findings = BuildIngressFindings(backends, addresses);

                return new IngressAnalysis
                {
                    Namespace = ns,
                    IngressName = ingress.Metadata?.Name ?? ingressName,
                    IngressClass = ingress.Spec?.IngressClassName,
                    Summary = BuildIngressSummary(backends, addresses),
                    Addresses = addresses,
                    Findings = findings.ToList(),
                    Backends = backends
                };
            }).ConfigureAwait(false);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Ingress '{ingressName}' was not found in namespace '{ns}'.",
                ex);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"Ingress analysis requires permission to read Ingress, Service, and Pod resources in namespace '{ns}'.",
                ex);
        }
    }

    public async Task<NetworkPolicyAnalysis> AnalyzeNetworkPoliciesAsync(
        string ns,
        string workloadKind,
        string workloadName,
        CancellationToken ct = default)
    {
        try
        {
            return await WithAuthRetryAsync(async () =>
            {
                var (selectedPods, selectorLabels) = await ResolveWorkloadPodsAsync(ns, workloadKind, workloadName, ct).ConfigureAwait(false);

                var servicesTask = _client.CoreV1.ListNamespacedServiceAsync(ns, cancellationToken: ct);
                var ingressesTask = _client.NetworkingV1.ListNamespacedIngressAsync(ns, cancellationToken: ct);
                var policiesTask = _client.NetworkingV1.ListNamespacedNetworkPolicyAsync(ns, cancellationToken: ct);
                var httpRoutesTask = GetHttpRoutesAsync(ns, ct);
                await Task.WhenAll(servicesTask, ingressesTask, policiesTask, httpRoutesTask).ConfigureAwait(false);

                var services = (await servicesTask.ConfigureAwait(false)).Items
                    .Where(service => ServiceTargetsAnyPod(service, selectedPods))
                    .Select(service => service.Metadata?.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();

                var exposedByIngresses = FindIngressesReferencingServices((await ingressesTask.ConfigureAwait(false)).Items, services)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();

                var exposedByHttpRoutes = FindHttpRoutesReferencingServices(await httpRoutesTask.ConfigureAwait(false), services)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();

                var policies = (await policiesTask.ConfigureAwait(false)).Items
                    .Where(policy => NetworkPolicyTargetsAnyPod(policy, selectedPods))
                    .Select(BuildNetworkPolicyMatch)
                    .OrderBy(policy => policy.Name, StringComparer.Ordinal)
                    .ToList();

                var ingressIsolated = policies.Any(policy => policy.PolicyTypes.Contains("Ingress", StringComparer.OrdinalIgnoreCase));
                var egressIsolated = policies.Any(policy => policy.PolicyTypes.Contains("Egress", StringComparer.OrdinalIgnoreCase));
                var findings = BuildNetworkPolicyFindings(selectedPods.Count, services, exposedByIngresses, exposedByHttpRoutes, policies);

                return new NetworkPolicyAnalysis
                {
                    Namespace = ns,
                    WorkloadKind = workloadKind,
                    WorkloadName = workloadName,
                    Summary = BuildNetworkPolicySummary(selectedPods.Count, policies.Count, ingressIsolated, egressIsolated),
                    MatchingPodCount = selectedPods.Count,
                    MatchingPods = selectedPods
                        .Select(pod => pod.Metadata?.Name)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Select(name => name!)
                        .Take(6)
                        .ToList(),
                    SelectorLabels = selectorLabels,
                    Services = services,
                    ExposedByIngresses = exposedByIngresses,
                    ExposedByHttpRoutes = exposedByHttpRoutes,
                    IngressIsolated = ingressIsolated,
                    EgressIsolated = egressIsolated,
                    Findings = findings.ToList(),
                    Policies = policies
                };
            }).ConfigureAwait(false);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"{workloadKind} '{workloadName}' was not found in namespace '{ns}'.",
                ex);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"Network policy analysis requires permission to read workload, Pod, Service, Ingress, and NetworkPolicy resources in namespace '{ns}'.",
                ex);
        }
    }

    private static IngressBackendAnalysis AnalyzeIngressBackend(
        string namespaceName,
        string? host,
        string path,
        string? pathType,
        V1IngressServiceBackend? backend,
        IReadOnlyDictionary<string, V1Service> services,
        IEnumerable<V1Pod> pods)
    {
        var serviceName = backend?.Name;
        var requestedPort = FormatIngressRequestedPort(backend);
        services.TryGetValue(serviceName ?? string.Empty, out var service);

        var analysis = new IngressBackendAnalysis
        {
            Host = string.IsNullOrWhiteSpace(host) ? "*" : host!,
            Path = string.IsNullOrWhiteSpace(path) ? "/" : path,
            PathType = pathType,
            ServiceName = serviceName,
            ServiceNamespace = namespaceName,
            RequestedPort = requestedPort,
            ServiceExists = service is not null,
            ServiceType = service?.Spec?.Type,
            HasSelector = service?.Spec?.Selector?.Count > 0
        };

        if (service is null)
        {
            analysis.Findings.Add($"Service '{serviceName ?? "(missing)"}' was not found.");
            return analysis;
        }

        var matchingServicePort = FindMatchingServicePort(service, backend);
        analysis.ServicePortResolved = matchingServicePort is not null;
        analysis.ResolvedServicePort = matchingServicePort is null
            ? null
            : $"{matchingServicePort.Port}/{matchingServicePort.Protocol ?? "TCP"} → {(matchingServicePort.TargetPort?.Value ?? matchingServicePort.Port.ToString(CultureInfo.InvariantCulture))}";

        if (matchingServicePort is null)
        {
            analysis.Findings.Add($"Requested port '{requestedPort}' does not exist on Service '{service.Metadata?.Name}'.");
        }

        if (!analysis.HasSelector)
        {
            analysis.Findings.Add($"Service '{service.Metadata?.Name}' has no pod selector, so backend readiness could not be inferred from pods.");
            return analysis;
        }

        var matchingPods = pods
            .Where(pod => MatchesLabels(service.Spec?.Selector, pod.Metadata?.Labels))
            .ToList();

        analysis.MatchingPodCount = matchingPods.Count;
        analysis.ReadyPodCount = matchingPods.Count(IsPodReady);
        analysis.MatchingPods = matchingPods
            .Select(pod => pod.Metadata?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Take(6)
            .ToList();

        if (matchingPods.Count == 0)
        {
            analysis.Findings.Add($"Service '{service.Metadata?.Name}' selector matched no pods.");
        }
        else if (analysis.ReadyPodCount == 0)
        {
            analysis.Findings.Add($"Service '{service.Metadata?.Name}' matched {matchingPods.Count} pod(s), but none were Ready.");
        }
        else if (analysis.ReadyPodCount < matchingPods.Count)
        {
            analysis.Findings.Add($"Service '{service.Metadata?.Name}' matched {analysis.ReadyPodCount}/{matchingPods.Count} Ready pod(s).");
        }

        return analysis;
    }

    private static List<string> BuildIngressFindings(
        IReadOnlyList<IngressBackendAnalysis> backends,
        IReadOnlyList<string> addresses)
    {
        var findings = new List<string>();

        if (addresses.Count == 0)
        {
            findings.Add("The ingress has no published load balancer address yet.");
        }

        if (backends.Count == 0)
        {
            findings.Add("No HTTP service backends were found in the ingress spec.");
            return findings;
        }

        findings.AddRange(backends.SelectMany(backend => backend.Findings).Distinct(StringComparer.Ordinal));
        if (findings.Count == 0)
        {
            findings.Add("All inspected ingress backends resolved to Services with matching Ready pods.");
        }

        return findings;
    }

    private static string BuildIngressSummary(
        IReadOnlyList<IngressBackendAnalysis> backends,
        IReadOnlyList<string> addresses)
    {
        if (backends.Count == 0)
        {
            return addresses.Count == 0
                ? "No published address or HTTP service backends were found."
                : "Published address found, but no HTTP service backends were discovered.";
        }

        var degradedBackends = backends.Count(backend => backend.Findings.Count > 0);
        if (degradedBackends == 0)
        {
            return $"All {backends.Count} inspected ingress backend(s) resolved to Services with matching Ready pods.";
        }

        return $"{degradedBackends} of {backends.Count} inspected ingress backend(s) need attention.";
    }

    private static string BuildNetworkPolicySummary(
        int matchingPodCount,
        int policyCount,
        bool ingressIsolated,
        bool egressIsolated)
    {
        if (matchingPodCount == 0)
        {
            return "No live pods matched the workload during analysis, so policy impact could not be confirmed from pod evidence.";
        }

        if (policyCount == 0)
        {
            return $"No network policies currently select the {matchingPodCount} matched pod(s).";
        }

        var ingressState = ingressIsolated ? "isolated" : "open";
        var egressState = egressIsolated ? "isolated" : "open";
        return $"{policyCount} network polic{(policyCount == 1 ? "y" : "ies")} select {matchingPodCount} pod(s). Ingress is {ingressState}; egress is {egressState}.";
    }

    private static List<string> BuildNetworkPolicyFindings(
        int matchingPodCount,
        IReadOnlyList<string> services,
        IReadOnlyList<string> exposedByIngresses,
        IReadOnlyList<string> exposedByHttpRoutes,
        IReadOnlyList<NetworkPolicyMatch> policies)
    {
        var findings = new List<string>();

        if (matchingPodCount == 0)
        {
            findings.Add("No live pods matched the workload selector while the analysis ran.");
        }

        if (policies.Count == 0)
        {
            findings.Add("No NetworkPolicy objects currently select this workload.");
        }

        if (services.Count == 0)
        {
            findings.Add("No Services in this namespace currently select the workload's pods.");
        }

        if (exposedByIngresses.Count > 0)
        {
            findings.Add($"Referenced by ingress resources: {string.Join(", ", exposedByIngresses)}.");
        }

        if (exposedByHttpRoutes.Count > 0)
        {
            findings.Add($"Referenced by HTTPRoute resources: {string.Join(", ", exposedByHttpRoutes)}.");
        }

        if (findings.Count == 0)
        {
            findings.Add("The workload is selected by Services and NetworkPolicy objects with no immediate object-level gaps detected.");
        }

        return findings;
    }

    private static NetworkPolicyMatch BuildNetworkPolicyMatch(V1NetworkPolicy policy)
    {
        var policyTypes = GetNetworkPolicyTypes(policy);

        return new NetworkPolicyMatch
        {
            Name = policy.Metadata?.Name ?? "unknown",
            PolicyTypes = policyTypes,
            IngressRules = policy.Spec?.Ingress?
                .Select(DescribeIngressRule)
                .Where(rule => !string.IsNullOrWhiteSpace(rule))
                .ToList() ?? [],
            EgressRules = policy.Spec?.Egress?
                .Select(DescribeEgressRule)
                .Where(rule => !string.IsNullOrWhiteSpace(rule))
                .ToList() ?? []
        };
    }

    private static List<string> GetNetworkPolicyTypes(V1NetworkPolicy policy)
    {
        var types = policy.Spec?.PolicyTypes?.ToList() ?? [];
        if (types.Count > 0)
        {
            return types;
        }

        types.Add("Ingress");
        if (policy.Spec?.Egress?.Count > 0)
        {
            types.Add("Egress");
        }

        return types;
    }

    private static string DescribeIngressRule(V1NetworkPolicyIngressRule rule)
    {
        var peers = DescribePeers(rule.FromProperty);
        var ports = DescribePorts(rule.Ports);
        return $"Allows ingress from {peers} on {ports}.";
    }

    private static string DescribeEgressRule(V1NetworkPolicyEgressRule rule)
    {
        var peers = DescribePeers(rule.To);
        var ports = DescribePorts(rule.Ports);
        return $"Allows egress to {peers} on {ports}.";
    }

    private static string DescribePeers(IList<V1NetworkPolicyPeer>? peers)
    {
        if (peers is null || peers.Count == 0)
        {
            return "all peers";
        }

        return string.Join("; ", peers.Select(DescribePeer));
    }

    private static string DescribePeer(V1NetworkPolicyPeer peer)
    {
        if (peer.IpBlock is not null)
        {
            var except = peer.IpBlock.Except?.Count > 0
                ? $" except {string.Join(", ", peer.IpBlock.Except)}"
                : string.Empty;
            return $"CIDR {peer.IpBlock.Cidr}{except}";
        }

        var parts = new List<string>();
        if (peer.NamespaceSelector is not null)
        {
            parts.Add($"namespaces [{DescribeLabelSelector(peer.NamespaceSelector)}]");
        }

        if (peer.PodSelector is not null)
        {
            parts.Add($"pods [{DescribeLabelSelector(peer.PodSelector)}]");
        }

        return parts.Count == 0 ? "all peers" : string.Join(" ", parts);
    }

    private static string DescribePorts(IList<V1NetworkPolicyPort>? ports)
    {
        if (ports is null || ports.Count == 0)
        {
            return "all ports";
        }

        return string.Join(", ", ports.Select(port =>
        {
            var protocol = string.IsNullOrWhiteSpace(port.Protocol) ? "TCP" : port.Protocol;
            var portValue = port.Port?.Value ?? "all";
            return $"{protocol}/{portValue}";
        }));
    }

    private static string DescribeLabelSelector(V1LabelSelector selector)
    {
        var parts = new List<string>();

        if (selector.MatchLabels is not null)
        {
            foreach (var matchLabel in selector.MatchLabels)
            {
                parts.Add($"{matchLabel.Key}={matchLabel.Value}");
            }
        }

        foreach (var expression in selector.MatchExpressions ?? [])
        {
            var values = expression.Values is { Count: > 0 }
                ? $" ({string.Join(", ", expression.Values)})"
                : string.Empty;
            parts.Add($"{expression.Key} {expression.OperatorProperty}{values}");
        }

        return parts.Count == 0 ? "all" : string.Join(", ", parts);
    }

    private static V1ServicePort? FindMatchingServicePort(V1Service service, V1IngressServiceBackend? backend)
    {
        if (backend?.Port is null)
        {
            return service.Spec?.Ports?.FirstOrDefault();
        }

        if (backend.Port.Number.HasValue)
        {
            return service.Spec?.Ports?.FirstOrDefault(port => port.Port == backend.Port.Number.Value);
        }

        if (!string.IsNullOrWhiteSpace(backend.Port.Name))
        {
            return service.Spec?.Ports?.FirstOrDefault(port =>
                string.Equals(port.Name, backend.Port.Name, StringComparison.OrdinalIgnoreCase));
        }

        return service.Spec?.Ports?.FirstOrDefault();
    }

    private static string FormatIngressRequestedPort(V1IngressServiceBackend? backend)
    {
        if (backend?.Port?.Number is int number)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(backend?.Port?.Name))
        {
            return backend.Port.Name;
        }

        return "unspecified";
    }

    private static bool ServiceTargetsAnyPod(V1Service service, IReadOnlyList<V1Pod> pods)
    {
        if (pods.Count == 0)
        {
            return false;
        }

        return pods.Any(pod => MatchesLabels(service.Spec?.Selector, pod.Metadata?.Labels));
    }

    private static IReadOnlyList<string> FindHttpRoutesReferencingServices(
        IReadOnlyList<HttpRouteInfo> routes,
        IReadOnlyList<string> services)
    {
        if (services.Count == 0)
        {
            return [];
        }

        var serviceSet = services.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<string>();

        foreach (var route in routes)
        {
            var referencesService = route.BackendRefs.Any(backendRef =>
            {
                var serviceName = backendRef.Contains(':', StringComparison.Ordinal)
                    ? backendRef[..backendRef.IndexOf(':', StringComparison.Ordinal)]
                    : backendRef;
                return serviceSet.Contains(serviceName);
            });

            if (referencesService && !string.IsNullOrWhiteSpace(route.Name))
            {
                var label = route.Hostnames.Count > 0
                    ? $"{route.Name} ({string.Join(", ", route.Hostnames)})"
                    : route.Name;
                results.Add(label);
            }
        }

        return results.Distinct(StringComparer.Ordinal).ToList();
    }

    private static IReadOnlyList<string> FindIngressesReferencingServices(
        IEnumerable<V1Ingress> ingresses,
        IReadOnlyList<string> services)
    {
        if (services.Count == 0)
        {
            return [];
        }

        var serviceSet = services.ToHashSet(StringComparer.Ordinal);
        var results = new List<string>();

        foreach (var ingress in ingresses)
        {
            var referencesService = ingress.Spec?.Rules?.Any(rule =>
                rule.Http?.Paths?.Any(path =>
                    !string.IsNullOrWhiteSpace(path.Backend?.Service?.Name)
                    && serviceSet.Contains(path.Backend.Service.Name)) == true) == true;

            if (!referencesService && !string.IsNullOrWhiteSpace(ingress.Spec?.DefaultBackend?.Service?.Name))
            {
                referencesService = serviceSet.Contains(ingress.Spec.DefaultBackend.Service.Name);
            }

            if (referencesService && !string.IsNullOrWhiteSpace(ingress.Metadata?.Name))
            {
                results.Add(ingress.Metadata.Name);
            }
        }

        return results.Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool NetworkPolicyTargetsAnyPod(V1NetworkPolicy policy, IReadOnlyList<V1Pod> pods)
    {
        if (pods.Count == 0)
        {
            return false;
        }

        return pods.Any(pod => MatchesLabelSelector(policy.Spec?.PodSelector, pod.Metadata?.Labels));
    }

    private static bool MatchesLabels(
        IDictionary<string, string>? requiredLabels,
        IDictionary<string, string>? actualLabels)
    {
        if (requiredLabels is null || requiredLabels.Count == 0)
        {
            return false;
        }

        if (actualLabels is null || actualLabels.Count == 0)
        {
            return false;
        }

        return requiredLabels.All(label =>
            actualLabels.TryGetValue(label.Key, out var value)
            && string.Equals(value, label.Value, StringComparison.Ordinal));
    }

    private static bool MatchesLabelSelector(V1LabelSelector? selector, IDictionary<string, string>? labels)
    {
        var matchLabels = selector?.MatchLabels;
        var matchExpressions = selector?.MatchExpressions;

        if ((matchLabels is null || matchLabels.Count == 0)
            && (matchExpressions is null || matchExpressions.Count == 0))
        {
            return true;
        }

        if (labels is null)
        {
            return false;
        }

        if (matchLabels is not null)
        {
            foreach (var label in matchLabels)
            {
                if (!labels.TryGetValue(label.Key, out var value)
                    || !string.Equals(value, label.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        if (matchExpressions is not null)
        {
            foreach (var expression in matchExpressions)
            {
                labels.TryGetValue(expression.Key, out var value);

                switch (expression.OperatorProperty)
                {
                    case "In":
                        if (value is null || expression.Values is null || !expression.Values.Contains(value, StringComparer.Ordinal))
                            return false;
                        break;
                    case "NotIn":
                        if (value is not null && expression.Values is not null && expression.Values.Contains(value, StringComparer.Ordinal))
                            return false;
                        break;
                    case "Exists":
                        if (!labels.ContainsKey(expression.Key))
                            return false;
                        break;
                    case "DoesNotExist":
                        if (labels.ContainsKey(expression.Key))
                            return false;
                        break;
                    default:
                        return false;
                }
            }
        }

        return true;
    }

    public async Task<IReadOnlyList<GatewayClassInfo>> GetGatewayClassesAsync(CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await ListClusterGatewayApiCustomObjectsAsync("gatewayclasses", ct).ConfigureAwait(false);
            if (result is null)
                return [];

            var json = JsonSerializer.Serialize(result);
            using var doc = JsonDocument.Parse(json);
            return MapGatewayClasses(doc.RootElement);
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await ListGatewayApiCustomObjectsAsync(ns, "gateways", ct).ConfigureAwait(false);
            if (result is null)
                return [];

            var json = JsonSerializer.Serialize(result);
            using var doc = JsonDocument.Parse(json);
            return MapGateways(doc.RootElement, ns);
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default)
    {
        return await WithAuthRetryAsync(async () =>
        {
            var result = await ListGatewayApiCustomObjectsAsync(ns, "httproutes", ct).ConfigureAwait(false);
            if (result is null)
                return [];

            var json = JsonSerializer.Serialize(result);
            using var doc = JsonDocument.Parse(json);
            return MapHttpRoutes(doc.RootElement, ns);
        }).ConfigureAwait(false);
    }

    private async Task<object?> ListGatewayApiCustomObjectsAsync(string ns, string plural, CancellationToken ct)
    {
        foreach (var version in GatewayApiVersions)
        {
            try
            {
                return await _client.CustomObjects.ListNamespacedCustomObjectAsync(
                    GatewayApiGroup,
                    version,
                    ns,
                    plural,
                    cancellationToken: ct).ConfigureAwait(false);
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }

        return null;
    }

    private async Task DeleteGatewayApiCustomObjectAsync(string ns, string plural, string name, CancellationToken ct)
    {
        foreach (var version in GatewayApiVersions)
        {
            try
            {
                await _client.CustomObjects.DeleteNamespacedCustomObjectAsync(
                    GatewayApiGroup,
                    version,
                    ns,
                    plural,
                    name,
                    cancellationToken: ct).ConfigureAwait(false);
                return;
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }
    }

    private async Task<object?> ListClusterGatewayApiCustomObjectsAsync(string plural, CancellationToken ct)
    {
        foreach (var version in GatewayApiVersions)
        {
            try
            {
                return await _client.CustomObjects.ListClusterCustomObjectAsync(
                    GatewayApiGroup,
                    version,
                    plural,
                    cancellationToken: ct).ConfigureAwait(false);
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }

        return null;
    }

    private async Task<object> ReadGatewayApiCustomObjectAsync(string ns, string plural, string name, CancellationToken ct)
    {
        foreach (var version in GatewayApiVersions)
        {
            try
            {
                return await _client.CustomObjects.GetNamespacedCustomObjectAsync(
                    GatewayApiGroup,
                    version,
                    ns,
                    plural,
                    name,
                    cancellationToken: ct).ConfigureAwait(false);
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }

        throw new InvalidOperationException(
            $"Gateway API resource '{plural}/{name}' is not available in namespace '{ns}'.");
    }

    private async Task<object> ReadClusterGatewayApiCustomObjectAsync(string plural, string name, CancellationToken ct)
    {
        foreach (var version in GatewayApiVersions)
        {
            try
            {
                return await _client.CustomObjects.GetClusterCustomObjectAsync(
                    GatewayApiGroup,
                    version,
                    plural,
                    name,
                    cancellationToken: ct).ConfigureAwait(false);
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }

        throw new InvalidOperationException(
            $"Gateway API resource '{plural}/{name}' is not available at cluster scope.");
    }

    private static List<GatewayClassInfo> MapGatewayClasses(JsonElement root)
    {
        if (!TryGetProperty(root, "items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var gatewayClasses = new List<GatewayClassInfo>();

        foreach (var item in items.EnumerateArray())
        {
            var name = GetMetadataName(item);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            gatewayClasses.Add(new GatewayClassInfo
            {
                Name = name,
                ControllerName = TryGetProperty(item, "spec", out var spec)
                    ? GetStringProperty(spec, "controllerName")
                    : null,
                Status = GetGatewayClassStatus(item),
                Description = TryGetProperty(item, "spec", out spec)
                    ? GetStringProperty(spec, "description")
                    : null,
                ParametersReference = GetGatewayClassParametersReference(item),
                IsDefault = string.Equals(
                    GetMetadataAnnotationValue(item, "gateway.networking.k8s.io/default-gatewayclass"),
                    bool.TrueString,
                    StringComparison.OrdinalIgnoreCase),
                Labels = GetMetadataLabels(item)
            });
        }

        return gatewayClasses
            .OrderBy(gatewayClass => gatewayClass.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static List<GatewayInfo> MapGateways(JsonElement root, string fallbackNamespace)
    {
        if (!TryGetProperty(root, "items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var gateways = new List<GatewayInfo>();

        foreach (var item in items.EnumerateArray())
        {
            var name = GetMetadataName(item);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var listenerRouteCounts = GetGatewayListenerRouteCounts(item);
            var listeners = GetGatewayListeners(item, listenerRouteCounts);
            var addresses = GetGatewayAddresses(item);

            gateways.Add(new GatewayInfo
            {
                Name = name,
                Namespace = GetMetadataNamespace(item, fallbackNamespace),
                GatewayClassName = TryGetProperty(item, "spec", out var spec)
                    ? GetStringProperty(spec, "gatewayClassName")
                    : null,
                Status = GetGatewayStatus(item, addresses),
                AttachedRoutes = listeners.Sum(listener => listener.AttachedRoutes),
                Addresses = addresses,
                Listeners = listeners,
                Labels = GetMetadataLabels(item)
            });
        }

        return gateways
            .OrderBy(gateway => gateway.Namespace, StringComparer.Ordinal)
            .ThenBy(gateway => gateway.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static List<HttpRouteInfo> MapHttpRoutes(JsonElement root, string fallbackNamespace)
    {
        if (!TryGetProperty(root, "items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var routes = new List<HttpRouteInfo>();

        foreach (var item in items.EnumerateArray())
        {
            var name = GetMetadataName(item);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var routeNamespace = GetMetadataNamespace(item, fallbackNamespace);
            routes.Add(new HttpRouteInfo
            {
                Name = name,
                Namespace = routeNamespace,
                Status = GetHttpRouteStatus(item),
                Hostnames = GetHttpRouteHostnames(item),
                ParentRefs = GetHttpRouteParentRefs(item, routeNamespace),
                BackendRefs = GetHttpRouteBackendRefs(item, routeNamespace),
                Labels = GetMetadataLabels(item)
            });
        }

        return routes
            .OrderBy(route => route.Namespace, StringComparer.Ordinal)
            .ThenBy(route => route.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static List<GatewayListenerInfo> GetGatewayListeners(
        JsonElement item,
        IReadOnlyDictionary<string, int> listenerRouteCounts)
    {
        if (!TryGetProperty(item, "spec", out var spec)
            || !TryGetProperty(spec, "listeners", out var listeners)
            || listeners.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<GatewayListenerInfo>();
        foreach (var listener in listeners.EnumerateArray())
        {
            var listenerName = GetStringProperty(listener, "name");
            if (string.IsNullOrWhiteSpace(listenerName))
                continue;

            results.Add(new GatewayListenerInfo
            {
                Name = listenerName,
                Port = GetIntProperty(listener, "port"),
                Protocol = GetStringProperty(listener, "protocol"),
                Hostname = GetStringProperty(listener, "hostname"),
                AttachedRoutes = listenerRouteCounts.TryGetValue(listenerName, out var attachedRoutes)
                    ? attachedRoutes
                    : 0
            });
        }

        return results;
    }

    private static Dictionary<string, int> GetGatewayListenerRouteCounts(JsonElement item)
    {
        var results = new Dictionary<string, int>(StringComparer.Ordinal);

        if (!TryGetProperty(item, "status", out var status)
            || !TryGetProperty(status, "listeners", out var listeners)
            || listeners.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var listener in listeners.EnumerateArray())
        {
            var listenerName = GetStringProperty(listener, "name");
            if (string.IsNullOrWhiteSpace(listenerName))
                continue;

            results[listenerName] = GetIntProperty(listener, "attachedRoutes");
        }

        return results;
    }

    private static List<string> GetGatewayAddresses(JsonElement item)
    {
        if (!TryGetProperty(item, "status", out var status)
            || !TryGetProperty(status, "addresses", out var addresses)
            || addresses.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return addresses.EnumerateArray()
            .Select(address => GetStringProperty(address, "value"))
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetGatewayStatus(JsonElement item, IReadOnlyList<string> addresses)
    {
        if (HasTopLevelCondition(item, "Programmed"))
            return "Programmed";

        if (HasTopLevelCondition(item, "Accepted"))
            return "Accepted";

        if (TryGetFirstTopLevelFailingCondition(item, out var failingCondition))
            return failingCondition;

        return addresses.Count > 0 ? "Addressed" : "Pending";
    }

    private static List<string> GetHttpRouteHostnames(JsonElement item)
    {
        if (!TryGetProperty(item, "spec", out var spec)
            || !TryGetProperty(spec, "hostnames", out var hostnames)
            || hostnames.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return hostnames.EnumerateArray()
            .Where(hostname => hostname.ValueKind == JsonValueKind.String)
            .Select(hostname => hostname.GetString())
            .Where(hostname => !string.IsNullOrWhiteSpace(hostname))
            .Select(hostname => hostname!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetHttpRouteParentRefs(JsonElement item, string routeNamespace)
    {
        if (!TryGetProperty(item, "spec", out var spec)
            || !TryGetProperty(spec, "parentRefs", out var parents)
            || parents.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return parents.EnumerateArray()
            .Select(parent => FormatParentRef(parent, routeNamespace))
            .Where(parent => !string.IsNullOrWhiteSpace(parent))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatParentRef(JsonElement parent, string routeNamespace)
    {
        var name = GetStringProperty(parent, "name");
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var kind = GetStringProperty(parent, "kind");
        var parentNamespace = GetStringProperty(parent, "namespace");
        var sectionName = GetStringProperty(parent, "sectionName");

        var prefix = !string.IsNullOrWhiteSpace(kind) && !string.Equals(kind, "Gateway", StringComparison.OrdinalIgnoreCase)
            ? $"{kind}/"
            : string.Empty;
        var namespacePrefix = !string.IsNullOrWhiteSpace(parentNamespace)
            && !string.Equals(parentNamespace, routeNamespace, StringComparison.Ordinal)
            ? $"{parentNamespace}/"
            : string.Empty;

        return $"{prefix}{namespacePrefix}{name}{(string.IsNullOrWhiteSpace(sectionName) ? string.Empty : $"#{sectionName}")}";
    }

    private static List<string> GetHttpRouteBackendRefs(JsonElement item, string routeNamespace)
    {
        if (!TryGetProperty(item, "spec", out var spec)
            || !TryGetProperty(spec, "rules", out var rules)
            || rules.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var backends = new List<string>();

        foreach (var rule in rules.EnumerateArray())
        {
            if (!TryGetProperty(rule, "backendRefs", out var backendRefs)
                || backendRefs.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var backend in backendRefs.EnumerateArray())
            {
                var formatted = FormatBackendRef(backend, routeNamespace);
                if (!string.IsNullOrWhiteSpace(formatted))
                    backends.Add(formatted);
            }
        }

        return backends.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string FormatBackendRef(JsonElement backend, string routeNamespace)
    {
        var name = GetStringProperty(backend, "name");
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var kind = GetStringProperty(backend, "kind");
        var backendNamespace = GetStringProperty(backend, "namespace");
        var port = TryGetIntProperty(backend, "port");

        var prefix = !string.IsNullOrWhiteSpace(kind) && !string.Equals(kind, "Service", StringComparison.OrdinalIgnoreCase)
            ? $"{kind}/"
            : string.Empty;
        var namespacePrefix = !string.IsNullOrWhiteSpace(backendNamespace)
            && !string.Equals(backendNamespace, routeNamespace, StringComparison.Ordinal)
            ? $"{backendNamespace}/"
            : string.Empty;

        return $"{prefix}{namespacePrefix}{name}{(port.HasValue ? $":{port.Value}" : string.Empty)}";
    }

    private static string GetHttpRouteStatus(JsonElement item)
    {
        if (HasParentCondition(item, "Accepted"))
            return "Accepted";

        if (HasParentCondition(item, "ResolvedRefs"))
            return "ResolvedRefs";

        if (TryGetFirstFailingParentCondition(item, out var failingCondition))
            return failingCondition;

        return "Pending";
    }

    private static bool HasTopLevelCondition(JsonElement item, string type)
    {
        if (!TryGetProperty(item, "status", out var status)
            || !TryGetProperty(status, "conditions", out var conditions)
            || conditions.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return conditions.EnumerateArray().Any(condition =>
            string.Equals(GetStringProperty(condition, "type"), type, StringComparison.OrdinalIgnoreCase)
            && string.Equals(GetStringProperty(condition, "status"), "True", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetFirstTopLevelFailingCondition(JsonElement item, out string conditionType)
    {
        if (!TryGetProperty(item, "status", out var status)
            || !TryGetProperty(status, "conditions", out var conditions)
            || conditions.ValueKind != JsonValueKind.Array)
        {
            conditionType = string.Empty;
            return false;
        }

        foreach (var condition in conditions.EnumerateArray())
        {
            if (!string.Equals(GetStringProperty(condition, "status"), "True", StringComparison.OrdinalIgnoreCase))
            {
                conditionType = GetStringProperty(condition, "type") ?? "Pending";
                return true;
            }
        }

        conditionType = string.Empty;
        return false;
    }

    private static bool HasParentCondition(JsonElement item, string type)
    {
        if (!TryGetProperty(item, "status", out var status)
            || !TryGetProperty(status, "parents", out var parents)
            || parents.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var parent in parents.EnumerateArray())
        {
            if (!TryGetProperty(parent, "conditions", out var conditions)
                || conditions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            if (conditions.EnumerateArray().Any(condition =>
                string.Equals(GetStringProperty(condition, "type"), type, StringComparison.OrdinalIgnoreCase)
                && string.Equals(GetStringProperty(condition, "status"), "True", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetFirstFailingParentCondition(JsonElement item, out string conditionType)
    {
        if (!TryGetProperty(item, "status", out var status)
            || !TryGetProperty(status, "parents", out var parents)
            || parents.ValueKind != JsonValueKind.Array)
        {
            conditionType = string.Empty;
            return false;
        }

        foreach (var parent in parents.EnumerateArray())
        {
            if (!TryGetProperty(parent, "conditions", out var conditions)
                || conditions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var condition in conditions.EnumerateArray())
            {
                if (!string.Equals(GetStringProperty(condition, "status"), "True", StringComparison.OrdinalIgnoreCase))
                {
                    conditionType = GetStringProperty(condition, "type") ?? "Pending";
                    return true;
                }
            }
        }

        conditionType = string.Empty;
        return false;
    }

    private static string SerializeCustomObjectYaml(object resource)
    {
        var json = JsonSerializer.Serialize(resource);
        using var document = JsonDocument.Parse(json);
        var serializer = new SerializerBuilder().Build();
        return serializer.Serialize(ConvertJsonElement(document.RootElement));
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var longValue)
                ? longValue
                : element.TryGetDouble(out var doubleValue)
                    ? doubleValue
                    : element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.ToString()
        };
    }


    private static string GetGatewayClassStatus(JsonElement item)
    {
        if (HasTopLevelCondition(item, "Accepted"))
            return "Accepted";

        if (HasTopLevelCondition(item, "SupportedVersion"))
            return "SupportedVersion";

        if (TryGetFirstTopLevelFailingCondition(item, out var failingCondition))
            return failingCondition;

        return "Pending";
    }

    private static string? GetGatewayClassParametersReference(JsonElement item)
    {
        if (!TryGetProperty(item, "spec", out var spec)
            || !TryGetProperty(spec, "parametersRef", out var parametersRef)
            || parametersRef.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = GetStringProperty(parametersRef, "name");
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var kind = GetStringProperty(parametersRef, "kind");
        var group = GetStringProperty(parametersRef, "group");
        var ns = GetStringProperty(parametersRef, "namespace");
        var typePrefix = string.IsNullOrWhiteSpace(kind)
            ? group
            : string.IsNullOrWhiteSpace(group)
                ? kind
                : $"{group}/{kind}";
        var nameRef = string.IsNullOrWhiteSpace(ns) ? name : $"{ns}/{name}";

        return string.IsNullOrWhiteSpace(typePrefix)
            ? nameRef
            : $"{typePrefix} {nameRef}";
    }
    private static bool TryGetProperty(JsonElement parent, string propertyName, out JsonElement value)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(propertyName, out value))
            return true;

        value = default;
        return false;
    }

    private static string? GetStringProperty(JsonElement parent, string propertyName)
    {
        if (!TryGetProperty(parent, propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static int GetIntProperty(JsonElement parent, string propertyName)
        => TryGetIntProperty(parent, propertyName) ?? 0;

    private static int? TryGetIntProperty(JsonElement parent, string propertyName)
    {
        if (!TryGetProperty(parent, propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
            return intValue;

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out intValue))
            return intValue;

        return null;
    }

    private static string GetMetadataNamespace(JsonElement item, string fallbackNamespace)
    {
        if (TryGetProperty(item, "metadata", out var metadata))
        {
            var itemNamespace = GetStringProperty(metadata, "namespace");
            if (!string.IsNullOrWhiteSpace(itemNamespace))
                return itemNamespace;
        }

        return fallbackNamespace;
    }

    private static string? GetMetadataName(JsonElement item)
    {
        if (!TryGetProperty(item, "metadata", out var metadata))
            return null;

        return GetStringProperty(metadata, "name");
    }

    private static Dictionary<string, string> GetMetadataLabels(JsonElement item)
    {
        if (!TryGetProperty(item, "metadata", out var metadata)
            || !TryGetProperty(metadata, "labels", out var labels)
            || labels.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in labels.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
                result[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return result;
    }

    private static string? GetMetadataAnnotationValue(JsonElement item, string annotationName)
    {
        if (!TryGetProperty(item, "metadata", out var metadata)
            || !TryGetProperty(metadata, "annotations", out var annotations)
            || annotations.ValueKind != JsonValueKind.Object
            || !annotations.TryGetProperty(annotationName, out var annotationValue))
        {
            return null;
        }

        return annotationValue.ValueKind == JsonValueKind.String
            ? annotationValue.GetString()
            : annotationValue.ToString();
    }
}
