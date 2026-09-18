import { useState } from "react";
import { FolderOpen, Play, Save, Trash2 } from "lucide-react";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { EmptyState } from "@/components/shared/EmptyState";
import { useClearSqlHistory, useDeleteSqlQuery, useSavedSqlQueries, useSaveSqlQuery, useSqlHistory } from "@/lib/hooks";

interface SavedQueriesPanelProps {
  connectionId: string;
  currentSql: string;
  onLoad: (sql: string) => void;
  onRun: (sql: string) => void;
}

/** Saved queries (grouped by folder) plus the capped execution history. Loading a
 * query copies it into the editor; Run executes it immediately. */
export function SavedQueriesPanel({ connectionId, currentSql, onLoad, onRun }: SavedQueriesPanelProps) {
  const saved = useSavedSqlQueries(connectionId);
  const history = useSqlHistory(connectionId);
  const saveQuery = useSaveSqlQuery();
  const deleteQuery = useDeleteSqlQuery();
  const clearHistory = useClearSqlHistory();

  const [name, setName] = useState("");
  const [folder, setFolder] = useState("");
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);
  const [confirmClear, setConfirmClear] = useState(false);

  const folders = new Map<string, typeof saved.data>();
  for (const q of saved.data ?? []) {
    const key = q.folder ?? "";
    folders.set(key, [...(folders.get(key) ?? []), q]);
  }

  return (
    <div className="flex min-h-0 flex-1 gap-4 overflow-auto p-4" data-testid="sql-saved-panel">
      <div className="w-1/2 space-y-3">
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-semibold">Saved queries</h3>
        </div>
        <div className="flex gap-2">
          <input
            type="text"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Query name"
            className="w-40 rounded border bg-card px-2 py-1 text-xs"
            data-testid="sql-save-name"
          />
          <input
            type="text"
            value={folder}
            onChange={(e) => setFolder(e.target.value)}
            placeholder="Folder (optional)"
            className="w-32 rounded border bg-card px-2 py-1 text-xs"
            data-testid="sql-save-folder"
          />
          <button
            onClick={() => {
              saveQuery.mutate(
                { name, folder: folder || null, sql: currentSql, connectionId },
                { onSuccess: () => setName("") },
              );
            }}
            disabled={!name.trim() || !currentSql.trim() || saveQuery.isPending}
            title={saveQuery.isPending ? "Saving…" : !name.trim() ? "Name the query first" : !currentSql.trim() ? "No SQL to save" : undefined}
            className="flex items-center gap-1 rounded bg-primary px-2 py-1 text-xs text-primary-foreground disabled:opacity-50"
            data-testid="sql-save-submit"
          >
            <Save className="h-3 w-3" /> Save current
          </button>
        </div>
        {!currentSql.trim() && (
          <p className="text-xs text-muted-foreground">Type a query in the editor to save it.</p>
        )}

        {saved.isLoading && <p className="text-xs text-muted-foreground">Loading…</p>}
        {saved.isError && (
          <p className="text-xs text-destructive">{String(saved.error)}</p>
        )}
        {saved.data && saved.data.length === 0 && (
          <EmptyState title="No saved queries" description="Save the editor's query with the form above." testId="sql-saved-empty" />
        )}
        {[...folders.entries()].map(([folderName, queries]) => (
          <div key={folderName || "(root)"}>
            {folderName && (
              <p className="flex items-center gap-1 text-xs font-medium text-muted-foreground">
                <FolderOpen className="h-3 w-3" /> {folderName}
              </p>
            )}
            <ul className="mt-1 space-y-1">
              {(queries ?? []).map((q) => (
                <li key={q.id} className="rounded border p-2" data-testid={`sql-saved-${q.id}`}>
                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => onLoad(q.sql)}
                      className="flex-1 truncate text-left text-xs font-medium hover:text-primary"
                      title={q.sql}
                      data-testid={`sql-saved-load-${q.id}`}
                    >
                      {q.name}
                    </button>
                    <button
                      onClick={() => onRun(q.sql)}
                      className="rounded border p-1 hover:bg-accent"
                      title="Run"
                      data-testid={`sql-saved-run-${q.id}`}
                    >
                      <Play className="h-3 w-3" />
                    </button>
                    <button
                      onClick={() => setPendingDeleteId(q.id)}
                      className="rounded border p-1 text-destructive hover:bg-accent"
                      title="Delete"
                      data-testid={`sql-saved-delete-${q.id}`}
                    >
                      <Trash2 className="h-3 w-3" />
                    </button>
                  </div>
                  <pre className="mt-1 max-h-16 overflow-auto whitespace-pre-wrap text-[11px] text-muted-foreground">{q.sql}</pre>
                  {pendingDeleteId === q.id && (
                    <ConfirmBar
                      message={`Delete "${q.name}"?`}
                      confirmLabel="Delete"
                      onConfirm={() => {
                        deleteQuery.mutate(q.id);
                        setPendingDeleteId(null);
                      }}
                      onCancel={() => setPendingDeleteId(null)}
                      testId={`sql-saved-confirm-${q.id}`}
                    />
                  )}
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>

      <div className="w-1/2 space-y-3">
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-semibold">History</h3>
          {(history.data?.length ?? 0) > 0 && (
            <button
              onClick={() => setConfirmClear(true)}
              className="text-xs text-destructive hover:opacity-80"
              data-testid="sql-history-clear"
            >
              Clear
            </button>
          )}
        </div>
        {confirmClear && (
          <ConfirmBar
            message="Clear all query history?"
            confirmLabel="Clear"
            onConfirm={() => {
              clearHistory.mutate();
              setConfirmClear(false);
            }}
            onCancel={() => setConfirmClear(false)}
            testId="sql-history-clear-confirm"
          />
        )}
        {history.isLoading && <p className="text-xs text-muted-foreground">Loading…</p>}
        {history.data && history.data.length === 0 && (
          <EmptyState title="No history yet" description="Executed queries appear here." testId="sql-history-empty" />
        )}
        <ul className="space-y-1">
          {(history.data ?? []).map((h) => (
            <li key={h.id} className="rounded border p-2" data-testid={`sql-history-${h.id}`}>
              <div className="flex items-center gap-2 text-[11px] text-muted-foreground">
                <span className={h.succeeded ? "text-success" : "text-destructive"}>
                  {h.succeeded ? "ok" : "failed"}
                </span>
                <span>{new Date(h.executedAt).toLocaleString()}</span>
                <span className="ml-auto">{h.rowCount} row(s) · {h.elapsedMs} ms</span>
                <button
                  onClick={() => onLoad(h.sql)}
                  className="rounded border px-1.5 py-0.5 hover:bg-accent"
                  data-testid={`sql-history-load-${h.id}`}
                >
                  Load
                </button>
              </div>
              <pre className="mt-1 max-h-16 overflow-auto whitespace-pre-wrap text-[11px]">{h.sql}</pre>
              {h.error && <p className="mt-1 text-[11px] text-destructive">{h.error}</p>}
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
