import { useMemo, useState } from "react";
import { Play } from "lucide-react";
import { useNavigate, useSearchParams } from "react-router";
import {
    useProfile,
    useSqlDatabases,
    useSqlSchema,
    useRunSqlQuery,
    useUpdateProfile,
} from "@/lib/hooks";
import type { SqlQueryResult } from "@/lib/types";
import { SchemaTree } from "./SchemaTree";
import { SqlEditor } from "./SqlEditor";
import { ResultsGrid } from "./ResultsGrid";
import { BrowsePanel } from "./BrowsePanel";
import { SavedQueriesPanel } from "./SavedQueriesPanel";
import { ComparePanel } from "./ComparePanel";

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

    const handleConnectionChange = (id: string) => {
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
                <select
                    value={resolvedConnectionId}
                    onChange={(e) => handleConnectionChange(e.target.value)}
                    className="rounded-md border bg-card px-3 py-1.5 text-sm"
                    data-testid="sql-connection-select"
                >
                    {connections.map((c) => (
                        <option key={c.id} value={c.id}>
                            {c.displayName}
                        </option>
                    ))}
                </select>
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
                                    className="flex items-center gap-1 rounded bg-primary px-3 py-1.5 text-sm text-primary-foreground disabled:opacity-50"
                                    data-testid="sql-run-query"
                                >
                                    <Play className="h-3.5 w-3.5" />
                                    {runQuery.isPending
                                        ? "Running…"
                                        : "Run (Ctrl+Enter)"}
                                </button>
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
