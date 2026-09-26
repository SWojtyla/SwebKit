import { useState } from "react";
import { ChevronDown, ChevronRight, Table2, Eye } from "lucide-react";
import { EmptyState } from "@/components/shared/EmptyState";
import type { SqlSchemaModel } from "@/lib/types";

interface SchemaTreeProps {
  schema: SqlSchemaModel | undefined;
  isLoading: boolean;
  error: unknown;
  selected: { schema: string; name: string } | null;
  onSelect: (schema: string, name: string) => void;
}

/** Left-sidebar db → schema → table/view tree. Clicking an object opens it in the
 * Browse tab; expanding shows its columns. */
export function SchemaTree({ schema, isLoading, error, selected, onSelect }: SchemaTreeProps) {
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
      // Locked-down environment: the identity can query but can't see catalog metadata —
      // distinct from "empty database" so the user knows browsing isn't broken, just denied.
      const held = (schema.effectivePermissions ?? []).filter(
        (p) => p !== "VIEW DEFINITION",
      );
      return (
        <EmptyState
          title="Schema hidden by permissions"
          description={`Your identity can query objects it has rights to but can't browse catalog metadata. ${
            held.length > 0 ? `You hold: ${held.join(", ")}. ` : ""
          }Ask a DBA for VIEW DEFINITION or db_datareader to enable browsing.`}
          testId="sql-schema-hidden"
        />
      );
    }
    return <EmptyState title="No schema objects" description="This database has no user tables or views." testId="sql-schema-empty" />;
  }

  const needle = filter.trim().toLowerCase();

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
                          onClick={() => onSelect(group.name, obj.name)}
                          className="flex flex-1 items-center gap-1 truncate text-left"
                          title={`${objKey} (${obj.kind})`}
                          data-testid={`sql-object-${objKey}`}
                        >
                          {obj.kind === "view" ? <Eye className="h-3 w-3 shrink-0" /> : <Table2 className="h-3 w-3 shrink-0" />}
                          <span className="truncate">{obj.name}</span>
                        </button>
                      </div>
                      {objOpen && (
                        <div className="pl-11 text-muted-foreground" data-testid={`sql-columns-${objKey}`}>
                          {obj.columns.map((col) => (
                            <div key={col.name} className="truncate py-0.5" title={`${col.dataType}${col.isNullable ? "" : " NOT NULL"}`}>
                              {col.name}
                              <span className="ml-1 opacity-60">{col.dataType}</span>
                              {col.isPrimaryKey && <span className="ml-1 text-warning">PK</span>}
                            </div>
                          ))}
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
