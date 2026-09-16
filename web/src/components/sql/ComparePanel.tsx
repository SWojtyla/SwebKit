import { useState } from "react";
import { GitCompareArrows } from "lucide-react";
import { EmptyState } from "@/components/shared/EmptyState";
import { useSqlDataCompare, useSqlSchema, useSqlSchemaCompare } from "@/lib/hooks";
import type { SqlConnectionEntry, SqlObjectInfo } from "@/lib/types";

interface ComparePanelProps {
  connections: SqlConnectionEntry[];
  sourceId: string;
  onSourceChange: (id: string) => void;
}

/** Data compare (key-column row diff) and schema compare (report only — no sync
 * scripts, per the feature's non-goals) between two configured connections. */
export function ComparePanel({ connections, sourceId, onSourceChange }: ComparePanelProps) {
  const [targetId, setTargetId] = useState<string>("");
  const [schemaName, setSchemaName] = useState("");
  const [tableName, setTableName] = useState("");
  const [keyColumns, setKeyColumns] = useState("");
  const [mode, setMode] = useState<"data" | "schema">("data");

  const resolvedTarget = targetId || connections.find((c) => c.id !== sourceId)?.id || "";
  const sourceSchema = useSqlSchema(sourceId || null, null);

  const dataCompare = useSqlDataCompare();
  const schemaCompare = useSqlSchemaCompare();

  const sourceObjects: SqlObjectInfo[] = sourceId
    ? (sourceSchema.data?.schemas.find((s) => s.name === schemaName)?.objects ?? [])
    : [];

  const schemaOptions = [...new Set((sourceSchema.data?.schemas ?? []).map((s) => s.name))];
  const selectedTable = sourceObjects.find((o) => o.name === tableName);

  const runCompare = () => {
    if (mode === "schema") {
      schemaCompare.mutate({ sourceConnectionId: sourceId, targetConnectionId: resolvedTarget });
    } else {
      const keys = keyColumns.split(",").map((k) => k.trim()).filter(Boolean);
      dataCompare.mutate({
        sourceConnectionId: sourceId,
        targetConnectionId: resolvedTarget,
        schema: schemaName,
        table: tableName,
        keyColumns: keys,
      });
    }
  };

  const canRun =
    !!sourceId && !!resolvedTarget && sourceId !== resolvedTarget &&
    (mode === "schema" || (!!schemaName && !!tableName && keyColumns.trim().length > 0));

  const running = dataCompare.isPending || schemaCompare.isPending;

  return (
    <div className="flex min-h-0 flex-1 flex-col overflow-auto p-4" data-testid="sql-compare-panel">
      <div className="flex flex-wrap items-center gap-2 text-xs">
        <select
          value={mode}
          onChange={(e) => setMode(e.target.value as "data" | "schema")}
          className="rounded border bg-card px-2 py-1.5"
          data-testid="sql-compare-mode"
        >
          <option value="data">Data compare</option>
          <option value="schema">Schema compare</option>
        </select>
        <select
          value={sourceId}
          onChange={(e) => onSourceChange(e.target.value)}
          className="rounded border bg-card px-2 py-1.5"
          data-testid="sql-compare-source"
          aria-label="Source connection"
        >
          {connections.map((c) => (
            <option key={c.id} value={c.id}>{c.displayName}</option>
          ))}
        </select>
        <span className="text-muted-foreground">vs</span>
        <select
          value={resolvedTarget}
          onChange={(e) => setTargetId(e.target.value)}
          className="rounded border bg-card px-2 py-1.5"
          data-testid="sql-compare-target"
          aria-label="Target connection"
        >
          {connections.filter((c) => c.id !== sourceId).map((c) => (
            <option key={c.id} value={c.id}>{c.displayName}</option>
          ))}
        </select>

        {mode === "data" && (
          <>
            <select
              value={schemaName}
              onChange={(e) => { setSchemaName(e.target.value); setTableName(""); }}
              className="rounded border bg-card px-2 py-1.5"
              data-testid="sql-compare-schema"
              aria-label="Schema"
            >
              <option value="">schema…</option>
              {schemaOptions.map((s) => (
                <option key={s} value={s}>{s}</option>
              ))}
            </select>
            <select
              value={tableName}
              onChange={(e) => setTableName(e.target.value)}
              className="rounded border bg-card px-2 py-1.5"
              data-testid="sql-compare-table"
              aria-label="Table"
            >
              <option value="">table…</option>
              {sourceObjects.map((o) => (
                <option key={o.name} value={o.name}>{o.name}</option>
              ))}
            </select>
            <input
              type="text"
              value={keyColumns}
              onChange={(e) => setKeyColumns(e.target.value)}
              placeholder="key columns, e.g. id"
              list="sql-compare-keys"
              className="w-44 rounded border bg-card px-2 py-1.5"
              data-testid="sql-compare-keys"
            />
            <datalist id="sql-compare-keys">
              {selectedTable?.columns.map((c) => (
                <option key={c.name} value={c.name} />
              ))}
            </datalist>
          </>
        )}

        <button
          onClick={runCompare}
          disabled={!canRun || running}
          className="flex items-center gap-1 rounded bg-primary px-3 py-1.5 text-primary-foreground disabled:opacity-50"
          data-testid="sql-compare-run"
        >
          <GitCompareArrows className="h-3.5 w-3.5" />
          {running ? "Comparing…" : "Compare"}
        </button>
      </div>

      {mode === "data" && (
        <div className="mt-4 flex-1" data-testid="sql-compare-data-result">
          {dataCompare.data ? (
            <DataCompareResult result={dataCompare.data} />
          ) : (
            !dataCompare.isPending && (
              <EmptyState
                title="No comparison yet"
                description="Pick a table and its key columns, then Compare."
                testId="sql-compare-data-empty"
              />
            )
          )}
        </div>
      )}
      {mode === "schema" && (
        <div className="mt-4 flex-1" data-testid="sql-compare-schema-result">
          {schemaCompare.data ? (
            <SchemaCompareResult result={schemaCompare.data} />
          ) : (
            !schemaCompare.isPending && (
              <EmptyState
                title="No comparison yet"
                description="Pick two connections, then Compare — a report only, no sync scripts are generated."
                testId="sql-compare-schema-empty"
              />
            )
          )}
        </div>
      )}
    </div>
  );
}

