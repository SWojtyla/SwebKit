import { useState } from "react";
import { Search } from "lucide-react";
import { useProfile, useUpdateProfile } from "@/lib/hooks";
import { useSqlDiscovery, useSqlTestConnection } from "@/lib/hooks/useSql";
import { useNotification } from "@/components/layout/NotificationSystem";
import type { SqlConnectionEntry } from "@/lib/types";
import { DraftInput } from "./DraftInput";
import { ConfirmBar } from "@/components/shared/ConfirmBar";

/** A connection is worth confirming removal of once it has a real server — an untouched
 * "New Connection" placeholder can go without the extra click (same rule RedisSettings uses). */
function isConfigured(conn: SqlConnectionEntry): boolean {
  return conn.server.trim() !== "";
}

export function SqlSettings() {
  const { data: profile } = useProfile();
  const updateProfile = useUpdateProfile();
  const { notify } = useNotification();
  const [pendingRemoveId, setPendingRemoveId] = useState<string | null>(null);
  const [discoverOpen, setDiscoverOpen] = useState(false);
  const discovery = useSqlDiscovery({ enabled: discoverOpen });

  if (!profile) return null;

  const sql = profile.config.sqlConfig ?? { connections: [], activeConnectionId: null };

  const update = (patch: Partial<typeof sql>) => {
    updateProfile.mutate((prev) => ({
      ...prev,
      config: {
        ...prev.config,
        sqlConfig: { ...(prev.config.sqlConfig ?? sql), ...patch },
      },
    }));
  };

  const addConnection = (seed?: Partial<SqlConnectionEntry>) => {
    const entry: SqlConnectionEntry = {
      id: crypto.randomUUID().slice(0, 8),
      displayName: "New Connection",
      server: "",
      database: "",
      allowWrites: false,
      active: true,
      ...seed,
    };
    update({
      connections: [...sql.connections, entry],
      activeConnectionId: sql.activeConnectionId ?? entry.id,
    });
  };

  const removeConnection = (id: string) => {
    const connections = sql.connections.filter((c) => c.id !== id);
    update({
      connections,
      activeConnectionId: sql.activeConnectionId === id ? connections[0]?.id ?? null : sql.activeConnectionId,
    });
  };

  const requestRemove = (conn: SqlConnectionEntry) => {
    if (isConfigured(conn)) setPendingRemoveId(conn.id);
    else removeConnection(conn.id);
  };

  const updateConnection = (id: string, patch: Partial<SqlConnectionEntry>) => {
    update({ connections: sql.connections.map((c) => (c.id === id ? { ...c, ...patch } : c)) });
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold">SQL Connections</h2>
        <div className="flex gap-2">
          <button
            onClick={() => setDiscoverOpen((v) => !v)}
            className="flex items-center gap-1 rounded-md border px-3 py-1.5 text-sm hover:bg-accent"
            data-testid="sql-discover-toggle"
            aria-expanded={discoverOpen}
          >
            <Search className="h-3.5 w-3.5" />
            {discoverOpen ? "Hide discovery" : "Discover in Azure"}
          </button>
          <button
            onClick={() => addConnection()}
            className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
            data-testid="sql-add-connection"
          >
            Add Connection
          </button>
        </div>
      </div>

      <p className="text-xs text-muted-foreground">
        Entra ID only — connections authenticate with your signed-in Azure identity
        (the same credential Service Bus and Storage use). No passwords are stored.
      </p>

      {discoverOpen && (
        <div className="rounded-lg border p-3" data-testid="sql-discovery-panel">
          {discovery.isLoading && <p className="text-xs text-muted-foreground">Scanning subscriptions…</p>}
          {discovery.isError && (
            <p className="text-xs text-destructive" data-testid="sql-discovery-error">
              {discovery.error instanceof Error ? discovery.error.message : String(discovery.error)}
            </p>
          )}
          {discovery.data && discovery.data.length === 0 && (
            <p className="text-xs text-muted-foreground" data-testid="sql-discovery-empty">
              No SQL servers found in your accessible subscriptions.
            </p>
          )}
          <ul className="space-y-1">
            {(discovery.data ?? []).map((server) => (
              <li key={`${server.subscriptionId}/${server.resourceGroup}/${server.name}`} className="rounded border p-2 text-xs" data-testid={`sql-discovered-${server.name}`}>
                <div className="flex items-center justify-between">
                  <div>
                    <span className="font-medium">{server.serverFqdn}</span>
                    <span className="ml-2 text-muted-foreground">
                      {server.resourceGroup} · {server.subscriptionName}
                    </span>
                  </div>
                  <button
                    onClick={() => {
                      addConnection({
                        displayName: server.name,
                        server: server.serverFqdn,
                        database: server.databases[0] ?? "",
                      });
                      notify("success", "Connection added", `${server.name} — review and save below.`);
                    }}
                    className="rounded border px-2 py-0.5 hover:bg-accent"
                    data-testid={`sql-discovered-add-${server.name}`}
                  >
                    Add
                  </button>
                </div>
                {server.databases.length > 0 && (
                  <p className="mt-1 text-muted-foreground">
                    Databases: {server.databases.join(", ")}
                  </p>
                )}
              </li>
            ))}
          </ul>
        </div>
      )}

      {sql.connections.map((conn) => (
        <ConnectionRow
          key={conn.id}
          connection={conn}
          onUpdate={(patch) => updateConnection(conn.id, patch)}
          onRequestRemove={() => requestRemove(conn)}
          pendingRemove={pendingRemoveId === conn.id}
          onConfirmRemove={() => {
            removeConnection(conn.id);
            setPendingRemoveId(null);
          }}
          onCancelRemove={() => setPendingRemoveId(null)}
        />
      ))}
    </div>
  );
}

