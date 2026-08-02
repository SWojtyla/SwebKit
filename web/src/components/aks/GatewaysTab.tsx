import { useCallback, type MouseEvent } from "react";
import { useAksGateways } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { GatewayInfo } from "@/lib/types";

interface GatewaysTabProps {
  ns: string;
  isMulti?: boolean;
}

const columns: Column<GatewayInfo>[] = [
  { header: "Class", cell: (gw) => <span className="text-xs text-muted-foreground">{gw.gatewayClass ?? "—"}</span> },
  { header: "Status", cell: (gw) => (
    <span className={
      gw.status === "Ready" ? "text-success" :
      gw.status === "Pending" ? "text-warning" :
      "text-muted-foreground"
    }>
      {gw.status}
    </span>
  )},
  { header: "Addresses", cell: (gw) => (
    <span className="text-xs text-muted-foreground">{gw.addresses.length > 0 ? gw.addresses.join(", ") : "—"}</span>
  )},
  { header: "Attached Routes", cell: (gw) => gw.attachedRoutes },
];

export function GatewaysTab({ ns, isMulti }: GatewaysTabProps) {
  const { data: gateways, isLoading } = useAksGateways(ns);
  const ws = useAksWorkspace();

  const buildMenu = useCallback((gw: GatewayInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(gw.name) },
    { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("gateway", gw.name, gw.namespace) },
    { label: "Analyze network", icon: "📶", onClick: () => ws.navigateToAnalysis() },
  ], [ws]);

  const handleRowContextMenu = useCallback(
    (e: MouseEvent<HTMLTableRowElement>, gw: GatewayInfo) => ws.showContextMenu(e, buildMenu(gw)),
    [ws, buildMenu],
  );

  return (
    <ResourceTable
      data={gateways}
      isLoading={isLoading}
      isMulti={isMulti}
      testIdPrefix="gateway"
      tableBodyTestId="gateways-table-body"
      emptyMessage="No gateways found"
      onRowContextMenu={handleRowContextMenu}
      columns={columns}
    />
  );
}
