import { useCallback, type MouseEvent } from "react";
import { useAksIngresses, useAksDeleteIngress } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { IngressInfo } from "@/lib/types";

interface IngressesTabProps {
  ns: string;
  isMulti?: boolean;
}

const columns: Column<IngressInfo>[] = [
  { header: "Class", cell: (ing) => <span className="text-muted-foreground">{ing.ingressClass ?? "—"}</span> },
  { header: "Hosts", cell: (ing) => (
    <span className="text-xs">{ing.rules.map((r) => r.host).filter(Boolean).join(", ") || "—"}</span>
  )},
  { header: "Addresses", cell: (ing) => (
    <span className="text-xs text-muted-foreground">{ing.addresses.length > 0 ? ing.addresses.join(", ") : "—"}</span>
  )},
  { header: "Rules", cell: (ing) => <span className="text-xs text-muted-foreground">{ing.rules.length} rule(s)</span> },
];

export function IngressesTab({ ns, isMulti }: IngressesTabProps) {
  const { data: ingresses, isLoading } = useAksIngresses(ns);
  const ws = useAksWorkspace();
  const deleteIngress = useAksDeleteIngress();

  const buildMenu = useCallback((ing: IngressInfo): ContextMenuItem[] => {
    const host = ing.rules.find((r) => r.host)?.host;
    return [
      { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(ing.name) },
      { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("ingress", ing.name, ing.namespace) },
      { label: "Edit YAML", icon: "✎", onClick: () => ws.openYaml("ingress", ing.name, ing.namespace) },
      { label: "Open URL in browser", icon: "🔗", onClick: () => { if (host) window.open(`http://${host}`, "_blank"); }, disabled: !host },
      { label: "Copy URL", icon: "📋", onClick: () => { if (host) ws.copyToClipboard(`http://${host}`); }, disabled: !host },
      { label: "Analyze ingress", icon: "🔍", onClick: () => ws.navigateToAnalysis() },
      { label: "", separator: true, onClick: () => {} },
      { label: "Delete Ingress", icon: "✕", onClick: () => {
        ws.requestConfirm({
          message: `Delete ingress "${ing.name}"?`,
          resourceName: ing.name,
          onConfirm: () => deleteIngress.mutate({ ns: ing.namespace, name: ing.name }),
        });
      }, destructive: true },
    ];
  }, [ws, deleteIngress.mutate]);

  const handleRowContextMenu = useCallback(
    (e: MouseEvent<HTMLTableRowElement>, ing: IngressInfo) => ws.showContextMenu(e, buildMenu(ing)),
    [ws, buildMenu],
  );

  return (
    <ResourceTable
      data={ingresses}
      isLoading={isLoading}
      isMulti={isMulti}
      testIdPrefix="ingress"
      tableBodyTestId="ingresses-table-body"
      emptyMessage="No ingresses found"
      onRowContextMenu={handleRowContextMenu}
      columns={columns}
    />
  );
}
