import { useCallback, type MouseEvent } from "react";
import { useAksServices } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksActions } from "./shared/aks-workspace-context";
import type { ContextMenuItem } from "./ContextMenu";
import type { AksQueryTarget, ServiceInfo } from "@/lib/types";

interface ServicesTabProps {
    targets: AksQueryTarget[];
    isMulti?: boolean;
    showContext?: boolean;
}

const columns: Column<ServiceInfo>[] = [
    { header: "Type", cell: (svc) => svc.type, sortValue: (svc) => svc.type },
    {
        header: "Cluster IP",
        cell: (svc) => (
            <span className="text-muted-foreground">{svc.clusterIp}</span>
        ),
    },
    {
        header: "External",
        cell: (svc) => (
            <span className="text-muted-foreground">
                {svc.externalAddresses.length > 0
                    ? svc.externalAddresses.join(", ")
                    : "—"}
            </span>
        ),
    },
    {
        header: "Ports",
        cell: (svc) => (
            <span className="text-xs">
                {svc.ports
                    .map(
                        (p) =>
                            `${p.port}:${p.targetPort ?? p.port}/${p.protocol}`,
                    )
                    .join(", ")}
            </span>
        ),
    },
];

export function ServicesTab({
    targets,
    isMulti,
    showContext,
}: ServicesTabProps) {
    const {
        data: services,
        isLoading,
        error,
        contextErrors,
    } = useAksServices(targets);
    const ws = useAksActions();

    const buildMenu = useCallback(
        (svc: ServiceInfo): ContextMenuItem[] => [
            {
                label: "Copy name",
                icon: "📋",
                onClick: () => ws.copyToClipboard(svc.name),
            },
            {
                label: "View YAML",
                icon: "{ }",
                onClick: () =>
                    ws.openYaml(
                        "service",
                        svc.name,
                        svc.namespace,
                        svc.context,
                    ),
            },
        ],
        [ws],
    );

    const handleRowContextMenu = useCallback(
        (e: MouseEvent<HTMLTableRowElement>, svc: ServiceInfo) =>
            ws.showContextMenu(e, buildMenu(svc)),
        [ws, buildMenu],
    );

    return (
        <ResourceTable
            data={services}
            isLoading={isLoading}
            error={error}
            isMulti={isMulti}
            showContext={showContext}
            contextErrors={contextErrors}
            testIdPrefix="service"
            tableBodyTestId="services-table-body"
            emptyMessage="No services found"
            onRowClick={(svc) =>
                ws.openYaml("service", svc.name, svc.namespace, svc.context)
            }
            onRowContextMenu={handleRowContextMenu}
            columns={columns}
        />
    );
}
