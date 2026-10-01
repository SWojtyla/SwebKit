import { useCallback, useMemo, type MouseEvent } from "react";
import { useAksSecrets } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksActions, useAksNav } from "./shared/aks-workspace-context";
import type { ContextMenuItem } from "./ContextMenu";
import type { AksQueryTarget, SecretInfo } from "@/lib/types";

interface SecretsTabProps {
    targets: AksQueryTarget[];
    isMulti?: boolean;
    showContext?: boolean;
}

// Cells only read from their row param, so this can be a stable module-level
// constant instead of being rebuilt (and defeating ResourceTable's memo) on
// every render.
const columns: Column<SecretInfo>[] = [
    {
        header: "Type",
        cell: (secret) => (
            <span className="text-muted-foreground">{secret.type}</span>
        ),
    },
    {
        header: "Keys",
        cell: (secret) => (
            <span className="text-xs text-muted-foreground">
                {secret.keys.length > 0 ? secret.keys.join(", ") : "—"}
            </span>
        ),
    },
];

export function SecretsTab({ targets, isMulti, showContext }: SecretsTabProps) {
    const {
        data: secrets,
        isLoading,
        error,
        contextErrors,
    } = useAksSecrets(targets);
    const nav = useAksNav();
    const actions = useAksActions();
    const ws = useMemo(() => ({ ...nav, ...actions }), [nav, actions]);

    const buildMenu = useCallback(
        (secret: SecretInfo): ContextMenuItem[] => [
            {
                label: "Copy name",
                icon: "📋",
                onClick: () => ws.copyToClipboard(secret.name),
            },
            {
                label: "View YAML",
                icon: "{ }",
                onClick: () =>
                    ws.openYaml(
                        "secret",
                        secret.name,
                        secret.namespace,
                        secret.context,
                    ),
            },
            {
                label: "View keys",
                icon: "🔑",
                onClick: () => ws.setSelectedSecret(secret),
            },
        ],
        [ws],
    );

    const handleRowContextMenu = useCallback(
        (e: MouseEvent<HTMLTableRowElement>, secret: SecretInfo) =>
            ws.showContextMenu(e, buildMenu(secret)),
        [ws, buildMenu],
    );

    return (
        <ResourceTable
            data={secrets}
            isLoading={isLoading}
            error={error}
            isMulti={isMulti}
            showContext={showContext}
            contextErrors={contextErrors}
            testIdPrefix="secret"
            tableBodyTestId="secrets-table-body"
            emptyMessage="No secrets found"
            onRowClick={(secret) => ws.setSelectedSecret(secret)}
            onRowContextMenu={handleRowContextMenu}
            columns={columns}
        />
    );
}
