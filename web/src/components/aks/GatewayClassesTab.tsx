import { useCallback, type MouseEvent } from "react";
import { useAksGatewayClasses } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { GatewayClassInfo } from "@/lib/types";

const columns: Column<GatewayClassInfo>[] = [
  { header: "Controller", cell: (gc) => <span className="text-xs text-muted-foreground">{gc.controllerName ?? "—"}</span> },
  { header: "Status", cell: (gc) => (
    <span className={
      gc.status === "Accepted" ? "text-green-500" :
      gc.status === "Pending" ? "text-yellow-500" :
      "text-muted-foreground"
    }>
      {gc.status}
    </span>
  )},
];

export function GatewayClassesTab() {
  const { data: classes, isLoading } = useAksGatewayClasses();
  const ws = useAksWorkspace();

  const buildMenu = useCallback((gc: GatewayClassInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(gc.name) },
    { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("gatewayclass", gc.name, "default") },
  ], [ws]);

  const handleRowContextMenu = useCallback(
    (e: MouseEvent<HTMLTableRowElement>, gc: GatewayClassInfo) => ws.showContextMenu(e, buildMenu(gc)),
    [ws, buildMenu],
  );

  return (
    <ResourceTable
      data={classes}
      isLoading={isLoading}
      isMulti={false}
      testIdPrefix="gatewayclass"
      tableBodyTestId="gatewayclasses-table-body"
      emptyMessage="No gateway classes found"
      onRowContextMenu={handleRowContextMenu}
      columns={columns}
    />
  );
}