interface ConnectionRowProps {
  connection: SqlConnectionEntry;
  onUpdate: (patch: Partial<SqlConnectionEntry>) => void;
  onRequestRemove: () => void;
  pendingRemove: boolean;
  onConfirmRemove: () => void;
  onCancelRemove: () => void;
}

function ConnectionRow({
  connection,
  onUpdate,
  onRequestRemove,
  pendingRemove,
  onConfirmRemove,
  onCancelRemove,
}: ConnectionRowProps) {
  const test = useSqlTestConnection(connection.id, { enabled: false });

  return (
    <div className="space-y-3 rounded-lg border p-4" data-testid={`sql-connection-${connection.id}`}>
      <div className="flex items-center justify-between">
        <DraftInput
          type="text"
          value={connection.displayName}
          onCommit={(v) => onUpdate({ displayName: v })}
          className="flex-1 rounded-md border bg-card px-3 py-1.5 text-sm"
          placeholder="Display name"
        />
        <button
          onClick={onRequestRemove}
          className="ml-2 text-sm text-destructive hover:opacity-80"
          data-testid={`sql-remove-${connection.id}`}
        >
          Remove
        </button>
      </div>

      <div>
        <DraftInput
          type="text"
          value={connection.server}
          onCommit={(v) => onUpdate({ server: v })}
          className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
          placeholder="myserver.database.windows.net"
          data-testid={`sql-server-${connection.id}`}
        />
        <p className="mt-1 text-xs text-muted-foreground">
          Server FQDN or hostname — e.g. <code>myserver.database.windows.net</code> or{" "}
          <code>localhost\SQLEXPRESS</code>.
        </p>
      </div>

      <div>
        <DraftInput
          type="text"
          value={connection.database}
          onCommit={(v) => onUpdate({ database: v })}
          className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
          placeholder="Database (optional — pick per-session on the SQL page)"
          data-testid={`sql-database-${connection.id}`}
        />
      </div>

      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={connection.allowWrites}
          onChange={(e) => onUpdate({ allowWrites: e.target.checked })}
          data-testid={`sql-allow-writes-${connection.id}`}
        />
        Allow writes
        <span className="text-xs text-muted-foreground">
          — off means the editor and the AI agent can only run read-only statements.
        </span>
      </label>

      <div className="flex items-center gap-2 pt-1">
        <button
          onClick={() => test.refetch()}
          disabled={test.isFetching}
          className="rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
          data-testid={`sql-test-connection-${connection.id}`}
        >
          {test.isFetching ? "Testing…" : "Test connection"}
        </button>
        {test.data && (
          <span
            className={`text-xs ${test.data.connected ? "text-success" : "text-destructive"}`}
            data-testid={`sql-test-result-${connection.id}`}
          >
            {test.data.connected ? "Connected" : `Failed: ${test.data.error ?? "unknown error"}`}
          </span>
        )}
        {test.isError && <span className="text-xs text-destructive">{String(test.error)}</span>}
      </div>

      {pendingRemove && (
        <ConfirmBar
          message={`Remove "${connection.displayName}"? This deletes its configuration from your profile — the database itself is unaffected.`}
          confirmLabel="Remove"
          onConfirm={onConfirmRemove}
          onCancel={onCancelRemove}
          testId={`sql-remove-confirm-${connection.id}`}
        />
      )}
    </div>
  );
}
