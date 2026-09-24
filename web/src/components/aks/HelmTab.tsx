import { useCallback, type MouseEvent } from "react";
import { useAksHelmReleases } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { HelmReleaseInfo } from "@/lib/types";
import { formatLocalDateTime } from "@/lib/datetime";

interface HelmTabProps {
    ns: string;
    isMulti?: boolean;
}

// `failed` is a hard failure and gets the same destructive-red every other broken resource in
// the app uses (Pods' Failed/Error, Deployments' Unavailable) — it was previously the same
// warning-yellow as an in-progress `pending-*` state, which reads as "still working on it"
// rather than "this release is actually broken."
function helmStatusClass(status: string): string {
    if (status === "deployed") return "text-success";
    if (status === "failed") return "text-destructive";
    return "text-warning";
}

const columns: Column<HelmReleaseInfo>[] = [
    {
        header: "Chart",
        cell: (rel) => (
            <span className="text-muted-foreground">{rel.chart ?? "—"}</span>
        ),
    },
    {
        header: "Version",
        cell: (rel) => (
            <span className="text-muted-foreground">
                {rel.appVersion ?? rel.chartVersion ?? "—"}
            </span>
        ),
    },
    {
        header: "Revision",
        cell: (rel) => rel.revision,
        sortValue: (rel) => rel.revision,
    },
    {
        header: "Status",
        cell: (rel) => (
            <span className={helmStatusClass(rel.status)}>{rel.status}</span>
        ),
        sortValue: (rel) =>
            rel.status === "deployed" ? 1 : rel.status === "failed" ? -1 : 0,
    },
    {
        header: "Updated",
        cell: (rel) => (
            <span className="text-xs text-muted-foreground">
                {rel.updated ? formatLocalDateTime(rel.updated) : "—"}
            </span>
        ),
    },
];

export function HelmTab({ ns, isMulti }: HelmTabProps) {
    const { data: releases, isLoading, error } = useAksHelmReleases(ns);
    const ws = useAksWorkspace();

    // No "Rollback" entry here: it's a real, working action already, in HelmDetailPanel (opened by
    // History/Values below) — this menu previously duplicated it as a dead, permanently-disabled
    // stub built around a native `prompt()`.
    const buildMenu = useCallback(
        (rel: HelmReleaseInfo): ContextMenuItem[] => [
            {
                label: "Copy name",
                icon: "📋",
                onClick: () => ws.copyToClipboard(rel.name),
            },
            {
                label: "History",
                icon: "📜",
                onClick: () => ws.setHelmRelease(rel),
            },
            {
                label: "Values",
                icon: "📋",
                onClick: () => ws.setHelmRelease(rel),
            },
        ],
        [ws],
    );

    const handleRowClick = useCallback(
        (rel: HelmReleaseInfo) => ws.setHelmRelease(rel),
        [ws],
    );
    const handleRowContextMenu = useCallback(
        (e: MouseEvent<HTMLTableRowElement>, rel: HelmReleaseInfo) =>
            ws.showContextMenu(e, buildMenu(rel)),
        [ws, buildMenu],
    );

    return (
        <ResourceTable
            data={releases}
            isLoading={isLoading}
            error={error}
            isMulti={isMulti}
            testIdPrefix="helm"
            tableBodyTestId="helm-table-body"
            emptyMessage="No Helm releases found"
            onRowClick={handleRowClick}
            onRowContextMenu={handleRowContextMenu}
            columns={columns}
            defaultSort={{
                sortValue: (rel) =>
                    rel.status === "deployed"
                        ? 1
                        : rel.status === "failed"
                          ? -1
                          : 0,
                direction: "asc",
            }}
        />
    );
}
