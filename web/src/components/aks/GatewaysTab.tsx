import { useAksGateways } from "@/lib/hooks";
import { ResourceTable } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { GatewayInfo } from "@/lib/types";

interface GatewaysTabProps {
  ns: string;
  isMulti?: boolean;
}

export function GatewaysTab({ ns, isMulti }: GatewaysTabProps) {
  const { data: gateways, isLoading } = useAksGateways(ns);
  const ws = useAksWorkspace();

  const buildMenu = (gw: GatewayInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(gw.name) },
    { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("gateway", gw.name, gw.namespace) },
    { label: "Analyze network", icon: "📶", onClick: () => ws.navigateToAnalysis() },
  ];

  return (
    <ResourceTable
      data={gateways}
      isLoading={isLoading}
      isMulti={isMulti}
      testIdPrefix="gateway"
      tableBodyTestId="gateways-table-body"
      emptyMessage="No gateways found"
      onRowContextMenu={(e, gw) => ws.showContextMenu(e, buildMenu(gw))}
      columns={[
        { header: "Class", cell: (gw) => <span className="text-xs text-muted-foreground">{gw.gatewayClass ?? "—"}</span> },
        { header: "Status", cell: (gw) => (
          <span className={
            gw.status === "Ready" ? "text-green-500" :
            gw.status === "Pending" ? "text-yellow-500" :
            "text-muted-foreground"
          }>
            {gw.status}
          </span>
        )},
        { header: "Addresses", cell: (gw) => (
          <span className="text-xs text-muted-foreground">{gw.addresses.length > 0 ? gw.addresses.join(", ") : "—"}</span>
        )},
        { header: "Attached Routes", cell: (gw) => gw.attachedRoutes },
      ]}
    />
  );
}
