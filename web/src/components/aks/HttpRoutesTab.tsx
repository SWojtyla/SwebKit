import { useCallback, type MouseEvent } from "react";
import { useAksHttpRoutes, useAksDeleteHttpRoute } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { HttpRouteInfo } from "@/lib/types";

interface HttpRoutesTabProps {
  ns: string;
  isMulti?: boolean;
}

const columns: Column<HttpRouteInfo>[] = [
  { header: "Hosts", cell: (route) => (
    <span className="text-xs">{route.hostnames.length > 0 ? route.hostnames.join(", ") : "—"}</span>
  )},
  { header: "Parents", cell: (route) => (
    <span className="text-xs text-muted-foreground">{route.parentRefs.length > 0 ? route.parentRefs.join(", ") : "—"}</span>
  )},
  { header: "Backends", cell: (route) => (
    <span className="text-xs text-muted-foreground">{route.backendRefs.length > 0 ? route.backendRefs.join(", ") : "—"}</span>
  )},
  { header: "Status", cell: (route) => (
    <span className={
      route.status === "Accepted" ? "text-green-500" :
      route.status === "Pending" ? "text-yellow-500" :
      "text-muted-foreground"
    }>
      {route.status}
    </span>
  )},
];

export function HttpRoutesTab({ ns, isMulti }: HttpRoutesTabProps) {
  const { data: routes, isLoading } = useAksHttpRoutes(ns);
  const ws = useAksWorkspace();
  const deleteHttpRoute = useAksDeleteHttpRoute();

  const buildMenu = useCallback((route: HttpRouteInfo): ContextMenuItem[] => {
    const host = route.hostnames[0];
    return [
      { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(route.name) },
      { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("httproute", route.name, route.namespace) },
      { label: "Edit YAML", icon: "✎", onClick: () => ws.openYaml("httproute", route.name, route.namespace) },
      { label: "Open URL in browser", icon: "🔗", onClick: () => { if (host) window.open(`https://${host}`, "_blank"); }, disabled: !host },
      { label: "Copy URL", icon: "📋", onClick: () => { if (host) ws.copyToClipboard(`https://${host}`); }, disabled: !host },
      { label: "", separator: true, onClick: () => {} },
      { label: "Delete HTTPRoute", icon: "✕", onClick: () => {
        ws.requestConfirm({
          message: `Delete HTTPRoute "${route.name}"?`,
          resourceName: route.name,
          onConfirm: () => deleteHttpRoute.mutate({ ns: route.namespace, name: route.name }),
        });
      }, destructive: true },
    ];
  }, [ws, deleteHttpRoute.mutate]);

  const handleRowContextMenu = useCallback(
    (e: MouseEvent<HTMLTableRowElement>, route: HttpRouteInfo) => ws.showContextMenu(e, buildMenu(route)),
    [ws, buildMenu],
  );

  return (
    <ResourceTable
      data={routes}
      isLoading={isLoading}
      isMulti={isMulti}
      testIdPrefix="httproute"
      tableBodyTestId="httproutes-table-body"
      emptyMessage="No HTTP routes found"
      onRowContextMenu={handleRowContextMenu}
      columns={columns}
    />
  );
}
