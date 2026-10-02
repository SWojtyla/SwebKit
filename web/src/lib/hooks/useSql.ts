import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { describeApiError, apiFetch, apiSend } from "../api";
import { useNotification } from "@/components/layout/notification-context";
import { mergeObjectColumns } from "../sql-declared";
import type {
    ConnectionTestResult,
  SavedSqlQuery,
  SqlCompletionContext,
  SqlDataCompareResult,
  SqlDatabaseInfo,
  SqlDiscoveredServer,
  SqlHistoryEntry,
  SqlObjectColumnsResult,
  SqlQueryResult,
  SqlSchemaCompareResult,
  SqlSchemaModel,
} from "../types";

// ── SQL (sql-database-explorer) ───────────────────────────────────────────────
// Query keys are rooted at ["sql", connectionId, ...] so `invalidateQueries({
// queryKey: ["sql"] })` refreshes every SQL view after a profile/demo-mode change.

/** Mirrors `useRedisTestConnection` — only fires when "Test connection" is clicked
 * (`enabled: false` from the caller), not on render. Tests the saved profile entry. */
export function useSqlTestConnection(connectionId: string | null, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["sql", connectionId, "test"],
    queryFn: ({ signal }) => apiFetch<ConnectionTestResult>(`/api/sql/${connectionId}/test`, { signal }),
    enabled: !!connectionId && (options?.enabled ?? true),
    retry: false,
  });
}

/** Tests the values currently on the settings form — `POST /api/sql/test` builds an unpooled
 * client, so a row tests what it shows, not the last-saved (possibly stale) pooled client. */
export function useSqlAdHocTest() {
  return useMutation({
    mutationFn: (vars: { server: string; database?: string | null }) =>
      apiSend<ConnectionTestResult>("/api/sql/test", "POST", vars),
  });
}

/** Lists databases on a server the user typed but hasn't saved — browse-before-add in
 * settings. Ad-hoc endpoint; errors come back in the payload (`{connected:false,error}`). */
export function useSqlBrowseDatabases() {
  return useMutation({
    mutationFn: (vars: { server: string; database?: string | null }) =>
      apiSend<SqlDatabaseInfo[] | { connected: false; error?: string }>("/api/sql/databases", "POST", vars),
  });
}

export function useSqlDatabases(connectionId: string | null) {
  return useQuery({
    queryKey: ["sql", connectionId, "databases"],
    queryFn: ({ signal }) => apiFetch<SqlDatabaseInfo[]>(`/api/sql/${connectionId}/databases`, { signal }),
    enabled: !!connectionId,
  });
}

/** The browsable schema tree (schemas → tables/views → columns). */
export function useSqlSchema(connectionId: string | null, database: string | null) {
  return useQuery({
    queryKey: ["sql", connectionId, "schema", database ?? ""],
    queryFn: ({ signal }) => {
      const params = database ? `?database=${encodeURIComponent(database)}` : "";
      return apiFetch<SqlSchemaModel>(`/api/sql/${connectionId}/schema${params}`, { signal });
    },
    enabled: !!connectionId,
    // The schema tree is the autocomplete source — keep it warm between keystrokes.
    staleTime: 60_000,
  });
}

/** Executes a query — a mutation, not a query: results shouldn't be refetched or
 * cached under a key that a different statement could collide with. */
export function useRunSqlQuery(connectionId: string | null) {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: { sql: string; database?: string | null; maxRows?: number }) =>
      apiSend<SqlQueryResult>(`/api/sql/${connectionId}/query`, "POST", vars),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["sql", "history"] });
    },
    onError: (error) => notify("error", "Query failed", describeApiError(error)),
  });
}

export interface SqlTableRowsParams {
  database?: string | null;
  filterColumn?: string | null;
  filter?: string | null;
  orderBy?: string | null;
  desc?: boolean;
  skip?: number;
  take?: number;
}

export function useSqlTableRows(connectionId: string | null, schema: string | null, table: string | null, params: SqlTableRowsParams) {
  return useQuery({
    queryKey: ["sql", connectionId, "rows", schema, table, params],
    queryFn: ({ signal }) => {
      const qs = new URLSearchParams();
      if (params.database) qs.set("database", params.database);
      if (params.filterColumn && params.filter) {
        qs.set("filterColumn", params.filterColumn);
        qs.set("filter", params.filter);
      }
      if (params.orderBy) qs.set("orderBy", params.orderBy);
      if (params.desc) qs.set("desc", "true");
      qs.set("skip", String(params.skip ?? 0));
      qs.set("take", String(params.take ?? 100));
      return apiFetch<SqlQueryResult>(
        `/api/sql/${connectionId}/tables/${encodeURIComponent(schema!)}/${encodeURIComponent(table!)}/rows?${qs}`,
        { signal },
      );
    },
    enabled: !!connectionId && !!schema && !!table,
    placeholderData: (previousData, previousQuery) =>
      previousQuery?.queryKey[1] === connectionId && previousQuery?.queryKey[3] === schema && previousQuery?.queryKey[4] === table
        ? previousData
        : undefined,
  });
}

