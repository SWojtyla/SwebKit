import { useCallback, useMemo, type MouseEvent } from "react";
import { useAksSecrets } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksActions, useAksNav } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { SecretInfo } from "@/lib/types";

interface SecretsTabProps {
  ns: string;
  isMulti?: boolean;
}

// Cells only read from their row param, so this can be a stable module-level
// constant instead of being rebuilt (and defeating ResourceTable's memo) on
// every render.
const columns: Column<SecretInfo>[] = [
  { header: "Type", cell: (secret) => <span className="text-muted-foreground">{secret.type}</span> },
  { header: "Keys", cell: (secret) => (
    <span className="text-xs text-muted-foreground">
      {secret.keys.length > 0 ? secret.keys.join(", ") : "—"}
    </span>
  )},
];

export function SecretsTab({ ns, isMulti }: SecretsTabProps) {
  const { data: secrets, isLoading, error } = useAksSecrets(ns);
  const nav = useAksNav();
  const actions = useAksActions();
  const ws = useMemo(() => ({ ...nav, ...actions }), [nav, actions]);

  const buildMenu = useCallback((secret: SecretInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(secret.name) },
    { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("secret", secret.name, secret.namespace) },
    { label: "View keys", icon: "🔑", onClick: () => ws.setSelectedSecret(secret) },
  ], [ws]);

  const handleRowContextMenu = useCallback(
    (e: MouseEvent<HTMLTableRowElement>, secret: SecretInfo) => ws.showContextMenu(e, buildMenu(secret)),
    [ws, buildMenu],
  );

  return (
    <ResourceTable
      data={secrets}
      isLoading={isLoading}
      error={error}
      isMulti={isMulti}
      testIdPrefix="secret"
      tableBodyTestId="secrets-table-body"
      emptyMessage="No secrets found"
      onRowClick={(secret) => ws.setSelectedSecret(secret)}
      onRowContextMenu={handleRowContextMenu}
      columns={columns}
    />
  );
}
