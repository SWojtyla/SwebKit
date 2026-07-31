import { useAksGatewayClasses } from "@/lib/hooks";
import { ResourceTable } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { GatewayClassInfo } from "@/lib/types";

export function GatewayClassesTab() {
  const { data: classes, isLoading } = useAksGatewayClasses();
  const ws = useAksWorkspace();

  const buildMenu = (gc: GatewayClassInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(gc.name) },
    { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("gatewayclass", gc.name, "default") },
  ];

  return (
    <ResourceTable
      data={classes}
      isLoading={isLoading}
      isMulti={false}
      testIdPrefix="gatewayclass"
      tableBodyTestId="gatewayclasses-table-body"
      emptyMessage="No gateway classes found"
      onRowContextMenu={(e, gc) => ws.showContextMenu(e, buildMenu(gc))}
      columns={[
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
      ]}
    />
  );
}
