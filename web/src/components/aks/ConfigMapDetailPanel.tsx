import { X } from "lucide-react";
import type { ConfigMapInfo } from "@/lib/types";

interface Props {
  configMap: ConfigMapInfo;
  onClose: () => void;
}

export function ConfigMapDetailPanel({ configMap, onClose }: Props) {
  const entries = Object.entries(configMap.data);

  return (
    <div className="flex h-full flex-col" data-testid="configmap-detail-panel">
      <div className="flex items-center justify-between border-b px-4 py-3">
        <div>
          <h2 className="text-lg font-semibold" data-testid="configmap-detail-name">{configMap.name}</h2>
          <p className="text-xs text-muted-foreground">{configMap.namespace}</p>
        </div>
        <button onClick={onClose} className="text-muted-foreground hover:text-foreground" data-testid="configmap-detail-close">
          <X className="h-4 w-4" />
        </button>
      </div>
      <div className="flex-1 overflow-auto p-4">
        {entries.length === 0 ? (
          <p className="text-xs text-muted-foreground">No data keys in this config map</p>
        ) : (
          <div className="space-y-3">
            {entries.map(([key, value]) => (
              <div key={key} data-testid={`configmap-data-${key}`}>
                <div className="text-xs font-medium text-muted-foreground">{key}</div>
                <pre className="mt-1 max-h-48 overflow-auto rounded border bg-background p-2 text-xs font-mono whitespace-pre-wrap break-all">
                  {value}
                </pre>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
