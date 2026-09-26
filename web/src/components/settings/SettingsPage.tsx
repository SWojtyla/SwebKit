import { useEffect, useState } from "react";
import { useLocation, useNavigate } from "react-router";
import { AlertTriangle } from "lucide-react";
import { useProfile, useDemoMode } from "@/lib/hooks";
import {
    useSettingsReadiness,
    type SettingsReadinessArea,
} from "@/lib/hooks/useProfile";
import { ServiceBusSettings } from "./ServiceBusSettings";
import { AksSettings } from "./AksSettings";
import { RedisSettings } from "./RedisSettings";
import { SqlSettings } from "./SqlSettings";
import { StorageSettings } from "./StorageSettings";
import { AgentSettings } from "./AgentSettings";
import { GeneralSettings } from "./GeneralSettings";
import { DiagnosticsSettings } from "./DiagnosticsSettings";
import { AppearanceSettings } from "./AppearanceSettings";
import { WorkspaceMapSettings } from "./WorkspaceMapSettings";

const tabs = [
    { id: "general", label: "General" },
    { id: "service-bus", label: "Service Bus" },
    { id: "aks", label: "AKS" },
    { id: "redis", label: "Redis" },
    { id: "sql", label: "SQL" },
    { id: "storage", label: "Storage" },
    { id: "agent", label: "AI Agent" },
    { id: "map", label: "Map" },
    { id: "diagnostics", label: "Diagnostics" },
    { id: "appearance", label: "Appearance" },
] as const;

type TabId = (typeof tabs)[number]["id"];

const TAB_IDS: ReadonlySet<string> = new Set(tabs.map((tab) => tab.id));

// Tabs whose connection fields are inert while demo mode is on — demo mode
// substitutes fixed sample data for these, so any real values entered here
// have no effect until demo mode is turned off (Dashboard toggle).
const DEMO_AFFECTED_TABS: ReadonlySet<TabId> = new Set([
    "service-bus",
    "aks",
    "redis",
    "sql",
    "storage",
]);

// The tabs `useSettingsReadiness` has an opinion about — one entry per area with a real
// "configured or not" concept. `TabId` and `SettingsReadinessArea` happen to share the same
// string values for these five, which is what makes the direct index below type-check.
const READINESS_TABS: ReadonlySet<TabId> = new Set([
    "service-bus",
    "aks",
    "redis",
    "sql",
    "storage",
]);

export function SettingsPage() {
    const [activeTab, setActiveTab] = useState<TabId>("general");
    const { data: profile, isLoading } = useProfile();
    const { data: demoMode } = useDemoMode();
    const location = useLocation();
    const navigate = useNavigate();
    const readiness = useSettingsReadiness();

    // Jump straight to a section requested from the command palette (each Settings sub-section is
    // registered there as a nav item carrying `state: { tab }`), mirroring the same `location.state`
    // deep-link convention every other feature area's palette entries already use.
    useEffect(() => {
        const state = location.state as { tab?: string } | null;
        if (state?.tab && TAB_IDS.has(state.tab)) {
            // eslint-disable-next-line react-hooks/set-state-in-effect -- one-shot location.state deep-link consumption; the paired navigate() must live in an effect anyway
            setActiveTab(state.tab as TabId);
            navigate(location.pathname, { replace: true, state: null });
        }
    }, [location, navigate]);

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
                <h1 className="text-xl font-bold" data-testid="settings-title">
                    Settings
                </h1>
                <p className="mt-1 text-sm text-muted-foreground">
                    Configure projects, environments, and connections
                </p>
            </div>

            {demoMode?.isDemoMode && DEMO_AFFECTED_TABS.has(activeTab) && (
                <div
                    className="flex items-center gap-2 border-b bg-warning/10 px-6 py-2 text-xs text-warning"
                    data-testid="settings-demo-mode-banner"
                >
                    <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
                    Demo mode is on — this tab's connection fields are inert
                    until you turn it off from the Dashboard.
                </div>
            )}

            <div className="flex flex-1 overflow-hidden">
                <div className="w-48 border-r p-2" data-testid="settings-tabs">
                    {tabs.map((tab) => {
                        // `null` while the profile hasn't loaded yet (shouldn't happen here — this
                        // component already gated on `profile` above — but keep the check honest).
                        const ready = READINESS_TABS.has(tab.id)
                            ? readiness?.[tab.id as SettingsReadinessArea]
                            : undefined;

                        return (
                            <button
                                key={tab.id}
                                onClick={() => setActiveTab(tab.id)}
                                data-testid={`settings-tab-${tab.id}`}
                                className={`flex w-full items-center gap-2 rounded-md px-3 py-2 text-left text-sm font-medium transition-colors ${
                                    activeTab === tab.id
                                        ? "bg-primary text-primary-foreground"
                                        : "text-muted-foreground hover:bg-accent hover:text-accent-foreground"
                                }`}
                            >
                                <span className="flex-1">{tab.label}</span>
                                {ready !== undefined && (
                                    <span
                                        className={`h-1.5 w-1.5 shrink-0 rounded-full ${
                                            ready
                                                ? "bg-success"
                                                : "bg-current opacity-40"
                                        }`}
                                        title={
                                            ready
                                                ? "Configured"
                                                : "Not configured"
                                        }
                                        data-testid={`settings-tab-readiness-${tab.id}`}
                                    />
                                )}
                            </button>
                        );
                    })}
                </div>

                <div
                    className="flex-1 overflow-auto p-6"
                    data-testid="settings-content"
                >
                    {activeTab === "general" && <GeneralSettings />}
                    {activeTab === "service-bus" && <ServiceBusSettings />}
                    {activeTab === "aks" && <AksSettings />}
                    {activeTab === "redis" && <RedisSettings />}
                    {activeTab === "sql" && <SqlSettings />}
                    {activeTab === "storage" && <StorageSettings />}
                    {activeTab === "agent" && <AgentSettings />}
                    {activeTab === "map" && <WorkspaceMapSettings />}
                    {activeTab === "diagnostics" && <DiagnosticsSettings />}
                    {activeTab === "appearance" && <AppearanceSettings />}
                </div>
            </div>
        </div>
    );
}
