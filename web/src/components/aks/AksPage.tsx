import {
  AksWorkspaceProvider,
  useAksWorkspace,
  aksRefreshIntervals,
  directTabs,
  networkTabs,
  extraTabs,
  networkTabIds,
} from "./shared/AksWorkspaceContext";
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
import { HpaTab } from "./HpaTab";
import { GatewayClassesTab } from "./GatewayClassesTab";
import { GatewaysTab } from "./GatewaysTab";
import { PodDetailPanel } from "./PodDetailPanel";
import { YamlViewer } from "./YamlViewer";
import { HelmDetailPanel } from "./HelmDetailPanel";
import { PortForwardPanel } from "./PortForwardPanel";
import { AnalysisPanel } from "./AnalysisPanel";
import { SecretDetailPanel } from "./SecretDetailPanel";
import { ConfigMapDetailPanel } from "./ConfigMapDetailPanel";
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
import { RefreshCw, Loader2 } from "lucide-react";

export function AksPage() {
  return (
    <AksWorkspaceProvider>
      <AksPageContent />
    </AksWorkspaceProvider>
  );
}

function AksPageContent() {
  const ws = useAksWorkspace();
  const isNetworkTabActive = networkTabIds.has(ws.activeTab);

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
          currentContext={ws.currentContext}
          onChange={ws.handleContextChange}
          isLoading={ws.contextLoading}
        />

        <span className="text-sm font-medium">Namespace:</span>
        <NamespaceSelector
          namespaces={ws.namespaces}
          selected={ws.selectedNamespaces}
          onChange={ws.setSelectedNamespaces}
          isLoading={ws.nsLoading}
          error={ws.nsError}
          disabledReason={
            ws.activeTab === "gatewayclasses"
              ? "Not applicable — GatewayClasses are cluster-scoped, not namespaced"
              : undefined
          }
        />

        {ws.contextLoading && (
          <div className="flex items-center gap-1.5 text-xs text-primary" data-testid="aks-loading-indicator">
            <Loader2 className="h-3.5 w-3.5 animate-spin" />
            Switching context…
          </div>
        )}

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
              onChange={(e) => ws.setAutoRefresh(e.target.checked)}
              disabled={!ws.namespaceToken}
              data-testid="aks-auto-refresh-checkbox"
            />
            <span>Auto</span>
          </label>
          <select
            value={ws.refreshInterval}
            onChange={(e) => ws.setRefreshInterval(Number(e.target.value))}
            disabled={!ws.autoRefresh || !ws.namespaceToken}
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
            disabled={!ws.namespaceToken}
            className="flex shrink-0 items-center gap-1 whitespace-nowrap rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
            data-testid="aks-refresh-btn"
          >
            <RefreshCw className={`h-3.5 w-3.5 ${ws.isAksFetching ? "animate-spin" : ""}`} />
            Refresh
          </button>
          <button
            onClick={async () => {
              const pods = ws.allPods ?? (await ws.refetchPods()).data ?? [];
              ws.openMultiPodLogs(pods);
            }}
            disabled={!ws.namespaceToken || ws.podsFetching}
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
      <div className="flex border-b overflow-x-auto" data-testid="aks-tabs">
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
          Network <span className="text-xs">{ws.networkMenuOpen ? "▲" : "▼"}</span>
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
        <div className="flex gap-1 border-b bg-card px-2 py-1" data-testid="aks-network-submenu">
          <span className="text-xs text-muted-foreground py-1 px-2">Network</span>
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
      <div className="flex flex-1 overflow-hidden" data-testid="aks-content">
        <div className="flex-1 overflow-auto">
          {!ws.namespaceToken ? (
            <div
              className="flex h-full items-center justify-center text-sm text-muted-foreground"
              data-testid="aks-empty-state"
            >
              Select a namespace to view resources
            </div>
          ) : (
            <>
              {ws.activeTab === "deployments" && (
                <DeploymentsTab ns={ws.namespaceToken} isMulti={ws.isMultiNamespace} />
              )}
              {ws.activeTab === "statefulsets" && (
                <StatefulSetsTab ns={ws.namespaceToken} isMulti={ws.isMultiNamespace} />
              )}
              {ws.activeTab === "pods" && (
                <PodsTab ns={ws.namespaceToken} isMulti={ws.isMultiNamespace} />
              )}
              {ws.activeTab === "services" && (
                <ServicesTab ns={ws.namespaceToken} isMulti={ws.isMultiNamespace} />
              )}
              {ws.activeTab === "ingresses" && (
                <IngressesTab ns={ws.namespaceToken} isMulti={ws.isMultiNamespace} />
              )}
              {ws.activeTab === "httproutes" && (
                <HttpRoutesTab ns={ws.namespaceToken} isMulti={ws.isMultiNamespace} />
              )}
              {ws.activeTab === "gatewayclasses" && <GatewayClassesTab />}
              {ws.activeTab === "gateways" && (
                <GatewaysTab ns={ws.namespaceToken} isMulti={ws.isMultiNamespace} />
              )}
              {ws.activeTab === "cronjobs" && (
                <CronJobsTab ns={ws.namespaceToken} isMulti={ws.isMultiNamespace} />
              )}
              {ws.activeTab === "jobs" && (
                <JobsTab ns={ws.namespaceToken} isMulti={ws.isMultiNamespace} />
              )}
              {ws.activeTab === "configmaps" && (
                <ConfigMapsTab ns={ws.namespaceToken} isMulti={ws.isMultiNamespace} />
              )}
              {ws.activeTab === "secrets" && (
                <SecretsTab ns={ws.namespaceToken} isMulti={ws.isMultiNamespace} />
              )}
              {ws.activeTab === "hpa" && <HpaTab ns={ws.namespaceToken} isMulti={ws.isMultiNamespace} />}
              {ws.activeTab === "helm" && (
                <HelmTab ns={ws.namespaceToken} isMulti={ws.isMultiNamespace} />
              )}
              {ws.activeTab === "events" && (
                <EventsTab ns={ws.namespaceToken} isMulti={ws.isMultiNamespace} />
              )}
              {ws.activeTab === "portforward" && (
                <PortForwardPanel
                  ns={ws.namespaceToken}
                  selectedPod={ws.selectedPod?.name ?? null}
                  context={ws.currentContext}
                  pods={ws.allPods}
                />
              )}
              {ws.activeTab === "analysis" && <AnalysisPanel ns={ws.namespaceToken} />}
            </>
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
              onViewYaml={() => ws.openYaml("pod", ws.selectedPod!.name, ws.selectedPod!.namespace)}
              onOpenShell={() => ws.setShellPod(ws.selectedPod)}
              onPortForward={() => ws.openPortForward(ws.selectedPod!)}
              onAskAi={() => ws.setAskAiPod(ws.selectedPod)}
            />
          </ResizablePanel>
        )}
        {ws.shellPod && (
          <PodShellPanel
            namespace={ws.shellPod.namespace}
            pod={ws.shellPod.name}
            container={ws.shellPod.containers[0] ?? null}
            context={ws.currentContext}
            onClose={() => ws.setShellPod(null)}
          />
        )}
        {ws.askAiPod && (
          <ContextualAssistant
            featureArea="Aks"
            title={`pod ${ws.askAiPod.name}`}
            selection={{ namespace: ws.askAiPod.namespace, pod: ws.askAiPod.name }}
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
            <SecretDetailPanel secret={ws.selectedSecret} onClose={() => ws.setSelectedSecret(null)} />
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
            <ConfigMapDetailPanel configMap={ws.selectedConfigMap} onClose={() => ws.setSelectedConfigMap(null)} />
          </ResizablePanel>
        )}
        {ws.showMultiPodLogs && ws.multiPodNamespace && (
          <ResizablePanel
            storageKey="aks-multi-pod-logs"
            defaultWidth={620}
            minWidth={320}
            maxWidth={1200}
            showHeader={false}
          >
            <MultiPodLogView
              ns={ws.multiPodNamespace}
              pods={ws.multiPodNames}
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
