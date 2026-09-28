import { useState } from "react";
import { ChevronDown, ChevronRight, Table2, Eye, Zap } from "lucide-react";
import { EmptyState } from "@/components/shared/EmptyState";
import { useSqlObjectColumns } from "@/lib/hooks/useSql";
import type { SqlColumnInfo, SqlObjectInfo } from "@/lib/types";
import type { SqlSchemaModel } from "@/lib/types";

interface SchemaTreeProps {
  schema: SqlSchemaModel | undefined;
  isLoading: boolean;
  error: unknown;
  /** Needed to fire the lazy column fetch for declared objects. */
  connectionId: string | null;
  database: string | null;
  selected: { schema: string; name: string } | null;
  /** `kind` lets the page route a "proc" click to the editor (EXEC) instead of Browse. */
  onSelect: (schema: string, name: string, kind: string) => void;
}

/** Left-sidebar db → schema → table/view tree. Clicking an object opens it in the
 * Browse tab; expanding shows its columns. Declared objects (user-typed names on a
 * catalog-hidden connection) render with a badge and load columns lazily on expand —
 * the server only needs SELECT on the object, not catalog rights. */
export function SchemaTree({ schema, isLoading, error, connectionId, database, selected, onSelect }: SchemaTreeProps) {
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [filter, setFilter] = useState("");

  const toggle = (key: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });

  if (isLoading) {
    return <div className="p-3 text-xs text-muted-foreground" data-testid="sql-schema-loading">Loading schema…</div>;
  }
  if (error) {
    return (
      <div className="p-3 text-xs text-destructive" data-testid="sql-schema-error">
        {error instanceof Error ? error.message : String(error)}
      </div>
    );
  }
  if (!schema || schema.schemas.length === 0) {
    if (schema?.metadataHidden) {
      // Locked-down environment with nothing declared: the identity can query but can't
      // see catalog metadata — distinct from "empty database" so the user knows browsing
      // isn't broken, just denied. Point at the declared-objects escape hatch.
      const held = (schema.effectivePermissions ?? []).filter(
        (p) => p !== "VIEW DEFINITION",
      );
      return (
        <EmptyState
          title="Schema hidden by permissions"
          description={`Your identity can query objects it has rights to but can't browse catalog metadata. ${
            held.length > 0 ? `You hold: ${held.join(", ")}. ` : ""
          }Ask a DBA for VIEW DEFINITION or db_datareader to enable browsing — or declare object names under Settings → SQL to browse what you're granted.`}
          testId="sql-schema-hidden"
        />
      );
    }
    return <EmptyState title="No schema objects" description="This database has no user tables or views." testId="sql-schema-empty" />;
  }

  const needle = filter.trim().toLowerCase();
  const hiddenCatalog = schema.metadataHidden === true;

  return (
    <div className="flex min-h-0 flex-1 flex-col" data-testid="sql-schema-tree">
      <input
        type="text"
        value={filter}
        onChange={(e) => setFilter(e.target.value)}
        placeholder="Filter objects…"
        className="mx-2 my-2 rounded-md border bg-card px-2 py-1 text-xs"
        data-testid="sql-schema-filter"
      />
      {hiddenCatalog && (
        // A declared-only tree is partial by construction — say so instead of letting it
        // pass for a full catalog browse.
        <div
          className="mx-2 mb-1 rounded border border-warning/50 px-2 py-1.5 text-xs text-warning"
          data-testid="sql-schema-partial"
        >
          Catalog metadata is hidden — showing declared objects only. Columns load on
          expand.
        </div>
      )}
      <div className="min-h-0 flex-1 overflow-auto px-2 pb-2 text-xs">
        {schema.schemas.map((group) => {
          const objects = needle
            ? group.objects.filter((o) => o.name.toLowerCase().includes(needle))
            : group.objects;
          if (objects.length === 0) return null;
          const schemaKey = `schema:${group.name}`;
          const schemaOpen = expanded.has(schemaKey) || !!needle;
          return (
            <div key={group.name}>
              <button
                onClick={() => toggle(schemaKey)}
                aria-expanded={schemaOpen}
                className="flex w-full items-center gap-1 rounded px-1 py-1 font-medium hover:bg-accent"
                data-testid={`sql-schema-${group.name}`}
              >
                {schemaOpen ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
                {group.name}
                <span className="ml-auto text-muted-foreground">{objects.length}</span>
              </button>
              {schemaOpen &&
                objects.map((obj) => {
                  const objKey = `${group.name}.${obj.name}`;
                  const objOpen = expanded.has(`obj:${objKey}`);
                  const isSelected = selected?.schema === group.name && selected?.name === obj.name;
                  return (
                    <div key={objKey}>
                      <div
                        className={`flex items-center gap-1 rounded py-0.5 pl-5 pr-1 ${isSelected ? "bg-primary/15 text-primary" : "hover:bg-accent"}`}
                      >
                        <button
                          onClick={() => toggle(`obj:${objKey}`)}
                          aria-expanded={objOpen}
                          className="p-0.5"
                          data-testid={`sql-object-expand-${objKey}`}
                        >
                          {objOpen ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
                        </button>
                        <button
                          onClick={() => onSelect(group.name, obj.name, obj.kind)}
                          className="flex flex-1 items-center gap-1 truncate text-left"
                          title={
                            obj.isDeclared
                              ? `${objKey} — declared in settings, not discovered via catalog metadata`
                              : `${objKey} (${obj.kind})`
                          }
                          data-testid={`sql-object-${objKey}`}
                        >
                          {obj.kind === "view" ? (
                            <Eye className="h-3 w-3 shrink-0" />
                          ) : obj.kind === "proc" ? (
                            <Zap className="h-3 w-3 shrink-0" />
                          ) : (
                            <Table2 className="h-3 w-3 shrink-0" />
                          )}
                          <span className="truncate">{obj.name}</span>
                          {obj.isDeclared && (
                            <span
                              className="ml-auto shrink-0 rounded border px-1 text-[10px] text-muted-foreground"
                              data-testid={`sql-declared-badge-${objKey}`}
                            >
                              declared
                            </span>
                          )}
                        </button>
                      </div>
                      {objOpen && (
                        <div className="pl-11 text-muted-foreground" data-testid={`sql-columns-${objKey}`}>
                          <ObjectColumns
                            obj={obj}
                            objKey={objKey}
                            connectionId={connectionId}
                            database={database}
                            schemaName={group.name}
                          />
                        </div>
                      )}
                    </div>
                  );
                })}
            </div>
          );
        })}
      </div>
    </div>
  );
}

function ObjectColumns({
  obj,
  objKey,
  connectionId,
  database,
  schemaName,
}: {
  obj: SqlObjectInfo;
  objKey: string;
  connectionId: string | null;
  database: string | null;
  schemaName: string;
}) {
  if (obj.kind === "proc") {
    // Procedures can't be introspected with SELECT TOP 0 — they're runnable, not
    // browsable. Selecting the node inserts an EXEC into the editor.
    return (
      <div className="py-0.5" data-testid={`sql-proc-hint-${objKey}`}>
        Stored procedure — click the name to insert an EXEC.
      </div>
    );
  }
  // Declared objects carry no catalog columns; fetch on first expand. Once the result
  // merges into the schema cache, obj.columns is non-empty and this branch stops
  // rendering (the static list below shows the same data).
  if (obj.isDeclared && obj.columns.length === 0) {
    return (
      <DeclaredColumns
        connectionId={connectionId}
        database={database}
        schemaName={schemaName}
        objectName={obj.name}
        objKey={objKey}
      />
    );
  }
  if (obj.columns.length === 0) {
    return (
      <div className="py-0.5" data-testid={`sql-columns-empty-${objKey}`}>
        No columns
      </div>
    );
  }
  return <ColumnList columns={obj.columns} />;
}

function DeclaredColumns({
  connectionId,
  database,
  schemaName,
  objectName,
  objKey,
}: {
  connectionId: string | null;
  database: string | null;
  schemaName: string;
  objectName: string;
  objKey: string;
}) {
  const query = useSqlObjectColumns(connectionId, database, schemaName, objectName);

  if (query.isPending) {
    return (
      <div className="py-0.5" data-testid={`sql-columns-loading-${objKey}`}>
        Loading columns…
      </div>
    );
  }
  if (query.isError) {
    return (
      <div className="py-0.5 text-destructive" data-testid={`sql-columns-error-${objKey}`}>
        {query.error instanceof Error ? query.error.message : String(query.error)}
      </div>
    );
  }
  const data = query.data;
  if (data?.denied) {
    return (
      <div
        className="py-0.5 text-destructive"
        data-testid={`sql-columns-denied-${objKey}`}
        title={data.denial?.guidance ?? undefined}
      >
        Access denied on this object
        {data.denial?.requiredAccess ? ` — needs ${data.denial.requiredAccess}` : ""}
      </div>
    );
  }
  if (data?.error) {
    return (
      <div className="py-0.5 text-destructive" data-testid={`sql-columns-error-${objKey}`}>
        {data.error}
      </div>
    );
  }
  if (!data || data.columns.length === 0) {
    return (
      <div className="py-0.5" data-testid={`sql-columns-empty-${objKey}`}>
        No column metadata returned
      </div>
    );
  }
  return <ColumnList columns={data.columns} />;
}

function ColumnList({ columns }: { columns: SqlColumnInfo[] }) {
  return (
    <>
      {columns.map((col) => (
        <div key={col.name} className="truncate py-0.5" title={`${col.dataType}${col.isNullable ? "" : " NOT NULL"}`}>
          {col.name}
          <span className="ml-1 opacity-60">{col.dataType}</span>
          {col.isPrimaryKey && <span className="ml-1 text-warning">PK</span>}
        </div>
      ))}
    </>
  );
}
