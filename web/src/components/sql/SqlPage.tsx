import { useEffect, useMemo, useState } from "react";
import { Play, Save } from "lucide-react";
import { useNavigate, useSearchParams } from "react-router";
import {
    useProfile,
    useSaveSqlQuery,
    useSqlDatabases,
    useSqlSchema,
    useRunSqlQuery,
    useUpdateProfile,
    useUpdateSearchParams,
} from "@/lib/hooks";
import type { SqlQueryResult } from "@/lib/types";
import { SearchableSelect } from "@/components/shared/SearchableSelect";
import { SchemaTree } from "./SchemaTree";
import { SqlEditor } from "./SqlEditor";
import { ResultsGrid } from "./ResultsGrid";
import { BrowsePanel } from "./BrowsePanel";
import { SavedQueriesPanel } from "./SavedQueriesPanel";
import { ComparePanel } from "./ComparePanel";
import { QueryBuilderPanel } from "./QueryBuilderPanel";

const tabs = [
    { id: "query", label: "Query" },
    { id: "browse", label: "Browse" },
    { id: "saved", label: "Saved & History" },
    { id: "compare", label: "Compare" },
] as const;
type TabId = (typeof tabs)[number]["id"];

export function SqlPage() {
    const { data: profile } = useProfile();
    const updateProfile = useUpdateProfile();
    const navigate = useNavigate();

    const connections = useMemo(
        () =>
            (profile?.config.sqlConfig?.connections ?? []).filter(
                (c) => c.active,
            ),
        [profile],
    );

    // Per-database entries group under their server — the picker shows databases,
    // not servers (db-less legacy entries keep their display name as the label).
    const connectionGroups = useMemo(() => {
        const map = new Map<string, typeof connections>();
        for (const c of connections) {
            const key = c.server.trim() || "No server";
            map.set(key, [...(map.get(key) ?? []), c]);
        }
        return [...map.entries()];
    }, [connections]);

    // Deep-link params `/sql?connection=&database=&table=schema.table` — consumed once on
    // mount (workspace-map "Open in SQL" and palette entries land here).
    const [searchParams] = useSearchParams();
    const deepLink = useMemo(() => {
        const table = searchParams.get("table");
        const dot = table?.indexOf(".") ?? -1;
        return {
            connection: searchParams.get("connection"),
            database: searchParams.get("database"),
            table:
                table && dot > 0
                    ? {
                          schema: table.slice(0, dot),
                          name: table.slice(dot + 1),
                      }
                    : null,
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps -- mount-only consumption.
    }, []);

    // The active connection follows the deep-link target, then the profile's persisted
    // choice, then the first configured one — same order SqlToolContext resolves.
    const resolvedConnectionId =
        connections.find((c) => c.id === deepLink.connection)?.id ??
        connections.find(
            (c) => c.id === profile?.config.sqlConfig?.activeConnectionId,
        )?.id ??
        connections[0]?.id ??
        null;
    const connection =
        connections.find((c) => c.id === resolvedConnectionId) ?? null;

    const [database, setDatabase] = useState<string | null>(
        () => deepLink.database,
    );
    const [activeTab, setActiveTab] = useState<TabId>(() =>
        deepLink.table ? "browse" : "query",
    );
    const [editorSql, setEditorSql] = useState("");
    const [selectedTable, setSelectedTable] = useState<{
        schema: string;
        name: string;
    } | null>(() => deepLink.table);
    const [result, setResult] = useState<SqlQueryResult | undefined>(undefined);
    const [saveOpen, setSaveOpen] = useState(false);
    const [saveName, setSaveName] = useState("");
    const [saveFolder, setSaveFolder] = useState("");
    const saveQuery = useSaveSqlQuery();

    // A connection switch resets the per-connection view state — the schema tree, the
    // browsed table and the database override all belong to the previous server. Adjusted
    // during render (not in an effect) so nothing ever paints the stale selection.
    const [prevConnectionId, setPrevConnectionId] =
        useState(resolvedConnectionId);
    if (prevConnectionId !== resolvedConnectionId) {
        setPrevConnectionId(resolvedConnectionId);
        setDatabase(null);
        setSelectedTable(null);
        setResult(undefined);
    }

    const databases = useSqlDatabases(resolvedConnectionId);
    const effectiveDatabase = database ?? connection?.database ?? null;
    const schema = useSqlSchema(resolvedConnectionId, effectiveDatabase);
    const runQuery = useRunSqlQuery(resolvedConnectionId);
    const updateParams = useUpdateSearchParams();

    // Settle ?connection on the resolved id — a bare visit becomes shareable, an
    // invalid deep-link id is corrected, and last-route restore records the real
    // connection rather than whatever the mount-time deep link happened to say.
    useEffect(() => {
        if (resolvedConnectionId && searchParams.get("connection") !== resolvedConnectionId) {
            updateParams({ connection: resolvedConnectionId }, { replace: true });
        }
    }, [resolvedConnectionId, searchParams, updateParams]);

    const handleConnectionChange = (id: string) => {
        updateParams({ connection: id });
        updateProfile.mutate((prev) => ({
            ...prev,
            config: {
                ...prev.config,
                sqlConfig: {
                    connections: prev.config.sqlConfig?.connections ?? [],
                    ...prev.config.sqlConfig,
                    activeConnectionId: id,
                },
            },
        }));
    };

    const run = (sql: string) => {
        if (!resolvedConnectionId || !sql.trim()) return;
        runQuery.mutate(
            { sql, database: effectiveDatabase },
            { onSuccess: (r) => setResult(r) },
        );
    };

    if (!resolvedConnectionId) {
        return (
            <div className="p-6" data-testid="sql-page">
                <h1 className="text-2xl font-bold" data-testid="sql-title">
                    SQL
                </h1>
                <p
                    className="mt-4 text-muted-foreground"
                    data-testid="sql-no-connection"
                >
                    No SQL connection configured. Add one in{" "}
                    <button
                        onClick={() =>
                            navigate("/settings", { state: { tab: "sql" } })
                        }
                        className="text-primary underline"
                        data-testid="sql-goto-settings"
                    >
                        Settings → SQL
                    </button>{" "}
                    or discover servers in Azure from there.
                </p>
            </div>
        );
    }

    return (
        <div className="flex h-full flex-col" data-testid="sql-page">
            {/* Connection bar */}
            <div className="flex flex-wrap items-center gap-3 border-b px-6 py-2">
                <h1
                    className="shrink-0 text-lg font-bold"
                    data-testid="sql-title"
                >
                    SQL
                </h1>
                <SearchableSelect
                    items={connectionGroups.flatMap(([server, conns]) =>
                        conns.map((c) => ({
                            value: c.id,
                            label: c.database || c.displayName,
                            subtitle: server,
                        })),
                    )}
                    value={resolvedConnectionId}
                    onChange={(item) => handleConnectionChange(item.value)}
                    placeholder="Select connection..."
                    filterPlaceholder="Filter connections..."
                    testId="sql-connection"
                    nativeSelectTestId="sql-connection-select"
                    listAriaLabel="SQL connections"
                    buttonClassName="min-w-[14rem]"
                />
                {databases.data && databases.data.length > 1 && (
                    <select
                        value={effectiveDatabase ?? ""}
                        onChange={(e) => setDatabase(e.target.value || null)}
                        className="rounded-md border bg-card px-2 py-1.5 text-sm"
                        data-testid="sql-database-select"
                        aria-label="Database"
                    >
                        {connection?.database &&
                            !databases.data.some(
                                (d) => d.name === connection.database,
                            ) && (
                                <option value={connection.database}>
                                    {connection.database}
                                </option>
                            )}
                        {databases.data.map((d) => (
                            <option key={d.name} value={d.name}>
                                {d.name}
                            </option>
                        ))}
                    </select>
                )}
                {connection && !connection.allowWrites && (
                    <span
                        className="rounded border border-warning/50 px-2 py-0.5 text-xs text-warning"
                        data-testid="sql-readonly-badge"
                        title="Mutating statements are rejected on this connection — enable writes in Settings → SQL."
                    >
                        Read-only
                    </span>
                )}
                {connection?.allowWrites && (
                    <span
                        className="rounded border border-destructive/50 px-2 py-0.5 text-xs text-destructive"
                        data-testid="sql-writes-badge"
                    >
                        Writes enabled
                    </span>
                )}
            </div>

            <div className="flex gap-1 border-b px-6" data-testid="sql-tabs">
                {tabs.map((tab) => (
                    <button
                        key={tab.id}
                        data-testid={`sql-tab-${tab.id}`}
                        onClick={() => setActiveTab(tab.id)}
                        className={`px-4 py-2 text-sm font-medium transition-colors ${
                            activeTab === tab.id
                                ? "border-b-2 border-primary text-primary"
                                : "text-muted-foreground hover:text-foreground"
                        }`}
                    >
                        {tab.label}
                    </button>
                ))}
            </div>

            <div className="flex flex-1 overflow-hidden">
                {/* Schema sidebar — always visible; clicking an object jumps to Browse. */}
                <aside className="w-60 shrink-0 overflow-hidden border-r">
                    <SchemaTree
                        schema={schema.data}
                        isLoading={schema.isLoading}
                        error={schema.error}
                        selected={selectedTable}
                        onSelect={(s, name) => {
                            setSelectedTable({ schema: s, name });
                            setActiveTab("browse");
                        }}
                    />
                </aside>

                <div className="flex min-w-0 flex-1 flex-col">
                    {activeTab === "query" && (
                        <div className="flex min-h-0 flex-1 flex-col p-3">
                            <QueryBuilderPanel schema={schema.data} onInsert={setEditorSql} />
                            <div className="flex h-40 flex-col">
                                <SqlEditor
                                    value={editorSql}
                                    onChange={setEditorSql}
                                    onRun={() => run(editorSql)}
                                    connectionId={resolvedConnectionId}
                                    schema={schema.data}
                                />
                            </div>
                            <div className="flex items-center gap-2 py-2">
                                <button
                                    onClick={() => run(editorSql)}
                                    disabled={
                                        !editorSql.trim() || runQuery.isPending
                                    }
                                    title={
                                        runQuery.isPending
                                            ? "Running…"
                                            : !editorSql.trim()
                                              ? "Write a query first"
                                              : undefined
                                    }
                                    className="flex items-center gap-1 rounded bg-primary px-3 py-1.5 text-sm text-primary-foreground disabled:opacity-50"
                                    data-testid="sql-run-query"
                                >
                                    <Play className="h-3.5 w-3.5" />
                                    {runQuery.isPending
                                        ? "Running…"
                                        : "Run (Ctrl+Enter)"}
                                </button>
                                <div className="relative">
                                    <button
                                        onClick={() => setSaveOpen((v) => !v)}
                                        disabled={!editorSql.trim()}
                                        title={!editorSql.trim() ? "Write a query first" : undefined}
                                        className="flex items-center gap-1 rounded border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
                                        data-testid="sql-save-query-open"
                                        aria-expanded={saveOpen}
                                    >
                                        <Save className="h-3.5 w-3.5" />
                                        Save
                                    </button>
                                    {saveOpen && (
                                        <div
                                            className="absolute left-0 top-full z-30 mt-1 w-64 space-y-2 rounded-md border bg-popover p-3 shadow-lg"
                                            data-testid="sql-save-popover"
                                        >
                                            <input
                                                type="text"
                                                value={saveName}
                                                onChange={(e) => setSaveName(e.target.value)}
                                                placeholder="Query name"
                                                className="w-full rounded border bg-card px-2 py-1 text-xs"
                                                data-testid="sql-save-popover-name"
                                                autoFocus
                                            />
                                            <input
                                                type="text"
                                                value={saveFolder}
                                                onChange={(e) => setSaveFolder(e.target.value)}
                                                placeholder="Folder (optional)"
                                                className="w-full rounded border bg-card px-2 py-1 text-xs"
                                                data-testid="sql-save-popover-folder"
                                            />
                                            <div className="flex justify-end gap-2">
                                                <button
                                                    onClick={() => setSaveOpen(false)}
                                                    className="rounded border px-2 py-1 text-xs hover:bg-accent"
                                                    data-testid="sql-save-popover-cancel"
                                                >
                                                    Cancel
                                                </button>
                                                <button
                                                    onClick={() =>
                                                        saveQuery.mutate(
                                                            {
                                                                name: saveName,
                                                                folder: saveFolder || null,
                                                                sql: editorSql,
                                                                connectionId: resolvedConnectionId,
                                                            },
                                                            {
                                                                onSuccess: () => {
                                                                    setSaveOpen(false);
                                                                    setSaveName("");
                                                                },
                                                            },
                                                        )
                                                    }
                                                    disabled={!saveName.trim() || saveQuery.isPending}
                                                    title={saveQuery.isPending ? "Saving…" : !saveName.trim() ? "Name the query first" : undefined}
                                                    className="rounded bg-primary px-2 py-1 text-xs text-primary-foreground disabled:opacity-50"
                                                    data-testid="sql-save-popover-submit"
                                                >
                                                    Save
                                                </button>
                                            </div>
                                        </div>
                                    )}
                                </div>
                                {runQuery.isError && (
                                    <span
                                        className="text-xs text-destructive"
                                        data-testid="sql-query-error"
                                    >
                                        {runQuery.error instanceof Error
                                            ? runQuery.error.message
                                            : String(runQuery.error)}
                                    </span>
                                )}
                            </div>
                            <ResultsGrid
                                result={result}
                                isRunning={runQuery.isPending}
                                testId="sql-query-results"
                            />
                        </div>
                    )}
                    {activeTab === "browse" && (
                        <BrowsePanel
                            connectionId={resolvedConnectionId}
                            database={effectiveDatabase}
                            schema={schema.data}
                            table={selectedTable}
                        />
                    )}
                    {activeTab === "saved" && (
                        <SavedQueriesPanel
                            connectionId={resolvedConnectionId}
                            currentSql={editorSql}
                            onLoad={(sql) => {
                                setEditorSql(sql);
                                setActiveTab("query");
                            }}
                            onRun={(sql) => {
                                setEditorSql(sql);
                                setActiveTab("query");
                                run(sql);
                            }}
                        />
                    )}
                    {activeTab === "compare" && (
                        <ComparePanel
                            connections={connections}
                            sourceId={resolvedConnectionId}
                            onSourceChange={handleConnectionChange}
                        />
                    )}
                </div>
            </div>
        </div>
    );
}
