import { AksWorkspaceProvider } from "./shared/AksWorkspaceContext";
import {
    useAksCluster,
    useAksNav,
    useAksQueries,
    useAksOps,
    useAksOverlays,
    useAksActions,
    aksRefreshIntervals,
    directTabs,
    networkTabs,
    extraTabs,
    networkTabIds,
} from "./shared/aks-workspace-context";
import { useScreenStateProvider } from "../../lib/stores/screen-state";
import { DeploymentsTab } from "./DeploymentsTab";
import { PodsTab } from "./PodsTab";
import { ServicesTab } from "./ServicesTab";
import { HelmTab } from "./HelmTab";
import { SecretsTab } from "./SecretsTab";
import { EventsTab } from "./EventsTab";
import { StatefulSetsTab } from "./StatefulSetsTab";
import { CronJobsTab } from "./CronJobsTab";
import { JobsTab } from "./JobsTab";
import { ConfigMapsTab } from "./ConfigMapsTab";
import { IngressesTab } from "./IngressesTab";
import { HttpRoutesTab } from "./HttpRoutesTab";
import { EnvoyTab } from "./EnvoyTab";
import { AutoscalingTab } from "./AutoscalingTab";
import { GatewayClassesTab } from "./GatewayClassesTab";
import { GatewaysTab } from "./GatewaysTab";
import { PodDetailPanel } from "./PodDetailPanel";
import { YamlViewer } from "./YamlViewer";
import { HelmDetailPanel } from "./HelmDetailPanel";
import { PortForwardPanel } from "./PortForwardPanel";
import { AnalysisPanel } from "./AnalysisPanel";
import { SecretDetailPanel } from "./SecretDetailPanel";
import { ConfigMapDetailPanel } from "./ConfigMapDetailPanel";
import { HttpRouteDetailPanel } from "./HttpRouteDetailPanel";
import { MultiPodLogView } from "./MultiPodLogView";
import { ContextMenu } from "./ContextMenu";
import { ContainerDetailPanel } from "./ContainerDetailPanel";
import { PodShellPanel } from "./PodShellPanel";
import { ContextualAssistant } from "@/components/agent/ContextualAssistant";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { LastRefreshed } from "@/components/shared/LastRefreshed";
import { ResizablePanel } from "@/components/ui/ResizablePanel";
import { NamespaceSelector } from "./NamespaceSelector";
import { ContextSelector } from "./ContextSelector";
import { PinResourceButton } from "@/components/shared/PinResourceButton";
import { pinAksNamespaces } from "@/lib/pinned-resources";
import { RefreshCw, Loader2, Ship } from "lucide-react";
import { useNavigate } from "react-router";
import { EmptyState } from "@/components/shared/EmptyState";

export function AksPage() {
    return (
        <AksWorkspaceProvider>
            <AksPageContent />
        </AksWorkspaceProvider>
    );
}

