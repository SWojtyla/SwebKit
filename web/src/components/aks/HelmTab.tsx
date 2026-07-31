import { useAksHelmReleases } from "@/lib/hooks";
import { ResourceTable } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { HelmReleaseInfo } from "@/lib/types";

interface HelmTabProps {
  ns: string;
  isMulti?: boolean;
}

export function HelmTab({ ns, isMulti }: HelmTabProps) {
  const { data: releases, isLoading } = useAksHelmReleases(ns);
  const ws = useAksWorkspace();

  const buildMenu = (rel: HelmReleaseInfo): ContextMenuItem[] => [
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
  ];

  return (
    <ResourceTable
      data={releases}
      isLoading={isLoading}
      isMulti={isMulti}
      testIdPrefix="helm"
      tableBodyTestId="helm-table-body"
      emptyMessage="No Helm releases found"
      onRowClick={(rel) => ws.setHelmRelease(rel)}
      onRowContextMenu={(e, rel) => ws.showContextMenu(e, buildMenu(rel))}
      columns={[
        { header: "Chart", cell: (rel) => <span className="text-muted-foreground">{rel.chart ?? "—"}</span> },
        { header: "Version", cell: (rel) => <span className="text-muted-foreground">{rel.appVersion ?? rel.chartVersion ?? "—"}</span> },
        { header: "Revision", cell: (rel) => rel.revision },
        { header: "Status", cell: (rel) => (
          <span className={rel.status === "deployed" ? "text-green-500" : "text-yellow-500"}>
            {rel.status}
          </span>
        )},
        { header: "Updated", cell: (rel) => (
          <span className="text-xs text-muted-foreground">{rel.updated ? new Date(rel.updated).toLocaleString() : "—"}</span>
        )},
      ]}
    />
  );
}
