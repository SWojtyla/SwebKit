import { useState } from "react";
import {
    X,
    Terminal,
    Box,
    FileText,
    TerminalSquare,
    ArrowRightLeft,
    Sparkles,
} from "lucide-react";
import { PodLogView } from "./PodLogView";
import { ContainerDetailPanel } from "./ContainerDetailPanel";
import { PhaseBadge } from "./shared/PhaseBadge";
import type { PodInfo } from "@/lib/types";

interface PodDetailPanelProps {
    pod: PodInfo;
    ns: string;
    onClose: () => void;
    onViewYaml?: () => void;
    onOpenShell?: () => void;
    onPortForward?: () => void;
    onAskAi?: () => void;
}

type DetailTab = "logs" | "containers";

export function PodDetailPanel({
    pod,
    ns,
    onClose,
    onViewYaml,
    onOpenShell,
    onPortForward,
    onAskAi,
}: PodDetailPanelProps) {
    const [activeTab, setActiveTab] = useState<DetailTab>("logs");

    return (
        <div className="flex h-full flex-col" data-testid="pod-detail-panel">
            {/* Identity row: name/status/close only, so a long pod name never fights the action
          buttons below for space (previously all of this shared one row and the last button —
          "Ask AI" — was silently clipped off the edge of the panel). */}
            <div className="flex items-center gap-2 border-b px-4 py-2">
                <Terminal className="h-4 w-4 shrink-0 text-fg-muted" />
                <span
                    className="truncate font-mono text-sm font-semibold"
                    title={pod.name}
                >
                    {pod.name}
                </span>
                <PhaseBadge phase={pod.status} className="shrink-0" />
                {pod.context && (
                    <span
                        className="shrink-0 rounded-md bg-accent-soft px-1.5 py-0.5 text-xs text-accent-ink"
                        title="Kubernetes context"
                        data-testid="pod-detail-context"
                    >
                        {pod.context}
                    </span>
                )}
                <button
                    onClick={onClose}
                    className="ml-auto inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-fg-muted hover:bg-surface-2 hover:text-fg"
                    aria-label="Close"
                    title="Close"
                >
                    <X className="h-4 w-4" />
                </button>
            </div>

            {/* Action row: Logs/Containers are the two views this panel actually renders below, so
          they stay as labeled tabs; Shell/Port-Forward/Ask AI/YAML are one-shot actions that
          navigate elsewhere, so they're icon-only (with a tooltip) to stay compact regardless
          of how narrow the panel is resized. */}
            <div className="flex items-center gap-1 border-b px-4">
                <button
                    onClick={() => setActiveTab("logs")}
                    className={`-mb-px flex items-center gap-1.5 border-b-2 px-2 py-2 text-xs font-medium ${activeTab === "logs" ? "border-primary text-fg" : "border-transparent text-fg-muted hover:text-fg"}`}
                    data-testid="pod-tab-logs"
                >
                    <Terminal className="h-3 w-3" /> Logs
                </button>
                <button
                    onClick={() => setActiveTab("containers")}
                    className={`-mb-px flex items-center gap-1.5 border-b-2 px-2 py-2 text-xs font-medium ${activeTab === "containers" ? "border-primary text-fg" : "border-transparent text-fg-muted hover:text-fg"}`}
                    data-testid="pod-tab-containers"
                >
                    <Box className="h-3 w-3" /> Containers
                </button>
                <div className="ml-auto flex shrink-0 items-center gap-1 py-1">
                    {onViewYaml && (
                        <button
                            onClick={onViewYaml}
                            className="inline-flex h-7 w-7 items-center justify-center rounded-md border bg-surface-1 text-fg-muted hover:bg-surface-2 hover:text-fg"
                            data-testid="pod-yaml-btn"
                            title="View YAML"
                            aria-label="View YAML"
                        >
                            <FileText className="h-3.5 w-3.5" />
                        </button>
                    )}
                    {onOpenShell && (
                        <button
                            onClick={onOpenShell}
                            className="inline-flex h-7 w-7 items-center justify-center rounded-md border bg-surface-1 text-fg-muted hover:bg-surface-2 hover:text-fg"
                            data-testid="pod-shell-btn"
                            title="Open shell in pod"
                            aria-label="Open shell in pod"
                        >
                            <TerminalSquare className="h-3.5 w-3.5" />
                        </button>
                    )}
                    {onPortForward && (
                        <button
                            onClick={onPortForward}
                            className="inline-flex h-7 w-7 items-center justify-center rounded-md border bg-surface-1 text-fg-muted hover:bg-surface-2 hover:text-fg"
                            data-testid="pod-port-forward-btn"
                            title="Port-forward…"
                            aria-label="Port-forward…"
                        >
                            <ArrowRightLeft className="h-3.5 w-3.5" />
                        </button>
                    )}
                    {onAskAi && (
                        <button
                            onClick={onAskAi}
                            className="inline-flex h-7 w-7 items-center justify-center rounded-md border bg-surface-1 text-fg-muted hover:bg-surface-2 hover:text-fg"
                            data-testid="pod-ask-ai-btn"
                            title="Ask AI about this pod"
                            aria-label="Ask AI about this pod"
                        >
                            <Sparkles className="h-3.5 w-3.5" />
                        </button>
                    )}
                </div>
            </div>

            <div className="flex-1 overflow-hidden">
                {activeTab === "logs" && (
                    <PodLogView
                        ns={ns}
                        podName={pod.name}
                        containers={pod.containers}
                        context={pod.context}
                    />
                )}
                {activeTab === "containers" && (
                    <ContainerDetailPanel
                        ns={ns}
                        podName={pod.name}
                        context={pod.context}
                    />
                )}
            </div>
        </div>
    );
}
