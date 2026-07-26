import { useState } from "react";
import { AlertTriangle } from "lucide-react";
import { useProfile, useDemoMode } from "@/lib/hooks";
import { ServiceBusSettings } from "./ServiceBusSettings";
import { AksSettings } from "./AksSettings";
import { RedisSettings } from "./RedisSettings";
import { StorageSettings } from "./StorageSettings";
import { AgentSettings } from "./AgentSettings";
import { GeneralSettings } from "./GeneralSettings";
import { DevOpsSettings } from "./DevOpsSettings";
import { DiagnosticsSettings } from "./DiagnosticsSettings";
import { AppearanceSettings } from "./AppearanceSettings";

const tabs = [
  { id: "general", label: "General" },
  { id: "service-bus", label: "Service Bus" },
  { id: "aks", label: "AKS" },
  { id: "redis", label: "Redis" },
  { id: "storage", label: "Storage" },
  { id: "agent", label: "AI Agent" },
  { id: "devops", label: "DevOps" },
  { id: "diagnostics", label: "Diagnostics" },
  { id: "appearance", label: "Appearance" },
] as const;

type TabId = (typeof tabs)[number]["id"];

// Tabs whose connection fields are inert while demo mode is on — demo mode
// substitutes fixed sample data for these, so any real values entered here
// have no effect until demo mode is turned off (Dashboard toggle).
const DEMO_AFFECTED_TABS: ReadonlySet<TabId> = new Set(["service-bus", "aks", "redis", "storage"]);

export function SettingsPage() {
  const [activeTab, setActiveTab] = useState<TabId>("general");
  const { data: profile, isLoading } = useProfile();
  const { data: demoMode } = useDemoMode();

  if (isLoading) {
    return (
      <div className="flex h-full items-center justify-center text-muted-foreground">
        Loading settings...
      </div>
    );
  }

  if (!profile) {
    return (
      <div className="flex h-full items-center justify-center text-muted-foreground">
        Failed to load settings. Is the sidecar running?
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col" data-testid="settings-page">
      <div className="border-b px-6 py-4">
        <h1 className="text-xl font-bold" data-testid="settings-title">Settings</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Configure projects, environments, and connections
        </p>
      </div>

      {demoMode?.isDemoMode && DEMO_AFFECTED_TABS.has(activeTab) && (
        <div className="flex items-center gap-2 border-b bg-yellow-500/10 px-6 py-2 text-xs text-yellow-600" data-testid="settings-demo-mode-banner">
          <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
          Demo mode is on — this tab's connection fields are inert until you turn it off from the Dashboard.
        </div>
      )}

      <div className="flex flex-1 overflow-hidden">
        <div className="w-48 border-r p-2" data-testid="settings-tabs">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              data-testid={`settings-tab-${tab.id}`}
              className={`w-full rounded-md px-3 py-2 text-left text-sm font-medium transition-colors ${
                activeTab === tab.id
                  ? "bg-primary text-primary-foreground"
                  : "text-muted-foreground hover:bg-accent hover:text-accent-foreground"
              }`}
            >
              {tab.label}
            </button>
          ))}
        </div>

        <div className="flex-1 overflow-auto p-6" data-testid="settings-content">
          {activeTab === "general" && <GeneralSettings />}
          {activeTab === "service-bus" && <ServiceBusSettings />}
          {activeTab === "aks" && <AksSettings />}
          {activeTab === "redis" && <RedisSettings />}
          {activeTab === "storage" && <StorageSettings />}
          {activeTab === "agent" && <AgentSettings />}
          {activeTab === "devops" && <DevOpsSettings />}
          {activeTab === "diagnostics" && <DiagnosticsSettings />}
          {activeTab === "appearance" && <AppearanceSettings />}
        </div>
      </div>
    </div>
  );
}
