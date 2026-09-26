namespace SwebKit.Core.Services;

public partial class DemoAksClient
{
    private readonly Dictionary<string, string> _yamlOverrides = [];

    public Task<string> GetResourceYamlAsync(string ns, string kind, string name, CancellationToken ct = default)
    {
        var key = $"{kind}/{ns}/{name}";
        if (_yamlOverrides.TryGetValue(key, out var overridden))
            return Task.FromResult(overridden);

        if (kind.Equals("Helm", StringComparison.OrdinalIgnoreCase))
        {
            var helmYaml = $"""
                ---
                # Source: {name}/templates/deployment.yaml
                apiVersion: apps/v1
                kind: Deployment
                metadata:
                  name: {name}
                  namespace: {ns}
                  labels:
                    app.kubernetes.io/name: {name}
                    app.kubernetes.io/managed-by: Helm
                    helm.sh/chart: {name}-1.3.0
                spec:
                  replicas: 3
                  selector:
                    matchLabels:
                      app.kubernetes.io/name: {name}
                  template:
                    metadata:
                      labels:
                        app.kubernetes.io/name: {name}
                    spec:
                      containers:
                      - name: {name}
                        image: acr.azurecr.io/{name}:1.3.0
                        ports:
                        - containerPort: 8080
                        resources:
                          requests:
                            cpu: 100m
                            memory: 128Mi
                          limits:
                            cpu: 500m
                            memory: 512Mi
                ---
                # Source: {name}/templates/service.yaml
                apiVersion: v1
                kind: Service
                metadata:
                  name: {name}
                  namespace: {ns}
                  labels:
                    app.kubernetes.io/name: {name}
                    app.kubernetes.io/managed-by: Helm
                spec:
                  type: ClusterIP
                  ports:
                  - port: 80
                    targetPort: 8080
                    protocol: TCP
                  selector:
                    app.kubernetes.io/name: {name}
                """;
            return Task.FromResult(helmYaml);
        }

        if (kind.Equals("StatefulSet", StringComparison.OrdinalIgnoreCase))
        {
            var ssYaml = $"""
                apiVersion: apps/v1
                kind: StatefulSet
                metadata:
                  name: {name}
                  namespace: {ns}
                  labels:
                    app: {name}
                spec:
                  replicas: 3
                  serviceName: {name}
                  selector:
                    matchLabels:
                      app: {name}
                  template:
                    metadata:
                      labels:
                        app: {name}
                    spec:
                      containers:
                      - name: {name}
                        image: acr.azurecr.io/{name}:1.8.3
                        ports:
                        - containerPort: 8080
                """;
            return Task.FromResult(ssYaml);
        }

        if (kind.Equals("ConfigMap", StringComparison.OrdinalIgnoreCase))
        {
            var cmYaml = $"""
                apiVersion: v1
                kind: ConfigMap
                metadata:
                  name: {name}
                  namespace: {ns}
                data:
                  ConnectionStrings__Redis: redis://redis-service:6379
                  Feature__SearchEnabled: "true"
                """;
            return Task.FromResult(cmYaml);
        }

        if (kind.Equals("Secret", StringComparison.OrdinalIgnoreCase))
        {
            var secretYaml = $"""
                apiVersion: v1
                kind: Secret
                metadata:
                  name: {name}
                  namespace: {ns}
                type: Opaque
                data:
                  api-key: c2stZGVtby1hYmMxMjM=
                  webhook-secret: d2hzZWMtZGVtby14eXo3ODk=
                """;
            return Task.FromResult(secretYaml);
        }

        if (kind.Equals("Gateway", StringComparison.OrdinalIgnoreCase))
        {
            var gatewayYaml = $"""
                                apiVersion: gateway.networking.k8s.io/v1
                                kind: Gateway
                                metadata:
                                    name: {name}
                                    namespace: {ns}
                                    labels:
                                        gateway.envoyproxy.io/managed: "true"
                                spec:
                                    gatewayClassName: envoy-gateway
                                    listeners:
                                    - name: https-api
                                        hostname: api.ecommerce.example.com
                                        port: 443
                                        protocol: HTTPS
                                        tls:
                                            mode: Terminate
                                            certificateRefs:
                                            - kind: Secret
                                                name: edge-tls
                                status:
                                    conditions:
                                    - type: Programmed
                                        status: "True"
                                """;
            return Task.FromResult(gatewayYaml);
        }

        if (kind.Equals("GatewayClass", StringComparison.OrdinalIgnoreCase))
        {
            var isInternal = name.Contains("internal", StringComparison.OrdinalIgnoreCase);
            var gatewayClassYaml = $"""
                                apiVersion: gateway.networking.k8s.io/v1
                                kind: GatewayClass
                                metadata:
                                    name: {name}
                                {(!isInternal ? "  annotations:\n    gateway.networking.k8s.io/default-gatewayclass: \"true\"\n" : string.Empty)}spec:
                                    controllerName: gateway.envoyproxy.io/gatewayclass-controller
                                    description: {(isInternal ? "Internal Envoy Gateway class for private workloads." : "Default Envoy Gateway class for internet-facing traffic.")}
                                    parametersRef:
                                        group: gateway.envoyproxy.io
                                        kind: EnvoyProxy
                                        namespace: infrastructure
                                        name: {(isInternal ? "envoy-internal-config" : "envoy-gateway-config")}
                                status:
                                    conditions:
                                    - type: Accepted
                                        status: "True"
                                """;
            return Task.FromResult(gatewayClassYaml.ReplaceLineEndings("\n"));
        }

        if (kind.Equals("HTTPRoute", StringComparison.OrdinalIgnoreCase))
        {
            var routeYaml = $"""
                                apiVersion: gateway.networking.k8s.io/v1
                                kind: HTTPRoute
                                metadata:
                                    name: {name}
                                    namespace: {ns}
                                spec:
                                    parentRefs:
                                    - name: public-gateway
                                        sectionName: https-api
                                    hostnames:
                                    - api.ecommerce.example.com
                                    rules:
                                    - matches:
                                        - path:
                                                type: PathPrefix
                                                value: /
                                        backendRefs:
                                        - name: order-api
                                            port: 80
                                status:
                                    parents:
                                    - parentRef:
                                            name: public-gateway
                                        conditions:
                                        - type: Accepted
                                            status: "True"
                                """;
            return Task.FromResult(routeYaml);
        }

        if (kind.Equals("HorizontalPodAutoscaler", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("HPA", StringComparison.OrdinalIgnoreCase))
        {
            var hpaYaml = $"""
                apiVersion: autoscaling/v2
                kind: HorizontalPodAutoscaler
                metadata:
                  name: {name}
                  namespace: {ns}
                spec:
                  scaleTargetRef:
                    apiVersion: apps/v1
                    kind: Deployment
                    name: {name}
                  minReplicas: 2
                  maxReplicas: 5
                  metrics:
                  - type: Resource
                    resource:
                      name: cpu
                      target:
                        type: Utilization
                        averageUtilization: 70
                """;
            return Task.FromResult(hpaYaml);
        }

        if (kind.Equals("Job", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(BuildJobYaml(ns, name));

        if (kind.Equals("CronJob", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(BuildCronJobYaml(ns, name));

        if (kind.Equals("ScaledJob", StringComparison.OrdinalIgnoreCase))
        {
            var scaledJobYaml = $"""
                apiVersion: keda.sh/v1alpha1
                kind: ScaledJob
                metadata:
                  name: {name}
                  namespace: {ns}
                spec:
                  jobTargetRef:
                    parallelism: 1
                    completions: 1
                    template:
                      spec:
                        containers:
                        - name: {name}
                          image: acr.azurecr.io/{name}:latest
                        restartPolicy: Never
                  minReplicaCount: 0
                  maxReplicaCount: 5
                  triggers:
                  - type: cron
                    metadata:
                      timezone: UTC
                      start: 0 2 * * *
                      end: 0 4 * * *
                      desiredReplicas: "2"
                """;
            return Task.FromResult(scaledJobYaml);
        }

        if (TryResolveDemoEnvoyKind(kind) is { } envoyKind)
        {
            return Task.FromResult(BuildDemoEnvoyYaml(envoyKind, ns, name));
        }

        if (kind.Equals("Pod", StringComparison.OrdinalIgnoreCase))
        {
            var podYaml = $"""
                apiVersion: v1
                kind: Pod
                metadata:
                  name: {name}
                  namespace: {ns}
                  labels:
                    app: {name.Split('-').FirstOrDefault() ?? name}
                    pod-template-hash: {name.Split('-').LastOrDefault() ?? "abc123"}
                  ownerReferences:
                  - apiVersion: apps/v1
                    kind: ReplicaSet
                    name: {(name.Contains('-') ? name[..name.LastIndexOf('-')] : name)}
                    controller: true
                spec:
                  containers:
                  - name: {name}
                    image: acr.azurecr.io/{name}:1.8.3
                    ports:
                    - containerPort: 8080
                    env:
                    - name: ASPNETCORE_ENVIRONMENT
                      value: Production
                    - name: FEATURE__SEARCH
                      valueFrom:
                        configMapKeyRef:
                          name: app-settings
                          key: Feature__SearchEnabled
                    - name: OTEL__ENDPOINT
                      valueFrom:
                        configMapKeyRef:
                          name: tracing-config
                          key: Otel__Endpoint
                    - name: DB__CONNECTIONSTRING
                      valueFrom:
                        secretKeyRef:
                          name: db-credentials
                          key: connection-string
                    - name: POD_IP
                      valueFrom:
                        fieldRef:
                          fieldPath: status.podIP
                    envFrom:
                    - configMapRef:
                        name: app-settings
                      prefix: CFG_
                    - secretRef:
                        name: order-api-secret
                    resources:
                      requests:
                        cpu: 100m
                        memory: 128Mi
                      limits:
                        cpu: 500m
                        memory: 512Mi
                  - name: istio-proxy
                    image: docker.io/istio/proxyv2:1.20.3
                    ports:
                    - containerPort: 15090
                status:
                  phase: Running
                  podIP: 10.244.1.17
                  hostIP: 10.240.0.4
                  containerStatuses:
                  - name: {name}
                    ready: true
                    restartCount: 0
                    state:
                      running:
                        startedAt: "2026-07-31T08:12:00Z"
                """;
            return Task.FromResult(podYaml);
        }

        var yaml = $"""
            apiVersion: {(kind == "Deployment" ? "apps/v1" : kind == "Ingress" ? "networking.k8s.io/v1" : "v1")}
            kind: {kind}
            metadata:
              name: {name}
              namespace: {ns}
              labels:
                app: {name}
                version: "1.8.3"
                team: commerce
              annotations:
                deployment.kubernetes.io/revision: "12"
            spec:
              replicas: 3
              selector:
                matchLabels:
                  app: {name}
              template:
                metadata:
                  labels:
                    app: {name}
                spec:
                  containers:
                  - name: {name}
                    image: acr.azurecr.io/{name}:1.8.3
                    ports:
                    - containerPort: 8080
                    resources:
                      requests:
                        cpu: 100m
                        memory: 128Mi
                      limits:
                        cpu: 500m
                        memory: 512Mi
                    livenessProbe:
                      httpGet:
                        path: /healthz
                        port: 8080
                      initialDelaySeconds: 10
                      periodSeconds: 15
                    readinessProbe:
                      httpGet:
                        path: /ready
                        port: 8080
                      initialDelaySeconds: 5
                      periodSeconds: 10
                  - name: istio-proxy
                    image: docker.io/istio/proxyv2:1.20.3
                    ports:
                    - containerPort: 15090
            """;
        return Task.FromResult(yaml);
    }

    public async Task ApplyResourceYamlAsync(string ns, string kind, string name, string yaml, CancellationToken ct = default)
    {
        await Task.Delay(400, ct).ConfigureAwait(false); // simulate apply latency
        // Demo mode: store override so the next GetResourceYamlAsync call returns the edited YAML
        _yamlOverrides[$"{kind}/{ns}/{name}"] = yaml;
    }

    // ── Feature 1: Multi-pod log aggregation ─────────────────────────────────
}
