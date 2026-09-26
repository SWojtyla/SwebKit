export interface AksConfig {
    kubeconfigPath: string | null;
    kubeconfigContext: string | null;
    defaultNamespace: string;
    watchedDeployments: string[];
    logBufferSize: number;
    autoRefreshIntervalSeconds: number;
    monitoringEnabled: boolean;
    monitoredNamespaces: string[];
}

export interface KubeContextInfo {
    name: string;
    cluster: string | null;
    user: string | null;
    namespace: string | null;
    isCurrent: boolean;
}

export interface DeploymentInfo {
    name: string;
    namespace: string;
    replicas: number;
    readyReplicas: number;
    status: string;
    imageTag: string | null;
    labels: Record<string, string>;
    selectorLabels: Record<string, string>;
}

export interface PodInfo {
    name: string;
    namespace: string;
    phase: string;
    status: string;
    ready: boolean;
    readyContainers: number;
    totalContainers: number;
    restartCount: number;
    lastRestartTime: string | null;
    lastRestartReason: string | null;
    podIP: string | null;
    nodeName: string | null;
    startTime: string | null;
    containers: string[];
    labels: Record<string, string>;
    readyDisplay: string;
}

export interface KubernetesEvent {
    name: string;
    namespace: string;
    type: string;
    reason: string | null;
    message: string | null;
    involvedObjectName: string | null;
    involvedObjectKind: string | null;
    lastTimestamp: string | null;
    count: number;
}

export interface ServiceInfo {
    name: string;
    namespace: string;
    type: string;
    clusterIp: string;
    externalAddresses: string[];
    ports: ServicePortInfo[];
    selectorLabels: Record<string, string>;
    labels: Record<string, string>;
}

export interface ServicePortInfo {
    name: string | null;
    protocol: string;
    port: number;
    targetPort: string | null;
    nodePort: number | null;
}

export interface HelmReleaseInfo {
    name: string;
    namespace: string;
    chart: string | null;
    appVersion: string | null;
    chartVersion: string | null;
    status: string;
    revision: number;
    updated: string | null;
}

export interface SecretInfo {
    name: string;
    namespace: string;
    type: string;
    keys: string[];
    labels: Record<string, string>;
}

export interface ConfigMapInfo {
    name: string;
    namespace: string;
    /**
     * Data key names. The list endpoint sends these and omits the values — a namespace's ConfigMap
     * values can run to megabytes, and the list only ever renders names. `useAksConfigMapValues`
     * fetches one ConfigMap's values for the detail panel, the same way Secrets already work.
     */
    keys: string[];
    /** Total size of all values in characters — sent instead of the values themselves. */
    dataSizeChars: number;
    labels: Record<string, string>;
}

export interface StatefulSetInfo {
    name: string;
    namespace: string;
    replicas: number;
    readyReplicas: number;
    currentRevision: string | null;
    updateRevision: string | null;
    labels: Record<string, string>;
    selectorLabels: Record<string, string>;
}

export interface HpaInfo {
    name: string;
    namespace: string;
    targetKind: string;
    targetName: string;
    minReplicas: number;
    maxReplicas: number;
    currentReplicas: number;
    desiredReplicas: number;
    currentCpuUtilizationPercent: number | null;
    targetCpuUtilizationPercent: number | null;
    isKedaManaged: boolean;
    scaledObjectName: string | null;
    isScalingDisabled: boolean;
}

export interface CronJobInfo {
    name: string;
    namespace: string;
    schedule: string | null;
    /** spec.timeZone — the IANA zone the schedule is evaluated in, or null. */
    timeZone: string | null;
    suspend: boolean;
    activeCount: number;
    lastScheduleTime: string | null;
    lastSuccessfulTime: string | null;
}

/** A KEDA ScaledJob — job-based autoscaling that produces no HPA. */
export interface ScaledJobInfo {
    name: string;
    namespace: string;
    isPaused: boolean;
    minReplicas: number;
    maxReplicas: number;
    triggers: string[];
}

export interface IngressInfo {
    name: string;
    namespace: string;
    ingressClass: string | null;
    rules: {
        host: string | null;
        paths: {
            path: string;
            pathType: string | null;
            serviceName: string | null;
            servicePort: number | null;
        }[];
    }[];
    addresses: string[];
    labels: Record<string, string>;
}

export interface HttpRouteRuleInfo {
    matches: string[];
    filters: string[];
    backendRefs: string[];
    requestTimeout: string | null;
    backendRequestTimeout: string | null;
}

export interface HttpRouteParentStatus {
    parentRef: string;
    status: string;
    reason: string | null;
}

export interface HttpRouteInfo {
    name: string;
    namespace: string;
    status: string;
    hostnames: string[];
    parentRefs: string[];
    backendRefs: string[];
    rules: HttpRouteRuleInfo[];
    parentStatuses: HttpRouteParentStatus[];
    labels: Record<string, string>;
}

export interface EnvoyHighlight {
    label: string;
    value: string;
}

export interface EnvoyResourceInfo {
    kind: string;
    name: string;
    namespace: string;
    targetRefs: string[];
    highlights: EnvoyHighlight[];
    labels: Record<string, string>;
}

export interface GatewayInfo {
    name: string;
    namespace: string;
    gatewayClass: string;
    status: string;
    addresses: string[];
    attachedRoutes: number;
    labels: Record<string, string>;
}

export interface GatewayClassInfo {
    name: string;
    controllerName: string;
    status: string;
    labels: Record<string, string>;
}

export interface HelmHistoryEntry {
    revision: number;
    status: string;
    chart: string;
    appVersion: string;
    description: string;
    updated: string | null;
}

export interface HelmValuesResponse {
    userValues: string;
    computedValues: string;
}

export interface JobInfo {
    name: string;
    namespace: string;
    status: string;
    active: number;
    succeeded: number;
    failed: number;
    desiredCompletions: number | null;
    parallelism: number;
    startTime: string | null;
    completionTime: string | null;
    sourceKind: string | null;
    sourceName: string | null;
}

// ── Redis ─────────────────────────────────────────────────────────────────────

export interface ContainerDetail {
    name: string;
    image: string;
    imageTag: string | null;
    resources: ResourceRequirements;
    envVars: EnvVarDetail[];
}

export interface ResourceRequirements {
    cpuRequest: string | null;
    memoryRequest: string | null;
    cpuLimit: string | null;
    memoryLimit: string | null;
}

export type EnvVarSourceKind =
    | "Plain"
    | "ConfigMapRef"
    | "SecretRef"
    | "FieldRef";

export interface EnvVarDetail {
    name: string;
    value: string | null;
    source: EnvVarSourceKind;
    sourceName: string | null;
    sourceKey: string | null;
    isResolved: boolean;
}

export interface PodMetricInfo {
    podName: string;
    namespace: string;
    containers: PodMetricContainer[];
}

export interface PodMetricContainer {
    name: string;
    cpuCores: number;
    memoryBytes: number;
}
