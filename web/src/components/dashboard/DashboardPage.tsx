import { Link } from "react-router";
import { Activity, FlaskConical, Settings, Zap } from "lucide-react";
import {
    useProfile,
    useDemoMode,
    useToggleDemoMode,
} from "@/lib/hooks";
import {
    useMonitoringStream,
    useProactiveInsightsFeed,
} from "@/lib/hooks/useMonitoring";
import { StartDemoTourButton } from "@/components/layout/DemoTour";
import { CrossFeatureDemoButton } from "@/components/agent/CrossFeatureDemoButton";
import { CockpitCommandBar } from "./CockpitCommandBar";
import { AgentStatusStrip } from "./AgentStatusStrip";
import { ServiceGrid } from "./ServiceGrid";
import { WatchTiles } from "./WatchTiles";
import { CockpitTopology } from "./CockpitTopology";
import { InsightsFeed } from "./InsightsFeed";
import { PinnedShortcuts } from "./PinnedShortcuts";
import { useServiceHealth } from "./useServiceHealth";

/**
 * AI Cockpit — the dashboard is a composition of focused cockpit components:
 * a command bar that queues prompts into the docked agent panel, a status
 * strip, a consolidated per-service health/config grid, live watch tiles, the
 * workspace topology graph, the proactive-insights feed, and pinned shortcuts.
 */
export function DashboardPage() {
    const { data: profile } = useProfile();
    const { data: demoMode } = useDemoMode();
    const toggleDemo = useToggleDemoMode();
    const isDemo = demoMode?.isDemoMode ?? false;

    const maps = profile?.config.maps ?? [];
    const health = useServiceHealth();

    const { insights, addInsight, dismiss } = useProactiveInsightsFeed();
    useMonitoringStream(
        () => {},
        (insight) => addInsight(insight),
    );

    return (
        <div className="animate-fade-in-up p-6" data-testid="dashboard-page">
            <div className="mb-2 flex items-center justify-between">
                <div>
                    <h1
                        className="gradient-text text-2xl font-bold"
                        data-testid="dashboard-title"
                    >
                        AI Cockpit
                    </h1>
                    <p className="mt-1 text-sm text-muted-foreground">
                        Command center for your cloud workspace
                    </p>
                </div>
                <div className="flex items-center gap-2">
                    <CrossFeatureDemoButton />
                    <StartDemoTourButton />
                    <FlaskConical
                        className={`h-4 w-4 ${isDemo ? "text-primary" : "text-muted-foreground"}`}
                    />
                    <button
                        data-testid="dashboard-demo-mode-toggle"
                        onClick={() => toggleDemo.mutate(!isDemo)}
                        disabled={toggleDemo.isPending}
                        title={
                            toggleDemo.isPending
                                ? "Toggling demo mode…"
                                : undefined
                        }
                        className={`rounded-lg border px-3 py-1.5 text-sm font-medium transition-all ${
                            isDemo
                                ? "border-primary bg-primary text-primary-foreground shadow-sm"
                                : "hover:bg-accent"
                        }`}
                    >
                        {isDemo ? "Demo Mode ON" : "Enable Demo Mode"}
                    </button>
                </div>
            </div>

            <CockpitCommandBar />
            <AgentStatusStrip />

            <div className="mt-6 grid gap-6 lg:grid-cols-3">
                <div className="lg:col-span-2">
                    <h2 className="mb-3 flex items-center gap-2 text-sm font-semibold text-muted-foreground">
                        <Zap className="h-4 w-4" /> Services
                    </h2>
                    <ServiceGrid health={health} />

                    <h2 className="mb-3 mt-6 flex items-center gap-2 text-sm font-semibold text-muted-foreground">
                        <Activity className="h-4 w-4" /> Live Watch
                    </h2>
                    <WatchTiles health={health} />

                    <div className="mt-6">
                        <CockpitTopology maps={maps} />
                    </div>

                    <div className="mt-6">
                        <PinnedShortcuts />
                    </div>
                </div>

                <div className="space-y-6">
                    <InsightsFeed insights={insights} onDismiss={dismiss} />

                    <Link
                        to="/settings"
                        data-testid="settings-quick-link"
                        className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground"
                    >
                        <Settings className="h-4 w-4" />
                        Configure connections in Settings
                    </Link>
                </div>
            </div>
        </div>
    );
}
