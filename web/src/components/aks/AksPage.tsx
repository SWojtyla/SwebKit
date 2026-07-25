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

const tabs = [
  { id: "deployments", label: "Deployments" },
  { id: "pods", label: "Pods" },
  { id: "services", label: "Services" },
  { id: "helm", label: "Helm" },
  { id: "secrets", label: "Secrets" },
  { id: "events", label: "Events" },
] as const;

type TabId = (typeof tabs)[number]["id"];

export function AksPage() {
  const [activeTab, setActiveTab] = useState<TabId>("deployments");
  const [namespace, setNamespace] = useState<string | null>(null);
  const { data: namespaces, isLoading: nsLoading } = useAksNamespaces();
  const { data: testResult } = useAksTestConnection();

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
      <div className="flex border-b" data-testid="aks-tabs">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            data-testid={`aks-tab-${tab.id}`}
            className={`px-4 py-2 text-sm font-medium ${
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
      <div className="flex-1 overflow-auto" data-testid="aks-content">
        {!namespace ? (
          <div className="flex h-full items-center justify-center text-sm text-muted-foreground" data-testid="aks-empty-state">
            Select a namespace to view resources
          </div>
        ) : (
          <>
            {activeTab === "deployments" && <DeploymentsTab ns={namespace} />}
            {activeTab === "pods" && <PodsTab ns={namespace} />}
            {activeTab === "services" && <ServicesTab ns={namespace} />}
            {activeTab === "helm" && <HelmTab ns={namespace} />}
            {activeTab === "secrets" && <SecretsTab ns={namespace} />}
            {activeTab === "events" && <EventsTab ns={namespace} />}
          </>
        )}
      </div>
    </div>
  );
}