/** Lazy column introspection for one object (SELECT TOP 0 on the server) — fired when a
 * declared schema node expands. `enabled` is the expand flag: the query only runs while
 * the node is open and its columns aren't already known. On success the columns merge
 * into the schema query's cached model so the editor autocomplete and query builder see
 * them without a second fetch. Denials/errors arrive in the payload, not as a thrown
 * query error — one denied object must not fail the tree. */
export function useSqlObjectColumns(
  connectionId: string | null,
  database: string | null,
  schema: string | null,
  name: string | null,
  enabled = true,
) {
  const qc = useQueryClient();
  return useQuery({
    queryKey: ["sql", connectionId, "object-columns", database ?? "", schema ?? "", name ?? ""],
    queryFn: async ({ signal }) => {
      const qs = database ? `?database=${encodeURIComponent(database)}` : "";
      const result = await apiFetch<SqlObjectColumnsResult>(
        `/api/sql/${connectionId}/objects/${encodeURIComponent(schema!)}/${encodeURIComponent(name!)}/columns${qs}`,
        { signal },
      );
      if (!result.denied && !result.error && result.columns.length > 0) {
        qc.setQueryData<SqlSchemaModel>(
          ["sql", connectionId, "schema", database ?? ""],
          (prev) => mergeObjectColumns(prev, schema!, name!, result.columns),
        );
      }
      return result;
    },
    enabled: enabled && !!connectionId && !!schema && !!name,
    staleTime: 60_000,
    // A denied object is a payload, not a failure — but a transport failure shouldn't
    // storm retry the server either while a node sits expanded.
    retry: 1,
  });
}

// ── Saved queries & history ──────────────────────────────────────────────────

export function useSavedSqlQueries(
  connectionId: string | null,
  options?: { enabled?: boolean },
) {
  return useQuery({
    queryKey: ["sql", "queries", connectionId ?? "all"],
    queryFn: ({ signal }) => {
      const qs = connectionId ? `?connectionId=${encodeURIComponent(connectionId)}` : "";
      return apiFetch<SavedSqlQuery[]>(`/api/sql/queries${qs}`, { signal });
    },
    // The palette passes `null` + a lazy flag so opening it is the only thing
    // that pays for the cross-connection list.
    enabled: options?.enabled ?? true,
  });
}

export function useSaveSqlQuery() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: { name: string; folder?: string | null; sql: string; connectionId?: string | null }) =>
      apiSend<SavedSqlQuery>("/api/sql/queries", "POST", vars),
    onSuccess: (saved) => {
      qc.invalidateQueries({ queryKey: ["sql", "queries"] });
      notify("success", "Query saved", `"${saved.name}" is in your saved queries.`);
    },
    onError: (error) => notify("error", "Couldn't save query", describeApiError(error)),
  });
}

export function useDeleteSqlQuery() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (queryId: string) => apiSend<void>(`/api/sql/queries/${encodeURIComponent(queryId)}`, "DELETE"),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["sql", "queries"] });
      notify("success", "Query deleted");
    },
    onError: (error) => notify("error", "Couldn't delete query", describeApiError(error)),
  });
}

export function useSqlHistory(connectionId: string | null) {
  return useQuery({
    queryKey: ["sql", "history", connectionId ?? "all"],
    queryFn: ({ signal }) => {
      const qs = connectionId ? `?connectionId=${encodeURIComponent(connectionId)}` : "";
      return apiFetch<SqlHistoryEntry[]>(`/api/sql/history${qs}`, { signal });
    },
  });
}

export function useClearSqlHistory() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: () => apiSend<void>("/api/sql/history", "DELETE"),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["sql", "history"] });
      notify("success", "History cleared");
    },
    onError: (error) => notify("error", "Couldn't clear history", describeApiError(error)),
  });
}

// ── ARM discovery ─────────────────────────────────────────────────────────────

export function useSqlDiscovery(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["sql", "discover"],
    queryFn: ({ signal }) => apiFetch<SqlDiscoveredServer[]>("/api/sql/discover", { signal }),
    enabled: options?.enabled ?? true,
    retry: false,
    staleTime: 60_000,
  });
}

// ── Autocomplete context & compare ────────────────────────────────────────────

/** ScriptDom parse of the editor text — which table refs/aliases are in scope at the
 * cursor. Called on demand from the completion source, not on a timer. */
export async function fetchSqlCompletionContext(
  connectionId: string,
  sql: string,
  cursorOffset: number,
): Promise<SqlCompletionContext> {
  return apiSend<SqlCompletionContext>(`/api/sql/${connectionId}/completion-context`, "POST", { sql, cursorOffset });
}

export function useSqlDataCompare() {
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: {
      sourceConnectionId: string;
      targetConnectionId: string;
      schema: string;
      table: string;
      keyColumns: string[];
      sourceDatabase?: string | null;
      targetDatabase?: string | null;
      maxDiffRows?: number;
    }) => apiSend<SqlDataCompareResult>("/api/sql/compare/data", "POST", vars),
    onError: (error) => notify("error", "Data compare failed", describeApiError(error)),
  });
}

export function useSqlSchemaCompare() {
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: {
      sourceConnectionId: string;
      targetConnectionId: string;
      sourceDatabase?: string | null;
      targetDatabase?: string | null;
    }) => apiSend<SqlSchemaCompareResult>("/api/sql/compare/schema", "POST", vars),
    onError: (error) => notify("error", "Schema compare failed", describeApiError(error)),
  });
}
