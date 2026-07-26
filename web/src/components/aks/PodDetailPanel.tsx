import { useState } from "react";
import { X, Terminal, Box, FileText } from "lucide-react";
import { PodLogView } from "./PodLogView";
import { ContainerDetailPanel } from "./ContainerDetailPanel";
import type { PodInfo } from "@/lib/types";

interface PodDetailPanelProps {
  pod: PodInfo;
  ns: string;
  onClose: () => void;
  onViewYaml?: () => void;
}

type DetailTab = "logs" | "containers";

export function PodDetailPanel({ pod, ns, onClose, onViewYaml }: PodDetailPanelProps) {
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