function cellText(v: unknown): string {
  if (v === null || v === undefined) return "NULL";
  if (typeof v === "object") return JSON.stringify(v);
  return String(v);
}

function DataCompareResult({ result }: { result: import("@/lib/types").SqlDataCompareResult }) {
  return (
    <div className="space-y-4 text-xs">
      <p data-testid="sql-compare-data-summary">
        <span className="text-destructive">{result.totalOnlyInSource} only in source</span> ·{" "}
        <span className="text-warning">{result.totalOnlyInTarget} only in target</span> ·{" "}
        <span className="text-primary">{result.totalChanged} changed</span>
        {result.truncated && <span className="text-muted-foreground"> (truncated)</span>}
      </p>
      {result.schemaWarnings.map((w) => (
        <p key={w} className="text-warning" data-testid="sql-compare-warning">{w}</p>
      ))}
      {result.onlyInSource.length > 0 && (
        <DiffRows title="Only in source" rows={result.onlyInSource} testId="sql-compare-only-source" />
      )}
      {result.onlyInTarget.length > 0 && (
        <DiffRows title="Only in target" rows={result.onlyInTarget} testId="sql-compare-only-target" />
      )}
      {result.changed.length > 0 && (
        <div>
          <h4 className="mb-1 font-medium">Changed rows</h4>
          <table className="w-full border-collapse" data-testid="sql-compare-changed">
            <thead>
              <tr className="text-left text-muted-foreground">
                <th className="border-b px-2 py-1">Key</th>
                <th className="border-b px-2 py-1">Column</th>
                <th className="border-b px-2 py-1">Source</th>
                <th className="border-b px-2 py-1">Target</th>
              </tr>
            </thead>
            <tbody>
              {result.changed.map((row, i) =>
                row.diffs.map((d, j) => (
                  <tr key={`${i}-${j}`} className="odd:bg-muted/30">
                    {j === 0 && (
                      <td rowSpan={row.diffs.length} className="border-b px-2 py-1 align-top">
                        {Object.entries(row.key).map(([k, v]) => `${k}=${cellText(v)}`).join(", ")}
                      </td>
                    )}
                    <td className="border-b px-2 py-1">{d.column}</td>
                    <td className="border-b px-2 py-1 text-destructive">{cellText(d.sourceValue)}</td>
                    <td className="border-b px-2 py-1 text-success">{cellText(d.targetValue)}</td>
                  </tr>
                )),
              )}
            </tbody>
          </table>
        </div>
      )}
      {result.totalOnlyInSource === 0 && result.totalOnlyInTarget === 0 && result.totalChanged === 0 && (
        <EmptyState title="Tables are identical" description="No differing rows found." testId="sql-compare-identical" />
      )}
    </div>
  );
}

function DiffRows({ title, rows, testId }: { title: string; rows: Record<string, unknown>[]; testId: string }) {
  const columns = Object.keys(rows[0] ?? {});
  return (
    <div>
      <h4 className="mb-1 font-medium">{title}</h4>
      <table className="w-full border-collapse" data-testid={testId}>
        <thead>
          <tr className="text-left text-muted-foreground">
            {columns.map((c) => (
              <th key={c} className="border-b px-2 py-1">{c}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i} className="odd:bg-muted/30">
              {columns.map((c) => (
                <td key={c} className="border-b px-2 py-1">{cellText(row[c])}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function SchemaCompareResult({ result }: { result: import("@/lib/types").SqlSchemaCompareResult }) {
  const section = (
    title: string,
    objects: import("@/lib/types").SqlObjectDiff[],
    testId: string,
  ) =>
    objects.length > 0 && (
      <div>
        <h4 className="mb-1 text-xs font-medium">{title}</h4>
        <ul className="space-y-1 text-xs" data-testid={testId}>
          {objects.map((o) => (
            <li key={`${o.schema}.${o.name}`} className="rounded border p-2">
              <span className="font-medium">{o.schema}.{o.name}</span>
              <span className="ml-2 text-muted-foreground">{o.kind}</span>
              {o.diffs.length > 0 && (
                <ul className="mt-1 space-y-0.5 text-muted-foreground">
                  {o.diffs.map((d) => (
                    <li key={d.property}>
                      {d.property}: <span className="text-destructive">{d.sourceValue ?? "—"}</span>
                      {" → "}
                      <span className="text-success">{d.targetValue ?? "—"}</span>
                    </li>
                  ))}
                </ul>
              )}
            </li>
          ))}
        </ul>
      </div>
    );

  const empty =
    result.onlyInSource.length === 0 && result.onlyInTarget.length === 0 && result.differing.length === 0;

  return (
    <div className="space-y-4" data-testid="sql-compare-schema-report">
      {empty ? (
        <EmptyState title="Schemas match" description="No object differences found." testId="sql-compare-schema-identical" />
      ) : (
        <>
          {section("Only in source", result.onlyInSource, "sql-compare-schema-only-source")}
          {section("Only in target", result.onlyInTarget, "sql-compare-schema-only-target")}
          {section("Differing objects", result.differing, "sql-compare-schema-differing")}
        </>
      )}
    </div>
  );
}
