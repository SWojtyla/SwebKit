import { useAksSecrets } from "@/lib/hooks";
import { ResourceTable } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { SecretInfo } from "@/lib/types";

interface SecretsTabProps {
  ns: string;
  isMulti?: boolean;
}

export function SecretsTab({ ns, isMulti }: SecretsTabProps) {
  const { data: secrets, isLoading } = useAksSecrets(ns);
  const ws = useAksWorkspace();

  const buildMenu = (secret: SecretInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(secret.name) },
    { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("secret", secret.name, secret.namespace) },
    { label: "View keys", icon: "🔑", onClick: () => ws.setSelectedSecret(secret) },
  ];

  return (
    <ResourceTable
      data={secrets}
      isLoading={isLoading}
      isMulti={isMulti}
      testIdPrefix="secret"
      tableBodyTestId="secrets-table-body"
      emptyMessage="No secrets found"
      onRowContextMenu={(e, secret) => ws.showContextMenu(e, buildMenu(secret))}
      columns={[
        { header: "Type", cell: (secret) => <span className="text-muted-foreground">{secret.type}</span> },
        { header: "Keys", cell: (secret) => (
          <span className="text-xs text-muted-foreground">
            {secret.keys.length > 0 ? secret.keys.join(", ") : "—"}
          </span>
        )},
      ]}
    />
  );
}
