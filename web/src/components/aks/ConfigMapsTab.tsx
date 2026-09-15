import { useCallback, type MouseEvent } from "react";
import { useAksConfigMaps } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { ConfigMapInfo } from "@/lib/types";

interface ConfigMapsTabProps {
  ns: string;
  isMulti?: boolean;
}

const columns: Column<ConfigMapInfo>[] = [
  { header: "Keys", cell: (cm) => (
    <span className="text-xs text-muted-foreground">
      {cm.keys.length > 0 ? cm.keys.join(", ") : "—"}
    </span>
  )},
];

export function ConfigMapsTab({ ns, isMulti }: ConfigMapsTabProps) {
  const { data: configmaps, isLoading, error } = useAksConfigMaps(ns);
  const ws = useAksWorkspace();

  const buildMenu = useCallback((cm: ConfigMapInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(cm.name) },
    { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("configmap", cm.name, cm.namespace) },
    { label: "View keys", icon: "🔑", onClick: () => ws.setSelectedConfigMap(cm) },
  ], [ws]);

  const handleRowContextMenu = useCallback(
    (e: MouseEvent<HTMLTableRowElement>, cm: ConfigMapInfo) => ws.showContextMenu(e, buildMenu(cm)),
    [ws, buildMenu],
  );

  const selectedKey = ws.selectedConfigMap ? `${ws.selectedConfigMap.namespace}/${ws.selectedConfigMap.name}` : null;

  return (
    <ResourceTable
      data={configmaps}
      isLoading={isLoading}
      error={error}
      isMulti={isMulti}
      testIdPrefix="configmap"
      tableBodyTestId="configmaps-table-body"
      emptyMessage="No config maps found"
      onRowClick={(cm) => ws.setSelectedConfigMap(cm)}
      onRowContextMenu={handleRowContextMenu}
      selectedKey={selectedKey}
      columns={columns}
    />
  );
}
