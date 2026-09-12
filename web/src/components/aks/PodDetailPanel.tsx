import { useState } from "react";
import { X, Terminal, Box, FileText, TerminalSquare, ArrowRightLeft, Sparkles } from "lucide-react";
import { PodLogView } from "./PodLogView";
import { ContainerDetailPanel } from "./ContainerDetailPanel";
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

export function PodDetailPanel({ pod, ns, onClose, onViewYaml, onOpenShell, onPortForward, onAskAi }: PodDetailPanelProps) {
  const [activeTab, setActiveTab] = useState<DetailTab>("logs");

  return (
    <div className="flex h-full flex-col" data-testid="pod-detail-panel">
      <div className="flex items-center gap-2 border-b px-4 py-2">
        <Terminal className="h-4 w-4" />
        <span className="text-sm font-medium">{pod.name}</span>
        <span className="text-xs text-muted-foreground">· {pod.status}</span>
        <div className="ml-auto flex items-center gap-2">
          <button
            onClick={() => setActiveTab("logs")}
            className={`flex items-center gap-1 rounded px-2 py-1 text-xs ${activeTab === "logs" ? "bg-accent" : "hover:bg-accent"}`}
            data-testid="pod-tab-logs"
          >
            <Terminal className="h-3 w-3" /> Logs
          </button>
          <button
            onClick={() => setActiveTab("containers")}
            className={`flex items-center gap-1 rounded px-2 py-1 text-xs ${activeTab === "containers" ? "bg-accent" : "hover:bg-accent"}`}
            data-testid="pod-tab-containers"
          >
            <Box className="h-3 w-3" /> Containers
          </button>
          {onViewYaml && (
            <button
              onClick={onViewYaml}
              className="flex items-center gap-1 rounded px-2 py-1 text-xs hover:bg-accent"
              data-testid="pod-yaml-btn"
            >
              <FileText className="h-3 w-3" /> YAML
            </button>
          )}
          {onOpenShell && (
            <button
              onClick={onOpenShell}
              className="flex items-center gap-1 rounded px-2 py-1 text-xs hover:bg-accent"
              data-testid="pod-shell-btn"
              title="Open shell in pod"
            >
              <TerminalSquare className="h-3 w-3" /> Shell
            </button>
          )}
          {onPortForward && (
            <button
              onClick={onPortForward}
              className="flex items-center gap-1 rounded px-2 py-1 text-xs hover:bg-accent"
              data-testid="pod-port-forward-btn"
              title="Port-forward…"
            >
              <ArrowRightLeft className="h-3 w-3" /> Port-Forward
            </button>
          )}
          {onAskAi && (
            <button
              onClick={onAskAi}
              className="flex items-center gap-1 rounded px-2 py-1 text-xs hover:bg-accent"
              data-testid="pod-ask-ai-btn"
              title="Ask AI about this pod"
            >
              <Sparkles className="h-3 w-3" /> Ask AI
            </button>
          )}
          <button onClick={onClose} className="rounded p-1 hover:bg-accent">
            <X className="h-4 w-4" />
          </button>
        </div>
      </div>

      <div className="flex-1 overflow-hidden">
        {activeTab === "logs" && (
          <PodLogView ns={ns} podName={pod.name} containers={pod.containers} />
        )}
        {activeTab === "containers" && (
          <ContainerDetailPanel ns={ns} podName={pod.name} />
        )}
      </div>
    </div>
  );
}
