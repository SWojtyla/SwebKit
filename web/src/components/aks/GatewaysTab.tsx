import { useCallback, type MouseEvent } from "react";
import { useAksGateways } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksActions } from "./shared/aks-workspace-context";
import type { ContextMenuItem } from "./ContextMenu";
import type { AksQueryTarget, GatewayInfo } from "@/lib/types";

interface GatewaysTabProps {
    targets: AksQueryTarget[];
    isMulti?: boolean;
    showContext?: boolean;
}

function gatewayStatusRank(gw: GatewayInfo): number {
    return gw.status === "Ready" ? 1 : gw.status === "Pending" ? 0 : -1;
}

const columns: Column<GatewayInfo>[] = [
    {
        header: "Class",
        cell: (gw) => (
            <span className="text-xs text-muted-foreground">
                {gw.gatewayClass ?? "—"}
            </span>
        ),
    },
    {
        header: "Status",
        cell: (gw) => (
            <span
                className={
                    gw.status === "Ready"
                        ? "text-success"
                        : gw.status === "Pending"
                          ? "text-warning"
                          : "text-muted-foreground"
                }
            >
                {gw.status}
            </span>
        ),
        sortValue: gatewayStatusRank,
    },
    {
        header: "Addresses",
        cell: (gw) => (
            <span className="text-xs text-muted-foreground">
                {gw.addresses.length > 0 ? gw.addresses.join(", ") : "—"}
            </span>
        ),
    },
    {
        header: "Attached Routes",
        cell: (gw) => gw.attachedRoutes,
        sortValue: (gw) => gw.attachedRoutes,
    },
];

export function GatewaysTab({
    targets,
    isMulti,
    showContext,
}: GatewaysTabProps) {
    const {
        data: gateways,
        isLoading,
        error,
        contextErrors,
    } = useAksGateways(targets);
    const ws = useAksActions();

    const buildMenu = useCallback(
        (gw: GatewayInfo): ContextMenuItem[] => [
            {
                label: "Copy name",
                icon: "📋",
                onClick: () => ws.copyToClipboard(gw.name),
            },
            {
                label: "View YAML",
                icon: "{ }",
                onClick: () =>
                    ws.openYaml("gateway", gw.name, gw.namespace, gw.context),
            },
            {
                label: "Analyze network",
                icon: "📶",
                onClick: () => ws.navigateToAnalysis(),
            },
        ],
        [ws],
    );

    const handleRowContextMenu = useCallback(
        (e: MouseEvent<HTMLTableRowElement>, gw: GatewayInfo) =>
            ws.showContextMenu(e, buildMenu(gw)),
        [ws, buildMenu],
    );

    return (
        <ResourceTable
            data={gateways}
            isLoading={isLoading}
            error={error}
            isMulti={isMulti}
            showContext={showContext}
            contextErrors={contextErrors}
            testIdPrefix="gateway"
            tableBodyTestId="gateways-table-body"
            emptyMessage="No gateways found"
            onRowClick={(gw) =>
                ws.openYaml("gateway", gw.name, gw.namespace, gw.context)
            }
            onRowContextMenu={handleRowContextMenu}
            columns={columns}
            defaultSort={{ sortValue: gatewayStatusRank, direction: "asc" }}
        />
    );
}
