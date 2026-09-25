using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

public partial class DemoAksClient
{
    public async Task<IReadOnlyList<IngressInfo>> GetIngressesAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(200, ct).ConfigureAwait(false);
        return new List<IngressInfo>
        {
            new()
            {
                Name = "main-ingress", Namespace = ns, IngressClass = "nginx",
                Addresses = ["20.93.141.52"],
                Rules =
                [
                    new IngressRule
                    {
                        Host = "api.ecommerce.example.com",
                        Paths =
                        [
                            new IngressPath { Path = "/orders", PathType = "Prefix", ServiceName = "order-api", ServicePort = 80 },
                            new IngressPath { Path = "/products", PathType = "Prefix", ServiceName = "product-catalog", ServicePort = 80 },
                            new IngressPath { Path = "/cart", PathType = "Prefix", ServiceName = "cart-api", ServicePort = 80 },
                            new IngressPath { Path = "/auth", PathType = "Prefix", ServiceName = "auth-service", ServicePort = 80 },
                        ]
                    }
                ],
                Labels = new Dictionary<string, string> { ["app.kubernetes.io/managed-by"] = "Helm" }
            },
            new()
            {
                Name = "admin-ingress", Namespace = ns, IngressClass = "nginx",
                Addresses = ["20.93.141.52"],
                Rules =
                [
                    new IngressRule
                    {
                        Host = "admin.ecommerce.example.com",
                        Paths =
                        [
                            new IngressPath { Path = "/", PathType = "Prefix", ServiceName = "admin-dashboard", ServicePort = 8080 },
                        ]
                    }
                ],
                Labels = new Dictionary<string, string> { ["app.kubernetes.io/managed-by"] = "Helm" }
            },
            new()
            {
                Name = "monitoring-ingress", Namespace = ns, IngressClass = "nginx",
                Addresses = ["20.93.141.53"],
                Rules =
                [
                    new IngressRule
                    {
                        Host = "grafana.internal.example.com",
                        Paths =
                        [
                            new IngressPath { Path = "/", PathType = "Prefix", ServiceName = "grafana", ServicePort = 3000 },
                        ]
                    },
                    new IngressRule
                    {
                        Host = "prometheus.internal.example.com",
                        Paths =
                        [
                            new IngressPath { Path = "/", PathType = "Prefix", ServiceName = "prometheus-server", ServicePort = 9090 },
                        ]
                    }
                ],
                Labels = new Dictionary<string, string> { ["app.kubernetes.io/managed-by"] = "Helm", ["tier"] = "monitoring" }
            }
        };
    }

    public async Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(120, ct).ConfigureAwait(false);
        return new List<ServiceInfo>
        {
            new()
            {
                Name = "order-api",
                Namespace = ns,
                Type = "ClusterIP",
                ClusterIp = "10.0.12.10",
                Ports =
                [
                    new ServicePortInfo { Name = "http", Protocol = "TCP", Port = 80, TargetPort = "8080" }
                ],
                SelectorLabels = new Dictionary<string, string> { ["app"] = "order-api" },
                Labels = new Dictionary<string, string> { ["app.kubernetes.io/name"] = "order-api" }
            },
            new()
            {
                Name = "payment-gateway",
                Namespace = ns,
                Type = "ClusterIP",
                ClusterIp = "10.0.12.24",
                Ports =
                [
                    new ServicePortInfo { Name = "http", Protocol = "TCP", Port = 80, TargetPort = "8080" }
                ],
                SelectorLabels = new Dictionary<string, string> { ["app"] = "payment-gateway" },
                Labels = new Dictionary<string, string> { ["app.kubernetes.io/name"] = "payment-gateway" }
            },
            new()
            {
                Name = "ingress-nginx-controller",
                Namespace = ns,
                Type = "LoadBalancer",
                ClusterIp = "10.0.12.52",
                ExternalAddresses = ["20.93.141.52"],
                Ports =
                [
                    new ServicePortInfo { Name = "http", Protocol = "TCP", Port = 80, TargetPort = "80" },
                    new ServicePortInfo { Name = "https", Protocol = "TCP", Port = 443, TargetPort = "443" }
                ],
                SelectorLabels = new Dictionary<string, string> { ["app.kubernetes.io/name"] = "ingress-nginx" },
                Labels = new Dictionary<string, string> { ["app.kubernetes.io/managed-by"] = "Helm" }
            },
            new()
            {
                Name = "prometheus-server",
                Namespace = ns,
                Type = "ClusterIP",
                ClusterIp = "10.0.12.88",
                Ports =
                [
                    new ServicePortInfo { Name = "http", Protocol = "TCP", Port = 9090, TargetPort = "9090" }
                ],
                SelectorLabels = new Dictionary<string, string> { ["app"] = "prometheus-server" },
                Labels = new Dictionary<string, string> { ["tier"] = "monitoring" }
            }
        };
    }

    public async Task<IngressAnalysis> AnalyzeIngressAsync(string ns, string ingressName, CancellationToken ct = default)
    {
        await Task.Delay(120, ct).ConfigureAwait(false);

        var ingress = (await GetIngressesAsync(ns, ct).ConfigureAwait(false)).FirstOrDefault(item =>
            string.Equals(item.Name, ingressName, StringComparison.Ordinal));
        if (ingress is null)
        {
            throw new InvalidOperationException($"Ingress '{ingressName}' was not found in namespace '{ns}'.");
        }

        var services = (await GetServicesAsync(ns, ct).ConfigureAwait(false)).ToDictionary(service => service.Name, StringComparer.Ordinal);
        var pods = BuildDemoPods(ns, Math.Max(Volatile.Read(ref _demoTick), 1)).ToList();
        var backends = new List<IngressBackendAnalysis>();

        foreach (var rule in ingress.Rules)
        {
            foreach (var path in rule.Paths)
            {
                services.TryGetValue(path.ServiceName ?? string.Empty, out var service);
                var matchingPods = service?.SelectorLabels.Count > 0
                    ? pods.Where(pod => service.SelectorLabels.All(selector =>
                        pod.Labels.TryGetValue(selector.Key, out var value)
                        && string.Equals(value, selector.Value, StringComparison.Ordinal))).ToList()
                    : [];

                var requestedPort = path.ServicePort?.ToString() ?? "unspecified";
                var matchingPort = service?.Ports.FirstOrDefault(port => port.Port == path.ServicePort);
                var backendFindings = new List<string>();

                if (service is null)
                {
                    backendFindings.Add($"Service '{path.ServiceName ?? "(missing)"}' was not found.");
                }
                else
                {
                    if (matchingPort is null)
                    {
                        backendFindings.Add($"Requested port '{requestedPort}' does not exist on Service '{service.Name}'.");
                    }

                    if (service.SelectorLabels.Count == 0)
                    {
                        backendFindings.Add($"Service '{service.Name}' has no pod selector, so backend readiness could not be inferred from pods.");
                    }
                    else if (matchingPods.Count == 0)
                    {
                        backendFindings.Add($"Service '{service.Name}' selector matched no pods.");
                    }
                    else
                    {
                        var readyPods = matchingPods.Count(pod => pod.Ready);
                        if (readyPods == 0)
                        {
                            backendFindings.Add($"Service '{service.Name}' matched {matchingPods.Count} pod(s), but none were Ready.");
                        }
                        else if (readyPods < matchingPods.Count)
                        {
                            backendFindings.Add($"Service '{service.Name}' matched {readyPods}/{matchingPods.Count} Ready pod(s).");
                        }
                    }
                }

                backends.Add(new IngressBackendAnalysis
                {
                    Host = rule.Host ?? "*",
                    Path = path.Path,
                    PathType = path.PathType,
                    ServiceName = path.ServiceName,
                    ServiceNamespace = ns,
                    RequestedPort = requestedPort,
                    ServiceExists = service is not null,
                    ServiceType = service?.Type,
                    ServicePortResolved = matchingPort is not null,
                    ResolvedServicePort = matchingPort is null
                        ? null
                        : $"{matchingPort.Port}/{matchingPort.Protocol} → {(matchingPort.TargetPort ?? matchingPort.Port.ToString())}",
                    HasSelector = service?.SelectorLabels.Count > 0,
                    MatchingPodCount = matchingPods.Count,
                    ReadyPodCount = matchingPods.Count(pod => pod.Ready),
                    MatchingPods = matchingPods.Select(pod => pod.Name).Take(6).ToList(),
                    Findings = backendFindings
                });
            }
        }

        var findings = backends.SelectMany(backend => backend.Findings).Distinct(StringComparer.Ordinal).ToList();
        if (ingress.Addresses.Count == 0)
        {
            findings.Insert(0, "The ingress has no published load balancer address yet.");
        }

        if (findings.Count == 0)
        {
            findings.Add("All inspected ingress backends resolved to Services with matching Ready pods.");
        }

        return new IngressAnalysis
        {
            Namespace = ns,
            IngressName = ingress.Name,
            IngressClass = ingress.IngressClass,
            Summary = findings.Count == 1 && findings[0].StartsWith("All inspected", StringComparison.Ordinal)
                ? $"All {backends.Count} inspected ingress backend(s) resolved to Services with matching Ready pods."
                : $"{backends.Count(backend => backend.Findings.Count > 0)} of {backends.Count} inspected ingress backend(s) need attention.",
            Addresses = ingress.Addresses.ToList(),
            Findings = findings,
            Backends = backends
        };
    }

    public async Task<NetworkPolicyAnalysis> AnalyzeNetworkPoliciesAsync(
        string ns,
        string workloadKind,
        string workloadName,
        CancellationToken ct = default)
    {
        await Task.Delay(120, ct).ConfigureAwait(false);

        var pods = BuildDemoPods(ns, Math.Max(Volatile.Read(ref _demoTick), 1)).ToList();
        var selectedPods = workloadKind.Trim().ToLowerInvariant() switch
        {
            "deployment" or "statefulset" => pods.Where(pod =>
                pod.Labels.TryGetValue("app", out var app)
                && string.Equals(app, workloadName, StringComparison.Ordinal)).ToList(),
            "pod" => pods.Where(pod => string.Equals(pod.Name, workloadName, StringComparison.Ordinal)).ToList(),
            _ => throw new NotSupportedException($"Network policy analysis is not supported for workload kind '{workloadKind}'.")
        };

        var selectorLabels = workloadKind.Trim().Equals("pod", StringComparison.OrdinalIgnoreCase)
            ? selectedPods.FirstOrDefault()?.Labels is { Count: > 0 } labels
                ? new Dictionary<string, string>(labels)
                : []
            : new Dictionary<string, string> { ["app"] = workloadName };

        var services = (await GetServicesAsync(ns, ct).ConfigureAwait(false))
            .Where(service => service.SelectorLabels.Count > 0 && selectedPods.Any(pod =>
                service.SelectorLabels.All(selector =>
                    pod.Labels.TryGetValue(selector.Key, out var value)
                    && string.Equals(value, selector.Value, StringComparison.Ordinal))))
            .Select(service => service.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var ingresses = (await GetIngressesAsync(ns, ct).ConfigureAwait(false))
            .Where(ingress => ingress.Rules.Any(rule => rule.Paths.Any(path =>
                !string.IsNullOrWhiteSpace(path.ServiceName)
                && services.Contains(path.ServiceName, StringComparer.Ordinal))))
            .Select(ingress => ingress.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var policies = BuildDemoNetworkPolicies(workloadName);
        var ingressIsolated = policies.Any(policy => policy.PolicyTypes.Contains("Ingress", StringComparer.OrdinalIgnoreCase));
        var egressIsolated = policies.Any(policy => policy.PolicyTypes.Contains("Egress", StringComparer.OrdinalIgnoreCase));

        var findings = new List<string>();
        if (selectedPods.Count == 0)
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

        if (ingresses.Count > 0)
        {
            findings.Add($"Referenced by ingress resources: {string.Join(", ", ingresses)}.");
        }

        if (findings.Count == 0)
        {
            findings.Add("The workload is selected by Services and NetworkPolicy objects with no immediate object-level gaps detected.");
        }

        var summary = selectedPods.Count == 0
            ? "No live pods matched the workload during analysis, so policy impact could not be confirmed from pod evidence."
            : policies.Count == 0
                ? $"No network policies currently select the {selectedPods.Count} matched pod(s)."
                : $"{policies.Count} network polic{(policies.Count == 1 ? "y" : "ies")} select {selectedPods.Count} pod(s). Ingress is {(ingressIsolated ? "isolated" : "open")}; egress is {(egressIsolated ? "isolated" : "open")}.";

        return new NetworkPolicyAnalysis
        {
            Namespace = ns,
            WorkloadKind = workloadKind,
            WorkloadName = workloadName,
            Summary = summary,
            MatchingPodCount = selectedPods.Count,
            MatchingPods = selectedPods.Select(pod => pod.Name).Take(6).ToList(),
            SelectorLabels = selectorLabels,
            Services = services,
            ExposedByIngresses = ingresses,
            ExposedByHttpRoutes = [],
            IngressIsolated = ingressIsolated,
            EgressIsolated = egressIsolated,
            Findings = findings,
            Policies = policies
        };
    }

    private static List<NetworkPolicyMatch> BuildDemoNetworkPolicies(string workloadName)
    {
        if (string.Equals(workloadName, "order-api", StringComparison.Ordinal))
        {
            return
            [
                new NetworkPolicyMatch
                {
                    Name = "order-api-allow-from-ingress",
                    PolicyTypes = ["Ingress"],
                    IngressRules = ["Allows ingress from namespaces [app.kubernetes.io/name=ingress-nginx] on TCP/8080."]
                },
                new NetworkPolicyMatch
                {
                    Name = "order-api-egress-dependencies",
                    PolicyTypes = ["Egress"],
                    EgressRules = ["Allows egress to namespaces [team=platform] on TCP/443, TCP/5671."]
                }
            ];
        }

        if (string.Equals(workloadName, "payment-gateway", StringComparison.Ordinal))
        {
            return
            [
                new NetworkPolicyMatch
                {
                    Name = "payment-gateway-restrict-egress",
                    PolicyTypes = ["Egress"],
                    EgressRules = ["Allows egress to CIDR 10.20.0.0/24 on TCP/443."]
                }
            ];
        }

        if (string.Equals(workloadName, "search-indexer", StringComparison.Ordinal))
        {
            return
            [
                new NetworkPolicyMatch
                {
                    Name = "search-indexer-default-deny",
                    PolicyTypes = ["Ingress", "Egress"]
                }
            ];
        }

        return [];
    }

    public async Task<IReadOnlyList<GatewayClassInfo>> GetGatewayClassesAsync(CancellationToken ct = default)
    {
        await Task.Delay(120, ct).ConfigureAwait(false);
        return new List<GatewayClassInfo>
        {
            new()
            {
                Name = "envoy-gateway",
                ControllerName = "gateway.envoyproxy.io/gatewayclass-controller",
                Status = "Accepted",
                Description = "Default Envoy Gateway class for internet-facing traffic.",
                ParametersReference = "gateway.envoyproxy.io/EnvoyProxy infrastructure/envoy-gateway-config",
                IsDefault = true,
                Labels = new Dictionary<string, string>
                {
                    ["app.kubernetes.io/managed-by"] = "Helm"
                }
            },
            new()
            {
                Name = "envoy-internal",
                ControllerName = "gateway.envoyproxy.io/gatewayclass-controller",
                Status = "Accepted",
                Description = "Internal Envoy Gateway class for private workloads.",
                ParametersReference = "gateway.envoyproxy.io/EnvoyProxy infrastructure/envoy-internal-config",
                Labels = new Dictionary<string, string>
                {
                    ["tier"] = "internal"
                }
            }
        };
    }

    public async Task<IReadOnlyList<GatewayInfo>> GetGatewaysAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(180, ct).ConfigureAwait(false);
        return new List<GatewayInfo>
        {
            new()
            {
                Name = "public-gateway",
                Namespace = ns,
                GatewayClassName = "envoy-gateway",
                Status = "Programmed",
                AttachedRoutes = 2,
                Addresses = ["20.93.141.52"],
                Listeners =
                [
                    new GatewayListenerInfo
                    {
                        Name = "https-api",
                        Port = 443,
                        Protocol = "HTTPS",
                        Hostname = "api.ecommerce.example.com",
                        AttachedRoutes = 1
                    },
                    new GatewayListenerInfo
                    {
                        Name = "https-admin",
                        Port = 443,
                        Protocol = "HTTPS",
                        Hostname = "admin.ecommerce.example.com",
                        AttachedRoutes = 1
                    }
                ],
                Labels = new Dictionary<string, string>
                {
                    ["gateway.envoyproxy.io/managed"] = "true",
                    ["app.kubernetes.io/managed-by"] = "Helm"
                }
            },
            new()
            {
                Name = "internal-gateway",
                Namespace = ns,
                GatewayClassName = "envoy-gateway",
                Status = "Accepted",
                AttachedRoutes = 1,
                Addresses = ["10.0.12.24"],
                Listeners =
                [
                    new GatewayListenerInfo
                    {
                        Name = "http-metrics",
                        Port = 80,
                        Protocol = "HTTP",
                        Hostname = "metrics.internal.example.com",
                        AttachedRoutes = 1
                    }
                ],
                Labels = new Dictionary<string, string>
                {
                    ["gateway.envoyproxy.io/managed"] = "true",
                    ["tier"] = "internal"
                }
            }
        };
    }

    public virtual async Task<IReadOnlyList<HttpRouteInfo>> GetHttpRoutesAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(180, ct).ConfigureAwait(false);
        return new List<HttpRouteInfo>
        {
            new()
            {
                Name = "orders-api-route",
                Namespace = ns,
                Status = "Accepted",
                Hostnames = ["api.ecommerce.example.com"],
                ParentRefs = ["public-gateway#https-api"],
                BackendRefs = ["order-api:80"],
                Rules =
                [
                    new HttpRouteRuleInfo
                    {
                        Matches = ["PathPrefix /orders GET"],
                        Filters = ["RequestHeaderModifier", "ExtensionRef → HTTPRouteFilter/strip-api-prefix"],
                        BackendRefs = ["order-api:80 w=90", "order-api-canary:80 w=10"],
                        BackendRequestTimeout = "15s"
                    },
                    new HttpRouteRuleInfo
                    {
                        Matches = ["PathPrefix /health"],
                        BackendRefs = ["order-api:80"],
                        RequestTimeout = "5s"
                    }
                ],
                ParentStatuses =
                [
                    new HttpRouteParentStatus { ParentRef = "public-gateway#https-api", Status = "Accepted" }
                ],
                Labels = new Dictionary<string, string>
                {
                    ["app.kubernetes.io/name"] = "order-api"
                }
            },
            new()
            {
                Name = "admin-ui-route",
                Namespace = ns,
                Status = "Accepted",
                Hostnames = ["admin.ecommerce.example.com"],
                ParentRefs = ["public-gateway#https-admin"],
                BackendRefs = ["admin-dashboard:8080"],
                Rules =
                [
                    new HttpRouteRuleInfo
                    {
                        Matches = ["PathPrefix /"],
                        Filters = ["RequestRedirect → https (301)"],
                        BackendRefs = ["admin-dashboard:8080"]
                    }
                ],
                ParentStatuses =
                [
                    new HttpRouteParentStatus { ParentRef = "public-gateway#https-admin", Status = "Accepted" }
                ],
                Labels = new Dictionary<string, string>
                {
                    ["app.kubernetes.io/name"] = "admin-dashboard"
                }
            },
            new()
            {
                Name = "metrics-route",
                Namespace = ns,
                Status = "ResolvedRefs",
                Hostnames = ["metrics.internal.example.com"],
                ParentRefs = ["internal-gateway#http-metrics"],
                BackendRefs = ["prometheus-server:9090"],
                Rules =
                [
                    new HttpRouteRuleInfo
                    {
                        Matches = ["PathPrefix /metrics"],
                        BackendRefs = ["prometheus-server:9090"]
                    }
                ],
                ParentStatuses =
                [
                    new HttpRouteParentStatus { ParentRef = "internal-gateway#http-metrics", Status = "Accepted" },
                    new HttpRouteParentStatus { ParentRef = "internal-gateway#http-metrics", Status = "ResolvedRefs", Reason = "BackendNotFound" }
                ],
                Labels = new Dictionary<string, string>
                {
                    ["app.kubernetes.io/name"] = "prometheus-server"
                }
            }
        };
    }

    public async Task DeleteIngressAsync(string ns, string name, CancellationToken ct = default)
    {
        await Task.Delay(300, ct).ConfigureAwait(false); // simulate delete
    }

    public async Task DeleteHttpRouteAsync(string ns, string name, CancellationToken ct = default)
    {
        await Task.Delay(300, ct).ConfigureAwait(false); // simulate delete
    }

    public async Task<IReadOnlyList<EnvoyResourceInfo>> GetEnvoyResourcesAsync(string ns, string plural, CancellationToken ct = default)
    {
        await Task.Delay(120, ct).ConfigureAwait(false);
        return BuildDemoEnvoyResources(ns, plural);
    }

    /// <summary>Plural or kind name → proper Kind name for the Envoy Gateway resources demo data covers.</summary>
    private static string? TryResolveDemoEnvoyKind(string kind)
    {
        return kind.ToLowerInvariant() switch
        {
            "backends" or "backend" => "Backend",
            "backendtrafficpolicies" or "backendtrafficpolicy" => "BackendTrafficPolicy",
            "clienttrafficpolicies" or "clienttrafficpolicy" => "ClientTrafficPolicy",
            "envoyextensionpolicies" or "envoyextensionpolicy" => "EnvoyExtensionPolicy",
            "envoypatchpolicies" or "envoypatchpolicy" => "EnvoyPatchPolicy",
            "envoyproxies" or "envoyproxy" => "EnvoyProxy",
            "httproutefilters" or "httproutefilter" => "HTTPRouteFilter",
            "securitypolicies" or "securitypolicy" => "SecurityPolicy",
            _ => null
        };
    }

    private static string BuildDemoEnvoyYaml(string kind, string ns, string name)
    {
        var spec = kind switch
        {
            "BackendTrafficPolicy" => """
                    targetRefs:
                    - group: gateway.networking.k8s.io
                        kind: HTTPRoute
                        name: orders-api-route
                    circuitBreaker:
                        maxConnections: 1024
                        maxPendingRequests: 512
                    timeout:
                        http:
                            requestTimeout: 15s
                    rateLimit:
                        type: Global
                """,
            "SecurityPolicy" => """
                    targetRefs:
                    - group: gateway.networking.k8s.io
                        kind: HTTPRoute
                        name: orders-api-route
                    jwt:
                        providers:
                        - name: primary
                            issuer: https://login.ecommerce.example.com/
                    authorization:
                        defaultAction: Allow
                        rules:
                        - action: Allow
                            name: authenticated
                """,
            "ClientTrafficPolicy" => """
                    targetRefs:
                    - group: gateway.networking.k8s.io
                        kind: Gateway
                        name: public-gateway
                    tls:
                        minVersion: "1.2"
                    connection:
                        bufferLimit: 32Ki
                """,
            "Backend" => """
                    type: DynamicResolver
                    endpoints:
                    - fqdn:
                            hostname: search.ecommerce.example.com
                            port: 443
                """,
            "EnvoyProxy" => """
                    provider:
                        type: Kubernetes
                """,
            "HTTPRouteFilter" => """
                    urlRewrite:
                        path:
                            type: ReplacePrefixMatch
                            replacePrefixMatch: /
                """,
            "EnvoyPatchPolicy" => """
                    targetRef:
                        group: gateway.networking.k8s.io
                        kind: Gateway
                        name: public-gateway
                    type: JSONPatch
                    jsonPatches:
                    - type: "type.googleapis.com/envoy.config.listener.v3.Listener"
                        name: default/envoy-gateway/http
                """,
            _ => """
                    targetRefs:
                    - group: gateway.networking.k8s.io
                        kind: Gateway
                        name: public-gateway
                    extProc:
                    - name: access-logger
                """
        };

        return $"""
            apiVersion: gateway.envoyproxy.io/v1alpha1
            kind: {kind}
            metadata:
                name: {name}
                namespace: {ns}
            spec:
            {spec}
            status:
                conditions:
                - type: Accepted
                    status: "True"
            """;
    }

    private static IReadOnlyList<EnvoyResourceInfo> BuildDemoEnvoyResources(string ns, string plural)
    {
        return plural switch
        {
            "backendtrafficpolicies" =>
            [
                new EnvoyResourceInfo
                {
                    Kind = "BackendTrafficPolicy", Name = "orders-api-limits", Namespace = ns,
                    TargetRefs = ["HTTPRoute/orders-api-route"],
                    Highlights =
                    [
                        new() { Label = "Status", Value = "Accepted" },
                        new() { Label = "Max connections", Value = "1024" },
                        new() { Label = "Max pending", Value = "512" },
                        new() { Label = "Request timeout", Value = "15s" },
                        new() { Label = "Rate limit", Value = "Global (2 rules)" },
                        new() { Label = "Load balancer", Value = "LeastRequest" }
                    ],
                    Labels = new Dictionary<string, string> { ["app.kubernetes.io/name"] = "order-api" }
                },
                new EnvoyResourceInfo
                {
                    Kind = "BackendTrafficPolicy", Name = "admin-circuit-breaker", Namespace = ns,
                    TargetRefs = ["HTTPRoute/admin-ui-route"],
                    Highlights =
                    [
                        new() { Label = "Status", Value = "Accepted" },
                        new() { Label = "Max connections", Value = "256" },
                        new() { Label = "Retries", Value = "3" },
                        new() { Label = "Retry timeout", Value = "2s" },
                        new() { Label = "Health check", Value = "active + passive" }
                    ]
                }
            ],
            "clienttrafficpolicies" =>
            [
                new EnvoyResourceInfo
                {
                    Kind = "ClientTrafficPolicy", Name = "public-gateway-tls", Namespace = ns,
                    TargetRefs = ["Gateway/public-gateway"],
                    Highlights =
                    [
                        new() { Label = "Status", Value = "Accepted" },
                        new() { Label = "TLS min version", Value = "1.2" },
                        new() { Label = "Buffer limit", Value = "32Ki" },
                        new() { Label = "HTTP/2", Value = "yes" }
                    ]
                }
            ],
            "securitypolicies" =>
            [
                new EnvoyResourceInfo
                {
                    Kind = "SecurityPolicy", Name = "orders-api-auth", Namespace = ns,
                    TargetRefs = ["HTTPRoute/orders-api-route"],
                    Highlights =
                    [
                        new() { Label = "Status", Value = "Accepted" },
                        new() { Label = "JWT (1)", Value = "https://login.ecommerce.example.com/" },
                        new() { Label = "Authorization", Value = "Allow (3 rules)" },
                        new() { Label = "CORS", Value = "2 origins" }
                    ]
                }
            ],
            "backends" =>
            [
                new EnvoyResourceInfo
                {
                    Kind = "Backend", Name = "external-search", Namespace = ns,
                    Highlights =
                    [
                        new() { Label = "Type", Value = "DynamicResolver" },
                        new() { Label = "Endpoints (1)", Value = "search.ecommerce.example.com:443" }
                    ]
                }
            ],
            "envoypatchpolicies" =>
            [
                new EnvoyResourceInfo
                {
                    Kind = "EnvoyPatchPolicy", Name = "gateway-tuning", Namespace = ns,
                    TargetRefs = ["Gateway/public-gateway"],
                    Highlights =
                    [
                        new() { Label = "Status", Value = "Accepted" },
                        new() { Label = "Type", Value = "JSONPatch" },
                        new() { Label = "Patches", Value = "2" }
                    ]
                }
            ],
            "envoyextensionpolicies" =>
            [
                new EnvoyResourceInfo
                {
                    Kind = "EnvoyExtensionPolicy", Name = "access-logger", Namespace = ns,
                    TargetRefs = ["Gateway/public-gateway"],
                    Highlights =
                    [
                        new() { Label = "Status", Value = "Accepted" },
                        new() { Label = "Ext proc", Value = "1" }
                    ]
                }
            ],
            "envoyproxies" =>
            [
                new EnvoyResourceInfo
                {
                    Kind = "EnvoyProxy", Name = "envoy-gateway-config", Namespace = string.Empty,
                    Highlights = [new() { Label = "Provider", Value = "Kubernetes" }]
                }
            ],
            "httproutefilters" =>
            [
                new EnvoyResourceInfo
                {
                    Kind = "HTTPRouteFilter", Name = "strip-api-prefix", Namespace = ns,
                    Highlights = [new() { Label = "URL rewrite", Value = "yes" }]
                }
            ],
            _ => []
        };
    }
}
