import { useCallback, type MouseEvent } from "react";
import { useAksServices } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { ServiceInfo } from "@/lib/types";

interface ServicesTabProps {
  ns: string;
  isMulti?: boolean;
}

const columns: Column<ServiceInfo>[] = [
  { header: "Type", cell: (svc) => svc.type },
  { header: "Cluster IP", cell: (svc) => <span className="text-muted-foreground">{svc.clusterIp}</span> },
  { header: "External", cell: (svc) => (
    <span className="text-muted-foreground">
      {svc.externalAddresses.length > 0 ? svc.externalAddresses.join(", ") : "—"}
    </span>
  )},
  { header: "Ports", cell: (svc) => (
    <span className="text-xs">
      {svc.ports.map((p) => `${p.port}:${p.targetPort ?? p.port}/${p.protocol}`).join(", ")}
    </span>
  )},
];

export function ServicesTab({ ns, isMulti }: ServicesTabProps) {
  const { data: services, isLoading } = useAksServices(ns);
  const ws = useAksWorkspace();

  const buildMenu = useCallback((svc: ServiceInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(svc.name) },
    { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("service", svc.name, svc.namespace) },
  ], [ws]);

  const handleRowContextMenu = useCallback(
    (e: MouseEvent<HTMLTableRowElement>, svc: ServiceInfo) => ws.showContextMenu(e, buildMenu(svc)),
    [ws, buildMenu],
  );

  return (
    <ResourceTable
      data={services}
      isLoading={isLoading}
      isMulti={isMulti}
      testIdPrefix="service"
      tableBodyTestId="services-table-body"
      emptyMessage="No services found"
      onRowContextMenu={handleRowContextMenu}
      columns={columns}
    />
  );
}
