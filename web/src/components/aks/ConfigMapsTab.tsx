import { useState } from "react";
import { useAksConfigMaps } from "@/lib/hooks";
import type { ConfigMapInfo } from "@/lib/types";

interface ConfigMapsTabProps {
  ns: string;
  onContextMenu?: (e: React.MouseEvent, cm: ConfigMapInfo) => void;
}

export function ConfigMapsTab({ ns, onContextMenu }: ConfigMapsTabProps) {
  const { data: configmaps, isLoading } = useAksConfigMaps(ns);
  const [selected, setSelected] = useState<ConfigMapInfo | null>(null);

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!configmaps || configmaps.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No config maps found</div>;

  return (
    <div className="flex h-full">
      <div className="flex-1 p-4">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b text-left text-xs text-muted-foreground">
              <th className="py-2 pr-4">Name</th>
              <th className="py-2 pr-4">Keys</th>
            </tr>
          </thead>
          <tbody data-testid="configmaps-table-body">
            {configmaps.map((cm) => (
              <tr
                key={cm.name}
                data-testid={`configmap-row-${cm.name}`}
                className={`cursor-pointer border-b last:border-0 ${selected?.name === cm.name ? "bg-accent" : "hover:bg-accent/50"}`}
                onClick={() => setSelected(cm)}
                onContextMenu={(e) => onContextMenu?.(e, cm)}
              >
                <td className="py-2 pr-4 font-medium">{cm.name}</td>
                <td className="py-2 pr-4 text-xs text-muted-foreground">
                  {Object.keys(cm.data).length > 0 ? Object.keys(cm.data).join(", ") : "—"}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {selected && (
        <div className="w-96 border-l p-4 overflow-auto" data-testid="configmap-detail">
          <div className="mb-2 flex items-center justify-between">
            <h3 className="font-semibold text-sm">{selected.name}</h3>
            <button onClick={() => setSelected(null)} className="text-xs text-muted-foreground hover:text-foreground">Close</button>
          </div>
          {Object.entries(selected.data).length === 0 ? (
            <p className="text-xs text-muted-foreground">No data keys</p>
          ) : (
            <div className="space-y-3">
              {Object.entries(selected.data).map(([key, value]) => (
                <div key={key}>
                  <div className="text-xs font-medium text-muted-foreground">{key}</div>
                  <pre className="mt-1 max-h-48 overflow-auto rounded border bg-background p-2 text-xs font-mono whitespace-pre-wrap break-all">
                    {value}
                  </pre>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
