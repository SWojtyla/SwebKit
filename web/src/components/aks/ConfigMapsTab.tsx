import { useState } from "react";
import { useAksConfigMaps } from "@/lib/hooks";
import { ResourceTable } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { ConfigMapInfo } from "@/lib/types";

interface ConfigMapsTabProps {
  ns: string;
  isMulti?: boolean;
}

export function ConfigMapsTab({ ns, isMulti }: ConfigMapsTabProps) {
  const { data: configmaps, isLoading } = useAksConfigMaps(ns);
  const [selected, setSelected] = useState<ConfigMapInfo | null>(null);
  const ws = useAksWorkspace();

  const buildMenu = (cm: ConfigMapInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(cm.name) },
    { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("configmap", cm.name, cm.namespace) },
    { label: "View keys", icon: "🔑", onClick: () => ws.copyToClipboard(Object.keys(cm.data).join(", ")) },
  ];

  const selectedKey = selected ? `${selected.namespace}/${selected.name}` : null;

  return (
    <div className="flex h-full">
      <div className="flex-1">
        <ResourceTable
          data={configmaps}
          isLoading={isLoading}
          isMulti={isMulti}
          testIdPrefix="configmap"
          tableBodyTestId="configmaps-table-body"
          emptyMessage="No config maps found"
          onRowClick={(cm) => setSelected(cm)}
          onRowContextMenu={(e, cm) => ws.showContextMenu(e, buildMenu(cm))}
          selectedKey={selectedKey}
          columns={[
            { header: "Keys", cell: (cm) => (
              <span className="text-xs text-muted-foreground">
                {Object.keys(cm.data).length > 0 ? Object.keys(cm.data).join(", ") : "—"}
              </span>
            )},
          ]}
        />
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
