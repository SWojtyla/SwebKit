import { useEffect, useRef, useState } from "react";
import { Plus, Search, X } from "lucide-react";
import { useProfile, useUpdateProfile } from "@/lib/hooks";
import {
    useSqlAdHocTest,
    useSqlBrowseDatabases,
    useSqlDiscovery,
} from "@/lib/hooks/useSql";
import { useNotification } from "@/components/layout/notification-context";
import type { SqlConnectionEntry, SqlDatabaseInfo } from "@/lib/types";
import { parseDeclaredObjectsText } from "@/lib/sql-declared";
import { DraftInput } from "./DraftInput";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { ProfileListLayout } from "./ProfileListLayout";

/** A connection is worth confirming removal of once it has a real server — an untouched
 * "New Connection" placeholder can go without the extra click (same rule RedisSettings uses). */
function isConfigured(conn: SqlConnectionEntry): boolean {
    // server is typed string but can arrive null from persisted profiles.json
    return (conn.server ?? "").trim() !== "";
}

/** Grouping key for the collapsible server headers — connections are per-database entries,
 * so several rows can share one server. Entries still being typed group under "(no server)". */
function serverGroupKey(conn: SqlConnectionEntry): string {
    return (conn.server ?? "").trim().toLowerCase();
}

export function SqlSettings() {
    const { data: profile } = useProfile();
    const updateProfile = useUpdateProfile();
    const { notify } = useNotification();
    const [pendingRemoveId, setPendingRemoveId] = useState<string | null>(null);
    const [discoverOpen, setDiscoverOpen] = useState(false);
    const discovery = useSqlDiscovery({ enabled: discoverOpen });

    const sql = profile?.config.sqlConfig ?? {
        connections: [],
        activeConnectionId: null,
    };

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
            activeConnectionId:
                sql.activeConnectionId === id
                    ? (connections[0]?.id ?? null)
                    : sql.activeConnectionId,
        });
    };

    const requestRemove = (conn: SqlConnectionEntry) => {
        if (isConfigured(conn)) setPendingRemoveId(conn.id);
        else removeConnection(conn.id);
    };

    const updateConnection = (
        id: string,
        patch: Partial<SqlConnectionEntry>,
    ) => {
        update({
            connections: sql.connections.map((c) =>
                c.id === id ? { ...c, ...patch } : c,
            ),
        });
    };

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
                Entra ID only — connections authenticate with your signed-in
                Azure identity (the same credential Service Bus and Storage
                use). No passwords are stored. One entry per database: the SQL
                page lists databases grouped under their server.
            </p>

            {discoverOpen && (
                <div
                    className="rounded-lg border p-3"
                    data-testid="sql-discovery-panel"
                >
                    {discovery.isLoading && (
                        <p className="text-xs text-muted-foreground">
                            Scanning subscriptions…
                        </p>
                    )}
                    {discovery.isError && (
                        <p
                            className="text-xs text-destructive"
                            data-testid="sql-discovery-error"
                        >
                            {discovery.error instanceof Error
                                ? discovery.error.message
                                : String(discovery.error)}
                        </p>
                    )}
                    {discovery.data && discovery.data.length === 0 && (
                        <p
                            className="text-xs text-muted-foreground"
                            data-testid="sql-discovery-empty"
                        >
                            No SQL servers found in your accessible
                            subscriptions.
                        </p>
                    )}
                    <ul className="space-y-1">
                        {(discovery.data ?? []).map((server) => (
                            <li
                                key={`${server.subscriptionId}/${server.resourceGroup}/${server.name}`}
                                className="rounded border p-2 text-xs"
                                data-testid={`sql-discovered-${server.name}`}
                            >
                                <div className="flex items-center justify-between">
                                    <div>
                                        <span className="font-medium">
                                            {server.serverFqdn}
                                        </span>
                                        <span className="ml-2 text-muted-foreground">
                                            {server.resourceGroup} ·{" "}
                                            {server.subscriptionName}
                                        </span>
                                    </div>
                                    <button
                                        onClick={() => {
                                            addConnection({
                                                displayName: server.name,
                                                server: server.serverFqdn,
                                                database:
                                                    server.databases[0] ?? "",
                                                // ARM discovery knows exactly which
                                                // resource this is — carry the scope
                                                // onto the saved entry so access
                                                // requests can reference it.
                                                subscriptionId:
                                                    server.subscriptionId,
                                                resourceGroup:
                                                    server.resourceGroup,
                                                resourceId: server.resourceId,
                                            });
                                            notify(
                                                "success",
                                                "Connection added",
                                                `${server.name} — review and save below.`,
                                            );
                                        }}
                                        className="rounded border px-2 py-0.5 hover:bg-accent"
                                        data-testid={`sql-discovered-add-${server.name}`}
                                    >
                                        Add server
                                    </button>
                                </div>
                                {server.databases.length > 0 && (
                                    <ul
                                        className="mt-1 space-y-0.5"
                                        data-testid={`sql-discovered-dbs-${server.name}`}
                                    >
                                        {server.databases.map((db) => (
                                            <li
                                                key={db}
                                                className="flex items-center justify-between pl-2"
                                            >
                                                <span className="text-muted-foreground">
                                                    {db}
                                                </span>
                                                <button
                                                    onClick={() => {
                                                        addConnection({
                                                            displayName: db,
                                                            server: server.serverFqdn,
                                                            database: db,
                                                            subscriptionId:
                                                                server.subscriptionId,
                                                            resourceGroup:
                                                                server.resourceGroup,
                                                            resourceId:
                                                                server.resourceId,
                                                        });
                                                        notify(
                                                            "success",
                                                            "Connection added",
                                                            `${db} on ${server.name} — review and save below.`,
                                                        );
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

            <ProfileListLayout
                items={sql.connections}
                getKey={(c) => c.id}
                getTitle={(c) => c.displayName}
                getSubtitle={(c) =>
                    c.database ? `${c.server} / ${c.database}` : c.server
                }
                getGroup={(c) => serverGroupKey(c)}
                isActive={(c) => c.id === sql.activeConnectionId}
                getFilterText={(c) => c.database}
                testIdPrefix="sql"
                emptyMessage={
                    <>
                        No SQL connections configured. Click "Add Connection" or
                        "Discover in Azure" to create one.
                    </>
                }
                renderGroupAction={(serverKey) =>
                    serverKey ? (
                        <button
                            onClick={() => {
                                const seed = sql.connections.find(
                                    (c) => serverGroupKey(c) === serverKey,
                                );
                                addConnection({
                                    displayName: "New Connection",
                                    server: seed?.server ?? "",
                                });
                            }}
                            className="flex items-center gap-1 rounded border px-2 py-0.5 text-xs hover:bg-accent"
                            data-testid={`sql-server-add-db-${serverKey}`}
                        >
                            <Plus className="h-3 w-3" /> add database
                        </button>
                    ) : null
                }
                renderEditor={(conn) => (
                    <ConnectionRow
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
                )}
            />
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
    const [formValues, setFormValues] = useState({
        server: connection.server ?? "",
        database: connection.database ?? "",
    });
    const [browseOpen, setBrowseOpen] = useState(false);

    const browsedDatabases: SqlDatabaseInfo[] = Array.isArray(browse.data)
        ? browse.data
        : [];
    const browseError =
        browse.data && !Array.isArray(browse.data) ? browse.data.error : null;

    return (
        <div
            className="space-y-3 rounded-lg border p-4"
            data-testid={`sql-connection-${connection.id}`}
        >
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
                    onDraftChange={(server) =>
                        setFormValues((current) => ({ ...current, server }))
                    }
                    className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                    placeholder="myserver.database.windows.net"
                    data-testid={`sql-server-${connection.id}`}
                />
                <p className="mt-1 text-xs text-muted-foreground">
                    Server FQDN or hostname — e.g.{" "}
                    <code>myserver.database.windows.net</code> or{" "}
                    <code>localhost\SQLEXPRESS</code>.
                </p>
            </div>

            <div>
                <DraftInput
                    type="text"
                    value={connection.database}
                    onCommit={(v) => onUpdate({ database: v })}
                    onDraftChange={(database) =>
                        setFormValues((current) => ({ ...current, database }))
                    }
                    className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                    placeholder="Database (optional — pick per-session on the SQL page)"
                    data-testid={`sql-database-${connection.id}`}
                />
            </div>

            <div>
                <DraftInput
                    type="text"
                    value={connection.resourceId ?? ""}
                    onCommit={(v) => onUpdate({ resourceId: v || null })}
                    className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                    placeholder="Azure resource ID (optional)"
                    data-testid={`sql-resource-id-${connection.id}`}
                />
                <p className="mt-1 text-xs text-muted-foreground">
                    Filled automatically when you add a server from Azure
                    discovery — the access report uses it to scope access
                    requests. Leave empty if unknown; it's never guessed from
                    the hostname.
                </p>
            </div>

            <DeclaredObjectsEditor
                connectionId={connection.id}
                value={connection.declaredObjects ?? []}
                onCommit={(entries) => onUpdate({ declaredObjects: entries })}
            />

            <label className="flex items-center gap-2 text-sm">
                <input
                    type="checkbox"
                    checked={connection.allowWrites}
                    onChange={(e) =>
                        onUpdate({ allowWrites: e.target.checked })
                    }
                    data-testid={`sql-allow-writes-${connection.id}`}
                />
                Allow writes
                <span className="text-xs text-muted-foreground">
                    — off means the editor and the AI agent can only run
                    read-only statements.
                </span>
            </label>

            <div className="flex items-center gap-2 pt-1">
                <button
                    onClick={() => test.mutate(formValues)}
                    disabled={test.isPending || !formValues.server.trim()}
                    title={
                        test.isPending
                            ? "Testing…"
                            : !formValues.server.trim()
                              ? "Enter a server first"
                              : undefined
                    }
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
                    title={
                        browse.isPending
                            ? "Loading…"
                            : !formValues.server.trim()
                              ? "Enter a server first"
                              : undefined
                    }
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
                        {test.data.connected
                            ? "Connected"
                            : `Failed: ${test.data.error ?? "unknown error"}`}
                    </span>
                )}
                {test.isError && (
                    <span className="text-xs text-destructive">
                        {String(test.error)}
                    </span>
                )}
            </div>

            {browseOpen && (
                <div
                    className="rounded border p-2 text-xs"
                    data-testid={`sql-browse-panel-${connection.id}`}
                >
                    <div className="mb-1 flex items-center justify-between">
                        <span className="font-medium">
                            Databases on {formValues.server}
                        </span>
                        <button
                            onClick={() => setBrowseOpen(false)}
                            className="text-muted-foreground hover:text-foreground"
                            data-testid={`sql-browse-close-${connection.id}`}
                        >
                            Close
                        </button>
                    </div>
                    {browse.isPending && (
                        <p className="text-muted-foreground">
                            Listing databases…
                        </p>
                    )}
                    {browseError && (
                        <p
                            className="text-destructive"
                            data-testid={`sql-browse-error-${connection.id}`}
                        >
                            {browseError}
                        </p>
                    )}
                    {browse.isError && (
                        <p
                            className="text-destructive"
                            data-testid={`sql-browse-error-${connection.id}`}
                        >
                            {String(browse.error)}
                        </p>
                    )}
                    {browsedDatabases.length === 0 &&
                        browse.data &&
                        Array.isArray(browse.data) && (
                            <p className="text-muted-foreground">
                                No databases found on this server.
                            </p>
                        )}
                    <ul className="space-y-0.5">
                        {browsedDatabases.map((db) => (
                            <li
                                key={db.name}
                                className="flex items-center justify-between"
                            >
                                <span>
                                    {db.name}
                                    <span className="ml-1 text-muted-foreground">
                                        ({db.state.toLowerCase()})
                                    </span>
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

/** Editor for the connection's declared objects — "schema.name" or "exec:schema.name",
 * one per line. A textarea (not per-line inputs) because pasting a list is the normal
 * way this fills in; commit is on blur/unmount like DraftInput since every write is a
 * full profile save. Invalid lines refuse to commit rather than silently dropping text
 * the user typed — the committed list below stays authoritative meanwhile. */
function DeclaredObjectsEditor({
    connectionId,
    value,
    onCommit,
}: {
    connectionId: string;
    value: string[];
    onCommit: (entries: string[]) => void;
}) {
    const testId = `sql-declared-objects-${connectionId}`;
    const [draft, setDraft] = useState(() => value.join("\n"));
    const [error, setError] = useState<string | null>(null);
    const committedRef = useRef(value.join("\n"));

    const draftRef = useRef(draft);
    const onCommitRef = useRef(onCommit);
    useEffect(() => {
        draftRef.current = draft;
        onCommitRef.current = onCommit;
    });

    // Same re-sync rule as DraftInput: reconcile whenever the stored value diverges from
    // what we last committed — a save echo or a normalized commit must never fight typing.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    useEffect(() => {
        const joined = value.join("\n");
        if (joined !== committedRef.current) {
            committedRef.current = joined;
            setDraft(joined);
            setError(null);
        }
    });

    // Collapsing the row or switching settings tabs unmounts without a blur — commit a
    // fully-valid draft then; a draft with invalid lines is left to die rather than
    // silently saving a truncated list.
    useEffect(() => {
        return () => {
            const { entries, invalid } = parseDeclaredObjectsText(
                draftRef.current,
            );
            if (
                invalid.length === 0 &&
                entries.join("\n") !== committedRef.current
            ) {
                onCommitRef.current(entries);
            }
        };
    }, []);

    const commit = () => {
        const { entries, invalid } = parseDeclaredObjectsText(draft);
        if (invalid.length > 0) {
            setError(
                `Not valid "schema.name" entries: ${invalid.join(", ")} — fix or remove these lines.`,
            );
            return;
        }
        setError(null);
        const canonical = entries.join("\n");
        if (canonical !== draft) {
            setDraft(canonical);
            draftRef.current = canonical;
        }
        if (canonical === committedRef.current) return;
        committedRef.current = canonical;
        onCommit(entries);
    };

    return (
        <div>
            <label htmlFor={testId} className="mb-1 block text-sm font-medium">
                Declared objects
            </label>
            <textarea
                id={testId}
                value={draft}
                onChange={(e) => setDraft(e.target.value)}
                onBlur={commit}
                rows={3}
                spellCheck={false}
                placeholder={"prd.v_orders\nexec:prd.p_recalc"}
                className="w-full rounded-md border bg-card px-3 py-1.5 font-mono text-xs"
                data-testid={testId}
            />
            {error && (
                <p
                    className="mt-1 text-xs text-destructive"
                    data-testid={`${testId}-error`}
                >
                    {error}
                </p>
            )}
            <p className="mt-1 text-xs text-muted-foreground">
                One <code>schema.name</code> per line (prefix a procedure with{" "}
                <code>exec:</code>). For connections where catalog browsing is
                denied, declared objects still appear in the schema tree —
                columns load lazily on expand, needing only SELECT on the
                object.
            </p>
            {value.length > 0 && (
                <div
                    className="mt-1 flex flex-wrap gap-1"
                    data-testid={`${testId}-list`}
                >
                    {value.map((entry) => (
                        <span
                            key={entry}
                            className="inline-flex items-center gap-1 rounded border px-1.5 py-0.5 font-mono text-[11px]"
                            data-testid={`${testId}-entry-${entry}`}
                        >
                            {entry}
                            <button
                                type="button"
                                onClick={() =>
                                    onCommit(value.filter((e) => e !== entry))
                                }
                                className="text-muted-foreground hover:text-destructive"
                                aria-label={`Remove ${entry}`}
                                data-testid={`${testId}-remove-${entry}`}
                            >
                                <X className="h-3 w-3" />
                            </button>
                        </span>
                    ))}
                </div>
            )}
        </div>
    );
}
