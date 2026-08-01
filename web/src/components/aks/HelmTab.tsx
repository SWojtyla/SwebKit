import { useCallback, type MouseEvent } from "react";
import { useAksHelmReleases } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { HelmReleaseInfo } from "@/lib/types";

interface HelmTabProps {
  ns: string;
  isMulti?: boolean;
}

const columns: Column<HelmReleaseInfo>[] = [
  { header: "Chart", cell: (rel) => <span className="text-muted-foreground">{rel.chart ?? "—"}</span> },
  { header: "Version", cell: (rel) => <span className="text-muted-foreground">{rel.appVersion ?? rel.chartVersion ?? "—"}</span> },
  { header: "Revision", cell: (rel) => rel.revision },
  { header: "Status", cell: (rel) => (
    <span className={rel.status === "deployed" ? "text-success" : "text-warning"}>
      {rel.status}
    </span>
  )},
  { header: "Updated", cell: (rel) => (
    <span className="text-xs text-muted-foreground">{rel.updated ? new Date(rel.updated).toLocaleString() : "—"}</span>
  )},
];

export function HelmTab({ ns, isMulti }: HelmTabProps) {
  const { data: releases, isLoading } = useAksHelmReleases(ns);
  const ws = useAksWorkspace();

  const buildMenu = useCallback((rel: HelmReleaseInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(rel.name) },
    { label: "History", icon: "📜", onClick: () => ws.setHelmRelease(rel) },
    { label: "Values", icon: "📋", onClick: () => ws.setHelmRelease(rel) },
    { label: "Rollback", icon: "↶", onClick: () => {
      const rev = prompt(`Rollback to which revision?`);
      if (rev === null) return;
      const n = parseInt(rev, 10);
      if (isNaN(n)) return;
      // Rollback is intentionally disabled until the sidecar endpoint is fully wired.
    }, disabled: true },
  ], [ws]);

  const handleRowClick = useCallback((rel: HelmReleaseInfo) => ws.setHelmRelease(rel), [ws]);
  const handleRowContextMenu = useCallback(
    (e: MouseEvent<HTMLTableRowElement>, rel: HelmReleaseInfo) => ws.showContextMenu(e, buildMenu(rel)),
    [ws, buildMenu],
  );

  return (
    <ResourceTable
      data={releases}
      isLoading={isLoading}
      isMulti={isMulti}
      testIdPrefix="helm"
      tableBodyTestId="helm-table-body"
      emptyMessage="No Helm releases found"
      onRowClick={handleRowClick}
      onRowContextMenu={handleRowContextMenu}
      columns={columns}
    />
  );
}
