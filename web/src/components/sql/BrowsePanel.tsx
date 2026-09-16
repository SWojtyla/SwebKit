import { useState } from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { EmptyState } from "@/components/shared/EmptyState";
import { useSqlTableRows } from "@/lib/hooks";
import type { SqlObjectInfo, SqlSchemaModel } from "@/lib/types";
import { ResultsGrid } from "./ResultsGrid";

const PAGE_SIZE = 100;

interface BrowsePanelProps {
  connectionId: string;
  database: string | null;
  schema: SqlSchemaModel | undefined;
  table: { schema: string; name: string } | null;
}

/** Bounded top-N table browse — server-side single-column text filter, ordering and
 * skip/take paging so a large table never reaches the frontend unfiltered. */
export function BrowsePanel({ connectionId, database, schema, table }: BrowsePanelProps) {
  const [filterColumn, setFilterColumn] = useState<string>("");
  const [filterText, setFilterText] = useState("");
  const [orderBy, setOrderBy] = useState<string>("");
  const [desc, setDesc] = useState(false);
  const [page, setPage] = useState(0);

  const object: SqlObjectInfo | undefined = table
    ? schema?.schemas.find((s) => s.name === table.schema)?.objects.find((o) => o.name === table.name)
    : undefined;

  const rows = useSqlTableRows(connectionId, table?.schema ?? null, table?.name ?? null, {
    database,
    filterColumn: filterColumn || null,
    filter: filterText || null,
    orderBy: orderBy || null,
    desc,
    skip: page * PAGE_SIZE,
    take: PAGE_SIZE,
  });

  if (!table) {
    return (
      <EmptyState
        title="No table selected"
        description="Pick a table or view in the schema tree to browse its rows."
        testId="sql-browse-empty"
      />
    );
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col" data-testid="sql-browse-panel">
      <div className="flex flex-wrap items-center gap-2 border-b px-3 py-2 text-xs">
        <span className="font-medium" data-testid="sql-browse-title">
          {table.schema}.{table.name}
        </span>
        <select
          value={filterColumn}
          onChange={(e) => { setFilterColumn(e.target.value); setPage(0); }}
          className="rounded border bg-card px-1.5 py-1"
          data-testid="sql-browse-filter-column"
          aria-label="Filter column"
        >
          <option value="">No filter</option>
          {object?.columns.map((c) => (
            <option key={c.name} value={c.name}>{c.name}</option>
          ))}
        </select>
        {filterColumn && (
          <input
            type="text"
            value={filterText}
            onChange={(e) => { setFilterText(e.target.value); setPage(0); }}
            placeholder="contains…"
            className="w-40 rounded border bg-card px-1.5 py-1"
            data-testid="sql-browse-filter-text"
          />
        )}
        <select
          value={orderBy}
          onChange={(e) => { setOrderBy(e.target.value); setPage(0); }}
          className="rounded border bg-card px-1.5 py-1"
          data-testid="sql-browse-order-by"
          aria-label="Sort column"
        >
          <option value="">No sort</option>
          {object?.columns.map((c) => (
            <option key={c.name} value={c.name}>{c.name}</option>
          ))}
        </select>
        {orderBy && (
          <button
            onClick={() => setDesc((d) => !d)}
            className="rounded border px-1.5 py-1 hover:bg-accent"
            data-testid="sql-browse-order-dir"
          >
            {desc ? "DESC" : "ASC"}
          </button>
        )}
        <div className="ml-auto flex items-center gap-1">
          <button
            onClick={() => setPage((p) => Math.max(0, p - 1))}
            disabled={page === 0}
            className="rounded border p-1 hover:bg-accent disabled:opacity-40"
            data-testid="sql-browse-prev"
            aria-label="Previous page"
          >
            <ChevronLeft className="h-3.5 w-3.5" />
          </button>
          <span data-testid="sql-browse-page">Page {page + 1}</span>
          <button
            onClick={() => setPage((p) => p + 1)}
            disabled={(rows.data?.rows.length ?? 0) < PAGE_SIZE}
            className="rounded border p-1 hover:bg-accent disabled:opacity-40"
            data-testid="sql-browse-next"
            aria-label="Next page"
          >
            <ChevronRight className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>
      {rows.isError ? (
        <div className="p-4 text-sm text-destructive" data-testid="sql-browse-error">
          {rows.error instanceof Error ? rows.error.message : String(rows.error)}
        </div>
      ) : (
        <ResultsGrid result={rows.data} isRunning={rows.isLoading} testId="sql-browse-results" />
      )}
    </div>
  );
}
