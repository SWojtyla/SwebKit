import { useCallback, useMemo, type MouseEvent } from "react";
import { useAksConfigMaps } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksActions, useAksNav } from "./shared/aks-workspace-context";
import type { ContextMenuItem } from "./ContextMenu";
import type { AksQueryTarget, ConfigMapInfo } from "@/lib/types";

interface ConfigMapsTabProps {
    targets: AksQueryTarget[];
    isMulti?: boolean;
    showContext?: boolean;
}

const columns: Column<ConfigMapInfo>[] = [
    {
        header: "Keys",
        cell: (cm) => (
            <span className="text-xs text-muted-foreground">
                {cm.keys.length > 0 ? cm.keys.join(", ") : "—"}
            </span>
        ),
    },
];

export function ConfigMapsTab({
    targets,
    isMulti,
    showContext,
}: ConfigMapsTabProps) {
    const {
        data: configmaps,
        isLoading,
        error,
        contextErrors,
    } = useAksConfigMaps(targets);
    const nav = useAksNav();
    const actions = useAksActions();
    const ws = useMemo(() => ({ ...nav, ...actions }), [nav, actions]);

    const buildMenu = useCallback(
        (cm: ConfigMapInfo): ContextMenuItem[] => [
            {
                label: "Copy name",
                icon: "📋",
                onClick: () => ws.copyToClipboard(cm.name),
            },
            {
                label: "View YAML",
                icon: "{ }",
                onClick: () =>
                    ws.openYaml("configmap", cm.name, cm.namespace, cm.context),
            },
            {
                label: "View keys",
                icon: "🔑",
                onClick: () => ws.setSelectedConfigMap(cm),
            },
        ],
        [ws],
    );

    const handleRowContextMenu = useCallback(
        (e: MouseEvent<HTMLTableRowElement>, cm: ConfigMapInfo) =>
            ws.showContextMenu(e, buildMenu(cm)),
        [ws, buildMenu],
    );

    const selectedKey = ws.selectedConfigMap
        ? `${ws.selectedConfigMap.context ? `${ws.selectedConfigMap.context}:` : ""}${ws.selectedConfigMap.namespace}/${ws.selectedConfigMap.name}`
        : null;

    return (
        <ResourceTable
            data={configmaps}
            isLoading={isLoading}
            error={error}
            isMulti={isMulti}
            showContext={showContext}
            contextErrors={contextErrors}
            testIdPrefix="configmap"
            tableBodyTestId="configmaps-table-body"
            emptyMessage="No config maps found"
            onRowClick={(cm) => ws.setSelectedConfigMap(cm)}
            onRowContextMenu={handleRowContextMenu}
            selectedKey={selectedKey}
            columns={columns}
        />
    );
}