function AksPageContent() {
    const ws = {
        ...useAksCluster(),
        ...useAksNav(),
        ...useAksQueries(),
        ...useAksOps(),
        ...useAksOverlays(),
        ...useAksActions(),
    };
    const navigate = useNavigate();
    const isNetworkTabActive = networkTabIds.has(ws.activeTab);

    // Screen-state snapshot (agent-workspace-awareness M1): what the user sees on this page —
    // bounded to the fields the agent needs; read at publish time so it stays current.
    useScreenStateProvider(
        "aks-page",
        "Aks",
        () => ({
            context: ws.defaultContext,
            contexts: ws.selectedContexts,
            namespaces: ws.selectedNamespaces.map(
                (s) => `${s.context}:${s.namespace}`,
            ),
            activeTab: ws.activeTab,
            podCount: ws.allPods?.length ?? 0,
            pods: (ws.allPods ?? []).slice(0, 30).map((p) => ({
                context: p.context ?? ws.defaultContext,
                namespace: p.namespace,
                name: p.name,
                phase: p.phase,
                ready: p.ready,
                restarts: p.restartCount,
                lastRestartReason: p.lastRestartReason,
            })),
            podOverflow: Math.max(0, (ws.allPods?.length ?? 0) - 30),
            selectedPod: ws.selectedPod
                ? {
                      context: ws.selectedPod.context ?? ws.defaultContext,
                      namespace: ws.selectedPod.namespace,
                      name: ws.selectedPod.name,
                      phase: ws.selectedPod.phase,
                      ready: ws.selectedPod.ready,
                      restarts: ws.selectedPod.restartCount,
                      lastRestartReason: ws.selectedPod.lastRestartReason,
                  }
                : null,
        }),
        [
            ws.defaultContext,
            ws.selectedContexts,
            ws.selectedNamespaces,
            ws.activeTab,
            ws.allPods,
            ws.selectedPod,
        ],
    );

    return (
        <div className="flex h-full flex-col" data-testid="aks-page">
            {/* Header with context and namespace selectors. `flex-wrap` because at 1280px
          the toolbar genuinely does not fit on one line — without it the last
          controls were squeezed until their labels wrapped to three lines and the
          connection status was clipped off the right edge. */}
            <div className="flex flex-wrap items-center gap-x-3 gap-y-2 border-b px-4 py-2">
                <span className="text-sm font-medium">Context:</span>
                <ContextSelector
                    contexts={ws.contexts}
                    selectedContexts={ws.selectedContexts}
                    onToggle={ws.toggleContext}
                    onSelectOnly={(ctx) => ws.selectContexts([ctx])}
                />

                <span className="text-sm font-medium">Namespace:</span>
                <NamespaceSelector
                    scopes={ws.nsScopes}
                    selected={ws.selectedNamespaces}
                    defaultContext={ws.defaultContext}
                    onChange={ws.setSelectedNamespaces}
                    isLoading={ws.nsLoading}
                    loadingLabel="Loading namespaces…"
                    error={ws.nsError}
                    disabledReason={
                        ws.activeTab === "gatewayclasses"
                            ? "Not applicable — GatewayClasses are cluster-scoped, not namespaced"
                            : undefined
                    }
                />
                {ws.selectedNamespaces.length > 0 && (
                    <PinResourceButton
                        resource={pinAksNamespaces(
                            ws.selectedContexts,
                            ws.selectedNamespaces,
                        )}
                        testId="aks-pin-namespaces"
                    />
                )}

                {ws.nsLoading ? (
                    <div
                        className="flex items-center gap-1.5 text-xs text-muted-foreground"
                        data-testid="aks-ns-loading-indicator"
                    >
                        <Loader2 className="h-3.5 w-3.5 animate-spin" />
                        Loading namespaces…
                    </div>
                ) : null}

                {/* Auto-refresh controls. The in-flight state lives on the Refresh button's
            icon rather than a separate "Loading resources…" label — with auto-refresh
            on, that label appeared and vanished every few seconds and shoved the rest
            of the toolbar sideways each time. */}
                <div className="ml-auto flex items-center gap-2">
                    <label
                        className="flex items-center gap-1.5 text-xs"
                        data-testid="aks-auto-refresh"
                        title="Automatically re-fetch the resources in view"
                    >
                        <input
                            type="checkbox"
                            checked={ws.autoRefresh}
                            onChange={(e) =>
                                ws.setAutoRefresh(e.target.checked)
                            }
                            disabled={ws.queryTargets.length === 0}
                            title={
                                ws.queryTargets.length === 0
                                    ? "Namespace is still loading"
                                    : undefined
                            }
                            data-testid="aks-auto-refresh-checkbox"
                        />
                        <span>Auto</span>
                    </label>
                    <select
                        value={ws.refreshInterval}
                        onChange={(e) =>
                            ws.setRefreshInterval(Number(e.target.value))
                        }
                        disabled={
                            !ws.autoRefresh || ws.queryTargets.length === 0
                        }
                        title={
                            ws.queryTargets.length === 0
                                ? "Namespace is still loading"
                                : !ws.autoRefresh
                                  ? "Enable Auto to pick an interval"
                                  : ws.isMultiContext && ws.refreshInterval < 30
                                    ? "Intervals under 30s are floored to 30s while multiple clusters are attached"
                                    : undefined
                        }
                        className="rounded-md border bg-card px-2 py-1 text-xs disabled:opacity-40"
                        aria-label="Auto-refresh interval"
                        data-testid="aks-refresh-interval"
                    >
                        {aksRefreshIntervals.map((seconds) => (
                            <option key={seconds} value={seconds}>
                                {seconds}s
                            </option>
                        ))}
                    </select>
                    <LastRefreshed
                        at={ws.lastRefreshedAt}
                        isFetching={ws.isAksFetching}
                        paused={ws.autoRefreshPaused}
                        pausedReason="Auto-refresh is held while a detail panel is open"
                        testId="aks-last-refreshed"
                    />
                    <button
                        onClick={ws.handleManualRefresh}
                        disabled={ws.queryTargets.length === 0}
                        title={
                            ws.queryTargets.length === 0
                                ? "Namespace is still loading"
                                : "Refresh the resources in view"
                        }
                        className="flex shrink-0 items-center gap-1 whitespace-nowrap rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                        data-testid="aks-refresh-btn"
                    >
                        <RefreshCw
                            className={`h-3.5 w-3.5 ${ws.isAksFetching ? "animate-spin" : ""}`}
                        />
                        Refresh
                    </button>
                    <button
                        onClick={async () => {
                            const pods = ws.allPods ?? (await ws.refetchPods());
                            ws.openMultiPodLogs(pods);
                        }}
                        disabled={
                            ws.queryTargets.length === 0 || ws.podsFetching
                        }
                        title={
                            ws.queryTargets.length === 0
                                ? "Namespace is still loading"
                                : ws.podsFetching
                                  ? "Pods are still loading"
                                  : "Stream logs from all pods at once"
                        }
                        className="flex shrink-0 items-center gap-1 whitespace-nowrap rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                        data-testid="aks-multi-pod-logs"
                    >
                        Multi-Pod Logs
                    </button>
                </div>

                {ws.testResult && (
                    <span
                        className={`flex shrink-0 items-center gap-1.5 whitespace-nowrap text-xs ${ws.testResult.connected ? "text-success" : "text-destructive"}`}
                        data-testid="aks-connection-status"
                    >
                        <span
                            className={`h-2 w-2 rounded-full ${ws.testResult.connected ? "bg-success" : "bg-destructive"}`}
                        />
                        {ws.testResult.connected ? "Connected" : "Disconnected"}
                        {ws.testResult.error && ` — ${ws.testResult.error}`}
                    </span>
                )}
            </div>

            {ws.pendingConfirm && (
                <ConfirmBar
                    message={ws.pendingConfirm.message}
                    requireTypedName={ws.pendingConfirm.requireTypedName}
                    onConfirm={ws.pendingConfirm.onConfirm}
                    onCancel={() => ws.setPendingConfirm(null)}
                    testId="aks-confirm-bar"
                    confirmTestId="aks-confirm-yes"
                    cancelTestId="aks-confirm-cancel"
                    typedNameTestId="aks-confirm-typed-name"
                />
            )}

            {/* Tabs */}
            <div
                className="flex border-b overflow-x-auto"
                data-testid="aks-tabs"
            >
                {directTabs.map((tab) => (
                    <button
                        key={tab.id}
                        onClick={() => {
                            ws.setActiveTab(tab.id);
                            ws.setNetworkMenuOpen(false);
                        }}
                        data-testid={`aks-tab-${tab.id}`}
                        className={`whitespace-nowrap px-4 py-2 text-sm font-medium ${
                            ws.activeTab === tab.id
                                ? "border-b-2 border-primary text-foreground"
                                : "text-muted-foreground hover:text-foreground"
                        }`}
                    >
                        {tab.label}
                    </button>
                ))}
                <button
                    type="button"
                    onClick={() => ws.setNetworkMenuOpen((v) => !v)}
                    data-testid="aks-tab-network"
                    className={`flex items-center gap-1 whitespace-nowrap px-4 py-2 text-sm font-medium ${
                        isNetworkTabActive || ws.networkMenuOpen
                            ? "border-b-2 border-primary text-foreground"
                            : "text-muted-foreground hover:text-foreground"
                    }`}
                >
                    Network{" "}
                    <span className="text-xs">
                        {ws.networkMenuOpen ? "▲" : "▼"}
                    </span>
                </button>
                {extraTabs.map((tab) => (
                    <button
                        key={tab.id}
                        onClick={() => {
                            ws.setActiveTab(tab.id);
                            ws.setNetworkMenuOpen(false);
                        }}
                        data-testid={`aks-tab-${tab.id}`}
                        className={`whitespace-nowrap px-4 py-2 text-sm font-medium ${
                            ws.activeTab === tab.id
                                ? "border-b-2 border-primary text-foreground"
                                : "text-muted-foreground hover:text-foreground"
                        }`}
                    >
                        {tab.label}
                    </button>
                ))}
            </div>

            {ws.networkMenuOpen && (
                <div
                    className="flex gap-1 border-b bg-card px-2 py-1"
                    data-testid="aks-network-submenu"
                >
                    <span className="text-xs text-muted-foreground py-1 px-2">
                        Network
                    </span>
                    {networkTabs.map((tab) => (
                        <button
                            key={tab.id}
                            onClick={() => {
                                ws.setActiveTab(tab.id);
                                ws.setNetworkMenuOpen(true);
                            }}
                            data-testid={`aks-tab-${tab.id}`}
                            className={`rounded px-3 py-1 text-xs ${
                                ws.activeTab === tab.id
                                    ? "bg-primary text-primary-foreground"
                                    : "text-muted-foreground hover:bg-accent"
                            }`}
                        >
                            {tab.label}
                        </button>
                    ))}
                </div>
            )}

            {/* Content */}
            <div
                className="flex flex-1 overflow-hidden"
                data-testid="aks-content"
            >
                <div className="flex min-w-0 flex-1 flex-col">
                    <div className="flex-1 overflow-auto">
                        {ws.profileLoaded &&
                        !ws.defaultContext &&
                        !ws.isDemoMode ? (
                            <EmptyState
                                icon={Ship}
                                title="No AKS cluster configured"
                                description="Point SwebKit at a kubeconfig to browse deployments, pods, logs and more."
                                action={
                                    <button
                                        type="button"
                                        onClick={() =>
                                            navigate("/settings", {
                                                state: { tab: "aks" },
                                            })
                                        }
                                        className="rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:bg-primary/90"
                                        data-testid="aks-configure-cta"
                                    >
                                        Configure kubeconfig
                                    </button>
                                }
                                testId="aks-first-run"
                            />
                        ) : ws.queryTargets.length === 0 ? (
                            <div
                                className="flex h-full items-center justify-center text-sm text-muted-foreground"
                                data-testid="aks-empty-state"
                            >
                                {ws.selectedContexts.length === 0
                                    ? "Select a context to get started"
                                    : "Select a namespace to view resources"}
                            </div>
                        ) : (
                            <>
                                {ws.activeTab === "deployments" && (
                                    <DeploymentsTab
                                        targets={ws.queryTargets}
                                        isMulti={ws.isMultiNamespace}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "statefulsets" && (
                                    <StatefulSetsTab
                                        targets={ws.queryTargets}
                                        isMulti={ws.isMultiNamespace}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "pods" && (
                                    <PodsTab
                                        targets={ws.queryTargets}
                                        isMulti={ws.isMultiNamespace}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "services" && (
                                    <ServicesTab
                                        targets={ws.queryTargets}
                                        isMulti={ws.isMultiNamespace}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "ingresses" && (
                                    <IngressesTab
                                        targets={ws.queryTargets}
                                        isMulti={ws.isMultiNamespace}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "httproutes" && (
                                    <HttpRoutesTab
                                        targets={ws.queryTargets}
                                        isMulti={ws.isMultiNamespace}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "envoy" && (
                                    <EnvoyTab
                                        targets={ws.queryTargets}
                                        isMulti={ws.isMultiNamespace}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "gatewayclasses" && (
                                    <GatewayClassesTab
                                        contexts={ws.selectedContexts}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "gateways" && (
                                    <GatewaysTab
                                        targets={ws.queryTargets}
                                        isMulti={ws.isMultiNamespace}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "cronjobs" && (
                                    <CronJobsTab
                                        targets={ws.queryTargets}
                                        isMulti={ws.isMultiNamespace}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "jobs" && (
                                    <JobsTab
                                        targets={ws.queryTargets}
                                        isMulti={ws.isMultiNamespace}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "configmaps" && (
                                    <ConfigMapsTab
                                        targets={ws.queryTargets}
                                        isMulti={ws.isMultiNamespace}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "secrets" && (
                                    <SecretsTab
                                        targets={ws.queryTargets}
                                        isMulti={ws.isMultiNamespace}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "hpa" && (
                                    <AutoscalingTab
                                        targets={ws.queryTargets}
                                        isMulti={ws.isMultiNamespace}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "helm" && (
                                    <HelmTab
                                        targets={ws.queryTargets}
                                        isMulti={ws.isMultiNamespace}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "events" && (
                                    <EventsTab
                                        targets={ws.queryTargets}
                                        isMulti={ws.isMultiNamespace}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                                {ws.activeTab === "portforward" && (
                                    <PortForwardPanel
                                        selectedPod={ws.selectedPod}
                                        onPodConsumed={() => ws.setPodKey(null)}
                                        context={
                                            ws.selectedPod?.context ??
                                            ws.defaultContext
                                        }
                                        kubeconfig={ws.kubeconfigPath}
                                        pods={ws.allPods}
                                    />
                                )}
                                {ws.activeTab === "analysis" && (
                                    <AnalysisPanel
                                        targets={ws.queryTargets}
                                        showContext={ws.isMultiContext}
                                    />
                                )}
                            </>
                        )}
                    </div>

                    {/* Bottom-docked pod shell — a live terminal, not a detail view, so it docks rather
            than competing for the right-hand panel slot, and stays open across tab switches
            (the context clears it on cluster change, not on tab change). */}
                    {ws.shellPod && (
                        <PodShellPanel
                            namespace={ws.shellPod.namespace}
                            pod={ws.shellPod.name}
                            container={ws.shellPod.containers[0] ?? null}
                            context={ws.shellPod.context ?? ws.defaultContext}
                            kubeconfig={ws.kubeconfigPath}
                            onClose={() => ws.setShellPod(null)}
                        />
                    )}
                </div>

                {/* Side panel for detail views */}
                {ws.selectedPod && (
                    <ResizablePanel
                        storageKey="aks-pod-detail"
                        defaultWidth={620}
                        minWidth={320}
                        maxWidth={1200}
                        showHeader={false}
                    >
                        <PodDetailPanel
                            pod={ws.selectedPod}
                            ns={ws.selectedPod.namespace}
                            onClose={() => ws.setPodKey(null)}
                            onViewYaml={() =>
                                ws.openYaml(
                                    "pod",
                                    ws.selectedPod!.name,
                                    ws.selectedPod!.namespace,
                                    ws.selectedPod!.context,
                                )
                            }
                            onOpenShell={() => ws.setShellPod(ws.selectedPod)}
                            onPortForward={() =>
                                ws.openPortForward(ws.selectedPod!)
                            }
                            onAskAi={() => ws.setAskAiPod(ws.selectedPod)}
                        />
                    </ResizablePanel>
                )}
                {ws.askAiPod && (
                    <ContextualAssistant
                        featureArea="Aks"
                        title={`pod ${ws.askAiPod.name}`}
                        selection={{
                            namespace: ws.askAiPod.namespace,
                            pod: ws.askAiPod.name,
                        }}
                        onClose={() => ws.setAskAiPod(null)}
                    />
                )}
                {ws.yamlResource && (
                    <ResizablePanel
                        storageKey="aks-yaml-viewer"
                        defaultWidth={620}
                        minWidth={320}
                        maxWidth={1200}
                        showHeader={false}
                    >
                        <YamlViewer
                            ns={ws.yamlResource.namespace}
                            kind={ws.yamlResource.kind}
                            name={ws.yamlResource.name}
                            context={ws.yamlResource.context}
                            onClose={() => ws.setYamlResource(null)}
                        />
                    </ResizablePanel>
                )}
                {ws.helmRelease && (
                    <ResizablePanel
                        storageKey="aks-helm-detail"
                        defaultWidth={620}
                        minWidth={320}
                        maxWidth={1200}
                        showHeader={false}
                    >
                        <HelmDetailPanel
                            ns={ws.helmRelease.namespace}
                            release={ws.helmRelease.name}
                            context={ws.helmRelease.context}
                            onClose={() => ws.setHelmRelease(null)}
                            onRequestConfirm={ws.requestConfirm}
                        />
                    </ResizablePanel>
                )}
                {ws.selectedSecret && (
                    <ResizablePanel
                        storageKey="aks-secret-detail"
                        defaultWidth={620}
                        minWidth={320}
                        maxWidth={1200}
                        showHeader={false}
                    >
                        <SecretDetailPanel
                            secret={ws.selectedSecret}
                            onClose={() => ws.setSelectedSecret(null)}
                        />
                    </ResizablePanel>
                )}
                {ws.selectedConfigMap && (
                    <ResizablePanel
                        storageKey="aks-configmap-detail"
                        defaultWidth={620}
                        minWidth={320}
                        maxWidth={1200}
                        showHeader={false}
                    >
                        <ConfigMapDetailPanel
                            configMap={ws.selectedConfigMap}
                            onClose={() => ws.setSelectedConfigMap(null)}
                        />
                    </ResizablePanel>
                )}
                {ws.selectedHttpRoute && (
                    <ResizablePanel
                        storageKey="aks-httproute-detail"
                        defaultWidth={560}
                        minWidth={320}
                        maxWidth={1200}
                        showHeader={false}
                    >
                        <HttpRouteDetailPanel
                            route={ws.selectedHttpRoute}
                            onClose={() => ws.setSelectedHttpRoute(null)}
                            onViewYaml={() =>
                                ws.openYaml(
                                    "httproute",
                                    ws.selectedHttpRoute!.name,
                                    ws.selectedHttpRoute!.namespace,
                                    ws.selectedHttpRoute!.context,
                                )
                            }
                        />
                    </ResizablePanel>
                )}
                {ws.showMultiPodLogs && (
                    <ResizablePanel
                        storageKey="aks-multi-pod-logs"
                        defaultWidth={620}
                        minWidth={320}
                        maxWidth={1200}
                        showHeader={false}
                    >
                        <MultiPodLogView
                            pods={ws.multiLogPods}
                            onClose={() => ws.closeMultiPodLogs()}
                        />
                    </ResizablePanel>
                )}
                {ws.containerDetail && (
                    <ResizablePanel
                        storageKey="aks-container-detail"
                        title={ws.containerDetail.podName}
                        onClose={() => ws.setContainerDetail(null)}
                        defaultWidth={620}
                        minWidth={320}
                        maxWidth={1200}
                    >
                        <ContainerDetailPanel
                            ns={ws.containerDetail.namespace}
                            podName={ws.containerDetail.podName}
                            context={ws.containerDetail.context}
                        />
                    </ResizablePanel>
                )}
            </div>

            {/* Context menu */}
            {ws.contextMenu && (
                <ContextMenu
                    x={ws.contextMenu.x}
                    y={ws.contextMenu.y}
                    items={ws.contextMenu.items}
                    onClose={() => ws.setContextMenu(null)}
                />
            )}
        </div>
    );
}
