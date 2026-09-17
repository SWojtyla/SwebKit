import { useMemo, useState } from "react";
import { Plus, Trash2, WandSparkles } from "lucide-react";
import type { SqlObjectInfo, SqlSchemaModel } from "@/lib/types";
import { buildSelectQuery, type SqlFilterOperator, type SqlQueryFilter } from "@/lib/sql-query-builder";

interface QueryBuilderPanelProps {
  schema: SqlSchemaModel | undefined;
  onInsert: (sql: string) => void;
}

const operators: SqlFilterOperator[] = ["=", "<>", ">", ">=", "<", "<=", "LIKE", "IS NULL", "IS NOT NULL"];
const emptyFilter = (): SqlQueryFilter => ({ column: "", operator: "=", value: "" });

export function QueryBuilderPanel({ schema, onInsert }: QueryBuilderPanelProps) {
  const [open, setOpen] = useState(false);
  const [objectKey, setObjectKey] = useState("");
  const [columns, setColumns] = useState<string[]>([]);
  const [filters, setFilters] = useState<SqlQueryFilter[]>([]);
  const [orderBy, setOrderBy] = useState("");
  const [descending, setDescending] = useState(false);
  const [top, setTop] = useState(100);

  const objects = useMemo(
    () => (schema?.schemas ?? []).flatMap((group) => group.objects.map((object) => ({ group: group.name, object }))),
    [schema],
  );
  const selected = objects.find(({ group, object }) => `${group}.${object.name}` === objectKey);
  const selectedObject: SqlObjectInfo | undefined = selected?.object;

  const selectObject = (key: string) => {
    setObjectKey(key);
    const item = objects.find(({ group, object }) => `${group}.${object.name}` === key);
    setColumns(item?.object.columns.map((c) => c.name) ?? []);
    setFilters([]);
    setOrderBy("");
  };

  const updateFilter = (index: number, patch: Partial<SqlQueryFilter>) =>
    setFilters((current) => current.map((filter, i) => (i === index ? { ...filter, ...patch } : filter)));

  return (
    <div className="mb-2 rounded border" data-testid="sql-query-builder">
      <button
        onClick={() => setOpen((value) => !value)}
        className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-xs font-medium hover:bg-accent"
        aria-expanded={open}
        data-testid="sql-query-builder-toggle"
      >
        <WandSparkles className="h-3.5 w-3.5" /> Guided query builder
      </button>
      {open && (
        <div className="space-y-3 border-t p-3 text-xs">
          <div className="flex flex-wrap items-center gap-2">
            <label htmlFor="sql-builder-table" className="font-medium">Table</label>
            <select
              id="sql-builder-table"
              value={objectKey}
              onChange={(e) => selectObject(e.target.value)}
              className="rounded border bg-card px-2 py-1"
              data-testid="sql-builder-table"
            >
              <option value="">choose a table…</option>
              {objects.map(({ group, object }) => (
                <option key={`${group}.${object.name}`} value={`${group}.${object.name}`}>
                  {group}.{object.name}
                </option>
              ))}
            </select>
            <label htmlFor="sql-builder-top" className="font-medium">Top</label>
            <input
              id="sql-builder-top"
              type="number"
              min={1}
              max={5000}
              value={top}
              onChange={(e) => setTop(Number(e.target.value))}
              className="w-20 rounded border bg-card px-2 py-1"
              data-testid="sql-builder-top"
            />
          </div>

          {selected && selectedObject && (
            <>
              <fieldset>
                <legend className="mb-1 font-medium">Columns</legend>
                <div className="flex flex-wrap gap-x-3 gap-y-1">
                  {selectedObject.columns.map((column) => (
                    <label key={column.name} className="flex items-center gap-1">
                      <input
                        type="checkbox"
                        checked={columns.includes(column.name)}
                        onChange={(e) => setColumns((current) =>
                          e.target.checked ? [...current, column.name] : current.filter((name) => name !== column.name)
                        )}
                        data-testid={`sql-builder-column-${column.name}`}
                      />
                      {column.name}
                    </label>
                  ))}
                </div>
              </fieldset>

              <div className="space-y-1">
                <div className="flex items-center justify-between">
                  <span className="font-medium">Filters</span>
                  <button
                    onClick={() => setFilters((current) => [...current, emptyFilter()])}
                    className="flex items-center gap-1 rounded border px-2 py-0.5 hover:bg-accent"
                    data-testid="sql-builder-add-filter"
                  >
                    <Plus className="h-3 w-3" /> Add filter
                  </button>
                </div>
                {filters.map((filter, index) => (
                  <div key={index} className="flex items-center gap-1" data-testid={`sql-builder-filter-${index}`}>
                    <select
                      value={filter.column}
                      onChange={(e) => updateFilter(index, { column: e.target.value })}
                      className="rounded border bg-card px-2 py-1"
                      aria-label={`Filter ${index + 1} column`}
                    >
                      <option value="">column…</option>
                      {selectedObject.columns.map((column) => <option key={column.name}>{column.name}</option>)}
                    </select>
                    <select
                      value={filter.operator}
                      onChange={(e) => updateFilter(index, { operator: e.target.value as SqlFilterOperator })}
                      className="rounded border bg-card px-2 py-1"
                      aria-label={`Filter ${index + 1} operator`}
                    >
                      {operators.map((operator) => <option key={operator}>{operator}</option>)}
                    </select>
                    {!filter.operator.startsWith("IS ") && (
                      <input
                        value={filter.value}
                        onChange={(e) => updateFilter(index, { value: e.target.value })}
                        placeholder="value"
                        className="rounded border bg-card px-2 py-1"
                        aria-label={`Filter ${index + 1} value`}
                      />
                    )}
                    <button
                      onClick={() => setFilters((current) => current.filter((_, i) => i !== index))}
                      className="rounded border p-1 text-destructive hover:bg-accent"
                      title="Remove filter"
                      data-testid={`sql-builder-remove-filter-${index}`}
                    >
                      <Trash2 className="h-3 w-3" />
                    </button>
                  </div>
                ))}
              </div>

              <div className="flex flex-wrap items-center gap-2">
                <label htmlFor="sql-builder-order" className="font-medium">Order by</label>
                <select
                  id="sql-builder-order"
                  value={orderBy}
                  onChange={(e) => setOrderBy(e.target.value)}
                  className="rounded border bg-card px-2 py-1"
                  data-testid="sql-builder-order"
                >
                  <option value="">none</option>
                  {selectedObject.columns.map((column) => <option key={column.name}>{column.name}</option>)}
                </select>
                <select
                  value={descending ? "desc" : "asc"}
                  onChange={(e) => setDescending(e.target.value === "desc")}
                  className="rounded border bg-card px-2 py-1"
                  aria-label="Sort direction"
                  data-testid="sql-builder-direction"
                  disabled={!orderBy}
                >
                  <option value="asc">Ascending</option>
                  <option value="desc">Descending</option>
                </select>
                <button
                  onClick={() => onInsert(buildSelectQuery({
                    schema: selected.group,
                    table: selected.object.name,
                    columns,
                    filters,
                    orderBy,
                    descending,
                    top,
                  }))}
                  className="ml-auto rounded bg-primary px-3 py-1 text-primary-foreground hover:opacity-90"
                  data-testid="sql-builder-insert"
                >
                  Insert into editor
                </button>
              </div>
            </>
          )}
        </div>
      )}
    </div>
  );
}
