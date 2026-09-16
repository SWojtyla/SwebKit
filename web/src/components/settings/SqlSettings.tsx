import { useMemo, useState } from "react";
import { ChevronDown, ChevronRight, ListFilter, Plus, Search } from "lucide-react";
import { useProfile, useUpdateProfile } from "@/lib/hooks";
import { useSqlAdHocTest, useSqlBrowseDatabases, useSqlDiscovery } from "@/lib/hooks/useSql";
import { useNotification } from "@/components/layout/NotificationSystem";
import type { SqlConnectionEntry, SqlDatabaseInfo } from "@/lib/types";
import { DraftInput } from "./DraftInput";
import { ConfirmBar } from "@/components/shared/ConfirmBar";

/** A connection is worth confirming removal of once it has a real server — an untouched
 * "New Connection" placeholder can go without the extra click (same rule RedisSettings uses). */
function isConfigured(conn: SqlConnectionEntry): boolean {
  return conn.server.trim() !== "";
}

/** Grouping key for the collapsible server headers — connections are per-database entries,
 * so several rows can share one server. Entries still being typed group under "(no server)". */
function serverGroupKey(conn: SqlConnectionEntry): string {
  return conn.server.trim().toLowerCase() || "";
}

export function SqlSettings() {
  const { data: profile } = useProfile();
  const updateProfile = useUpdateProfile();
  const { notify } = useNotification();
  const [pendingRemoveId, setPendingRemoveId] = useState<string | null>(null);
  const [discoverOpen, setDiscoverOpen] = useState(false);
  const [filter, setFilter] = useState("");
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({});
  const discovery = useSqlDiscovery({ enabled: discoverOpen });

  const sql = profile?.config.sqlConfig ?? { connections: [], activeConnectionId: null };

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

  const filtered = useMemo(() => {
    const term = filter.trim().toLowerCase();
    if (!term) return sql.connections;
    return sql.connections.filter(
      (c) =>
        c.displayName.toLowerCase().includes(term) ||
        c.server.toLowerCase().includes(term) ||
        c.database.toLowerCase().includes(term),
    );
  }, [sql.connections, filter]);

  const groups = useMemo(() => {
    const map = new Map<string, SqlConnectionEntry[]>();
    for (const c of filtered) {
      const key = serverGroupKey(c);
      map.set(key, [...(map.get(key) ?? []), c]);
    }
    return [...map.entries()].sort((a, b) => a[0].localeCompare(b[0]));
  }, [filtered]);

  if (!profile) return null;

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
        One entry per database: the SQL page lists databases grouped under their server.
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
                    Add server
                  </button>
                </div>
                {server.databases.length > 0 && (
                  <ul className="mt-1 space-y-0.5" data-testid={`sql-discovered-dbs-${server.name}`}>
                    {server.databases.map((db) => (
                      <li key={db} className="flex items-center justify-between pl-2">
                        <span className="text-muted-foreground">{db}</span>
                        <button
                          onClick={() => {
                            addConnection({
                              displayName: db,
                              server: server.serverFqdn,
                              database: db,
                            });
                            notify("success", "Connection added", `${db} on ${server.name} — review and save below.`);
                          }}
                          className="rounded border px-2 py-0.5 hover:bg-accent"
                          data-testid={`sql-discovered-add-${server.name}-${db}`}
                        >
                          Add
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
              </li>
            ))}
          </ul>
        </div>
      )}

      {sql.connections.length > 5 && (
        <div className="flex items-center gap-2">
          <ListFilter className="h-3.5 w-3.5 text-muted-foreground" />
          <input
            type="text"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="Filter by name, server or database…"
            className="w-72 rounded-md border bg-card px-3 py-1.5 text-sm"
            data-testid="sql-connections-filter"
          />
        </div>
      )}

      {groups.map(([serverKey, conns]) => {
        const isCollapsed = collapsed[serverKey] ?? false;
        return (
          <div key={serverKey || "(none)"} className="space-y-2" data-testid={`sql-server-group-${serverKey || "none"}`}>
            <div className="flex items-center gap-2">
              <button
                onClick={() => setCollapsed((prev) => ({ ...prev, [serverKey]: !isCollapsed }))}
                className="flex items-center gap-1 text-sm font-medium hover:text-primary"
                aria-expanded={!isCollapsed}
                data-testid={`sql-server-toggle-${serverKey || "none"}`}
              >
                {isCollapsed ? <ChevronRight className="h-3.5 w-3.5" /> : <ChevronDown className="h-3.5 w-3.5" />}
                {serverKey || "No server set"}
                <span className="text-xs font-normal text-muted-foreground">
                  ({conns.length} {conns.length === 1 ? "entry" : "entries"})
                </span>
              </button>
              {serverKey && (
                <button
                  onClick={() =>
                    addConnection({
                      displayName: "New Connection",
                      server: conns[0].server,
                    })
                  }
                  className="flex items-center gap-1 rounded border px-2 py-0.5 text-xs hover:bg-accent"
                  data-testid={`sql-server-add-db-${serverKey}`}
                >
                  <Plus className="h-3 w-3" /> add database
                </button>
              )}
            </div>
            {!isCollapsed &&
              conns.map((conn) => (
                <ConnectionRow
                  key={conn.id}
                  connection={conn}
                  onUpdate={(patch) => updateConnection(conn.id, patch)}
                  onRequestRemove={() => requestRemove(conn)}
                  onAddDatabase={(db) =>
                    addConnection({
                      displayName: db,
                      server: conn.server,
                      database: db,
                    })
                  }
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
      })}

      {filter.trim() && filtered.length === 0 && (
        <p className="text-xs text-muted-foreground" data-testid="sql-connections-filter-empty">
          No connections match "{filter}".
        </p>
      )}
    </div>
  );
}

interface ConnectionRowProps {
  connection: SqlConnectionEntry;
  onUpdate: (patch: Partial<SqlConnectionEntry>) => void;
  onRequestRemove: () => void;
  onAddDatabase: (database: string) => void;
  pendingRemove: boolean;
  onConfirmRemove: () => void;
  onCancelRemove: () => void;
}

function ConnectionRow({
  connection,
  onUpdate,
  onRequestRemove,
  onAddDatabase,
  pendingRemove,
  onConfirmRemove,
  onCancelRemove,
}: ConnectionRowProps) {
  // Ad-hoc test/browse hit POST /api/sql/* with the current form values — the pooled client a
  // saved-entry test would hit can lag an uncommitted edit, and unsaved rows have no id at all.
  const test = useSqlAdHocTest();
  const browse = useSqlBrowseDatabases();
  const [formValues, setFormValues] = useState({ server: connection.server, database: connection.database });
  const [browseOpen, setBrowseOpen] = useState(false);

  const browsedDatabases: SqlDatabaseInfo[] = Array.isArray(browse.data) ? browse.data : [];
  const browseError = browse.data && !Array.isArray(browse.data) ? browse.data.error : null;

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
          onDraftChange={(server) => setFormValues((current) => ({ ...current, server }))}
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
          onDraftChange={(database) => setFormValues((current) => ({ ...current, database }))}
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
          onClick={() =>
            test.mutate(formValues)
          }
          disabled={test.isPending || !formValues.server.trim()}
          className="rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
          data-testid={`sql-test-connection-${connection.id}`}
        >
          {test.isPending ? "Testing…" : "Test connection"}
        </button>
        <button
          onClick={() => {
            setBrowseOpen(true);
            browse.mutate(formValues);
          }}
          disabled={browse.isPending || !formValues.server.trim()}
          className="rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
          data-testid={`sql-browse-databases-${connection.id}`}
        >
          {browse.isPending ? "Browsing…" : "Browse databases"}
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

      {browseOpen && (
        <div className="rounded border p-2 text-xs" data-testid={`sql-browse-panel-${connection.id}`}>
          <div className="mb-1 flex items-center justify-between">
            <span className="font-medium">Databases on {formValues.server}</span>
            <button
              onClick={() => setBrowseOpen(false)}
              className="text-muted-foreground hover:text-foreground"
              data-testid={`sql-browse-close-${connection.id}`}
            >
              Close
            </button>
          </div>
          {browse.isPending && <p className="text-muted-foreground">Listing databases…</p>}
          {browseError && (
            <p className="text-destructive" data-testid={`sql-browse-error-${connection.id}`}>
              {browseError}
            </p>
          )}
          {browse.isError && (
            <p className="text-destructive" data-testid={`sql-browse-error-${connection.id}`}>
              {String(browse.error)}
            </p>
          )}
          {browsedDatabases.length === 0 && browse.data && Array.isArray(browse.data) && (
            <p className="text-muted-foreground">No databases found on this server.</p>
          )}
          <ul className="space-y-0.5">
            {browsedDatabases.map((db) => (
              <li key={db.name} className="flex items-center justify-between">
                <span>
                  {db.name}
                  <span className="ml-1 text-muted-foreground">({db.state.toLowerCase()})</span>
                </span>
                <button
                  onClick={() => onAddDatabase(db.name)}
                  className="rounded border px-2 py-0.5 hover:bg-accent"
                  data-testid={`sql-browse-add-${connection.id}-${db.name}`}
                >
                  Add
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}

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
