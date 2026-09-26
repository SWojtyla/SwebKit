import { useCallback, type MouseEvent } from "react";
import { useAksGatewayClasses } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksActions } from "./shared/aks-workspace-context";
import type { ContextMenuItem } from "./ContextMenu";
import type { GatewayClassInfo } from "@/lib/types";

function gatewayClassStatusRank(gc: GatewayClassInfo): number {
  return gc.status === "Accepted" ? 1 : gc.status === "Pending" ? 0 : -1;
}

const columns: Column<GatewayClassInfo>[] = [
  { header: "Controller", cell: (gc) => <span className="text-xs text-muted-foreground">{gc.controllerName ?? "—"}</span> },
  { header: "Status", cell: (gc) => (
    <span className={
      gc.status === "Accepted" ? "text-success" :
      gc.status === "Pending" ? "text-warning" :
      "text-muted-foreground"
    }>
      {gc.status}
    </span>
  ), sortValue: gatewayClassStatusRank },
];

export function GatewayClassesTab() {
  const { data: classes, isLoading, error } = useAksGatewayClasses();
  const ws = useAksActions();

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
      error={error}
      isMulti={false}
      testIdPrefix="gatewayclass"
      tableBodyTestId="gatewayclasses-table-body"
      emptyMessage="No gateway classes found"
      onRowClick={(gc) => ws.openYaml("gatewayclass", gc.name, "default")}
      onRowContextMenu={handleRowContextMenu}
      columns={columns}
      defaultSort={{ sortValue: gatewayClassStatusRank, direction: "asc" }}
    />
  );
}
