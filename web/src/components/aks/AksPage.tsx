import { useState } from "react";
import {
  useAksNamespaces,
  useAksTestConnection,
} from "@/lib/hooks";
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
import { HpaTab } from "./HpaTab";
import { PodDetailPanel } from "./PodDetailPanel";
import { YamlViewer } from "./YamlViewer";
import { HelmDetailPanel } from "./HelmDetailPanel";
import type { PodInfo } from "@/lib/types";

const tabs = [
  { id: "deployments", label: "Deployments" },
  { id: "statefulsets", label: "StatefulSets" },
  { id: "pods", label: "Pods" },
  { id: "services", label: "Services" },
  { id: "ingresses", label: "Ingresses" },
  { id: "cronjobs", label: "CronJobs" },
  { id: "jobs", label: "Jobs" },
  { id: "configmaps", label: "ConfigMaps" },
  { id: "secrets", label: "Secrets" },
  { id: "hpa", label: "HPA" },
  { id: "helm", label: "Helm" },
  { id: "events", label: "Events" },
] as const;

type TabId = (typeof tabs)[number]["id"];

export function AksPage() {
  const [activeTab, setActiveTab] = useState<TabId>("deployments");
  const [namespace, setNamespace] = useState<string | null>(null);
  const { data: namespaces, isLoading: nsLoading } = useAksNamespaces();
  const { data: testResult } = useAksTestConnection();
  const [selectedPod, setSelectedPod] = useState<PodInfo | null>(null);
  const [yamlResource, setYamlResource] = useState<{ kind: string; name: string } | null>(null);
  const [helmRelease, setHelmRelease] = useState<string | null>(null);

  return (
    <div className="flex h-full flex-col" data-testid="aks-page">
      {/* Header with namespace selector */}
      <div className="flex items-center gap-3 border-b px-4 py-2">
        <span className="text-sm font-medium">Namespace:</span>
        <select
          data-testid="aks-namespace-select"
          value={namespace ?? ""}
          onChange={(e) => setNamespace(e.target.value || null)}
          className="rounded-md border bg-card px-3 py-1.5 text-sm"
        >
          <option value="">Select namespace...</option>
          {namespaces?.map((ns) => (
            <option key={ns} value={ns}>
              {ns}
            </option>
          ))}
        </select>
        {nsLoading && (
          <span className="text-xs text-muted-foreground">Loading...</span>
        )}
        {testResult && (
          <span className={`ml-auto flex items-center gap-1.5 text-xs ${testResult.connected ? "text-green-500" : "text-destructive"}`} data-testid="aks-connection-status">
            <span className={`h-2 w-2 rounded-full ${testResult.connected ? "bg-green-500" : "bg-destructive"}`} />
            {testResult.connected ? "Connected" : "Disconnected"}
            {testResult.error && ` — ${testResult.error}`}
          </span>
        )}
      </div>

      {/* Tabs */}
      <div className="flex border-b overflow-x-auto" data-testid="aks-tabs">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            data-testid={`aks-tab-${tab.id}`}
            className={`whitespace-nowrap px-4 py-2 text-sm font-medium ${
              activeTab === tab.id
                ? "border-b-2 border-primary text-foreground"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* Content */}
      <div className="flex flex-1 overflow-hidden" data-testid="aks-content">
        <div className="flex-1 overflow-auto">
          {!namespace ? (
            <div className="flex h-full items-center justify-center text-sm text-muted-foreground" data-testid="aks-empty-state">
              Select a namespace to view resources
            </div>
          ) : (
            <>
              {activeTab === "deployments" && <DeploymentsTab ns={namespace} />}
              {activeTab === "statefulsets" && <StatefulSetsTab ns={namespace} />}
              {activeTab === "pods" && <PodsTab ns={namespace} onPodClick={setSelectedPod} />}
              {activeTab === "services" && <ServicesTab ns={namespace} />}
              {activeTab === "ingresses" && <IngressesTab ns={namespace} />}
              {activeTab === "cronjobs" && <CronJobsTab ns={namespace} />}
              {activeTab === "jobs" && <JobsTab ns={namespace} />}
              {activeTab === "configmaps" && <ConfigMapsTab ns={namespace} />}
              {activeTab === "secrets" && <SecretsTab ns={namespace} />}
              {activeTab === "hpa" && <HpaTab ns={namespace} />}
              {activeTab === "helm" && <HelmTab ns={namespace} onReleaseClick={setHelmRelease} />}
              {activeTab === "events" && <EventsTab ns={namespace} />}
            </>
          )}
        </div>

        {/* Side panel for detail views */}
        {selectedPod && namespace && (
          <div className="w-2/5 border-l">
            <PodDetailPanel
              pod={selectedPod}
              ns={namespace}
              onClose={() => setSelectedPod(null)}
            />
          </div>
        )}
        {yamlResource && namespace && (
          <div className="w-2/5 border-l">
            <YamlViewer
              ns={namespace}
              kind={yamlResource.kind}
              name={yamlResource.name}
              onClose={() => setYamlResource(null)}
            />
          </div>
        )}
        {helmRelease && namespace && (
          <div className="w-2/5 border-l">
            <HelmDetailPanel
              ns={namespace}
              release={helmRelease}
              onClose={() => setHelmRelease(null)}
            />
          </div>
        )}
      </div>
    </div>
  );
}
