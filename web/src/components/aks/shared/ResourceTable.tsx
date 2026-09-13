import { memo, useMemo, useState, type ReactNode, type MouseEvent, type JSX } from "react";
import { AlertCircle, ArrowDown, ArrowUp, ArrowUpDown, Search } from "lucide-react";
import { SkeletonTableRows } from "@/components/shared/Skeleton";

export interface Column<T> {
  header: ReactNode;
  cell: (row: T) => ReactNode;
  className?: string;
  /** When present, the column header becomes clickable and sorts rows by this derived value. */
  sortValue?: (row: T) => string | number;
}

export interface ResourceTableProps<T extends { name: string; namespace?: string }> {
  data?: T[];
  isLoading: boolean;
  /** Distinct from "no items" — an RBAC-denied list or a sidecar error must not render as empty. */
  error?: unknown;
  isMulti?: boolean;
  columns: Column<T>[];
  keyExtractor?: (row: T) => string;
  onRowClick?: (row: T) => void;
  onRowContextMenu?: (e: MouseEvent<HTMLTableRowElement>, row: T) => void;
  emptyMessage: string;
  testIdPrefix: string;
  tableBodyTestId?: string;
  selectedKey?: string | null;
  getRowClassName?: (row: T) => string;
  /**
   * Applied before any user interaction (e.g. unhealthy-first for Pods/Deployments) and whenever
   * the user clears their own sort by clicking a third time. Ignored while the user has an active
   * column sort.
   */
  defaultSort?: { sortValue: (row: T) => string | number; direction?: "asc" | "desc" };
  /** Set to hide the built-in name filter for a tab where it wouldn't make sense. */
  hideNameFilter?: boolean;
}

type SortKey = "name" | number;
type SortState = { key: SortKey; direction: "asc" | "desc" } | null;

function compareValues(a: string | number, b: string | number): number {
  if (typeof a === "number" && typeof b === "number") return a - b;
  return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: "base" });
}

function ResourceTableInner<T extends { name: string; namespace?: string }>({
  data,
  isLoading,
  error,
  isMulti,
  columns,
  keyExtractor,
  onRowClick,
  onRowContextMenu,
  emptyMessage,
  testIdPrefix,
  tableBodyTestId,
  selectedKey,
  getRowClassName,
  defaultSort,
  hideNameFilter,
}: ResourceTableProps<T>): JSX.Element {
  const [nameFilter, setNameFilter] = useState("");
  const [sort, setSort] = useState<SortState>(null);

  const columnCount = 1 + (isMulti ? 1 : 0) + columns.length;

  // Only a real onRowClick makes a row clickable. A context-menu-only row must not carry the
  // same pointer-cursor/hover/keyboard-activation affordance as one with a left-click action —
  // that mismatch (every row "looked" clickable, most weren't) was the root cause of the
  // reported "have to right-click to do anything" bug.
  const clickable = Boolean(onRowClick);

  const rawRows = useMemo(() => data ?? [], [data]);

  const visibleRows = useMemo(() => {
    const filtered = nameFilter.trim()
      ? rawRows.filter((row) => row.name.toLowerCase().includes(nameFilter.trim().toLowerCase()))
      : rawRows;

    const activeSort = sort ?? (defaultSort ? { key: "name" as SortKey, direction: defaultSort.direction ?? "asc" } : null);
    const sortValueFor = sort
      ? sort.key === "name"
        ? (row: T) => row.name
        : columns[sort.key as number]?.sortValue
      : defaultSort?.sortValue;

    if (!activeSort || !sortValueFor) return filtered;
    const sorted = [...filtered].sort((a, b) => compareValues(sortValueFor(a), sortValueFor(b)));
    if (activeSort.direction === "desc") sorted.reverse();
    return sorted;
  }, [rawRows, nameFilter, sort, defaultSort, columns]);

  const toggleSort = (key: SortKey) => {
    setSort((prev) => {
      if (!prev || prev.key !== key) return { key, direction: "asc" };
      if (prev.direction === "asc") return { key, direction: "desc" };
      return null;
    });
  };

  const sortIcon = (key: SortKey) => {
    if (!sort || sort.key !== key) return <ArrowUpDown className="h-3 w-3 opacity-40" />;
    return sort.direction === "asc" ? <ArrowUp className="h-3 w-3" /> : <ArrowDown className="h-3 w-3" />;
  };

  if (error) {
    return (
      <div className="flex items-center gap-2 p-4 text-sm text-destructive" data-testid={`${testIdPrefix}s-error`}>
        <AlertCircle className="h-4 w-4 shrink-0" />
        <span>{error instanceof Error ? error.message : String(error)}</span>
      </div>
    );
  }

  const getKey = keyExtractor ?? ((row: T) => (row.namespace ? `${row.namespace}/${row.name}` : row.name));

  const header = (
    <thead>
      <tr className="border-b text-left text-xs text-muted-foreground">
        <th className="w-full py-2 pr-4">
          <button
            onClick={() => toggleSort("name")}
            className="flex items-center gap-1 hover:text-foreground"
            data-testid={`${testIdPrefix}s-sort-name`}
          >
            Name {sortIcon("name")}
          </button>
        </th>
        {isMulti && <th className="whitespace-nowrap py-2 pr-4">Namespace</th>}
        {columns.map((col, i) => (
          <th key={i} className={col.className ?? "whitespace-nowrap py-2 pr-4"}>
            {col.sortValue ? (
              <button
                onClick={() => toggleSort(i)}
                className="flex items-center gap-1 hover:text-foreground"
                data-testid={`${testIdPrefix}s-sort-${i}`}
              >
                {col.header} {sortIcon(i)}
              </button>
            ) : (
              col.header
            )}
          </th>
        ))}
      </tr>
    </thead>
  );

  const filterBar = !hideNameFilter && (
    <div className="mb-2 flex items-center gap-1.5 px-1">
      <Search className="h-3.5 w-3.5 text-muted-foreground" />
      <input
        type="text"
        value={nameFilter}
        onChange={(e) => setNameFilter(e.target.value)}
        placeholder="Filter by name…"
        className="w-56 rounded-md border bg-background px-2 py-1 text-xs"
        data-testid={`${testIdPrefix}s-name-filter`}
      />
    </div>
  );

  if (isLoading) {
    return (
      <div className="p-4">
        {filterBar}
        <table className="w-full text-sm tabular-nums">
          {header}
          <tbody>
            <SkeletonTableRows columns={columnCount} />
          </tbody>
        </table>
      </div>
    );
  }

  if (rawRows.length === 0) {
    return <div className="p-4 text-sm text-muted-foreground">{emptyMessage}</div>;
  }

  return (
    <div className="p-4">
      {filterBar}
      {visibleRows.length === 0 ? (
        <div className="p-4 text-sm text-muted-foreground" data-testid={`${testIdPrefix}s-no-filter-match`}>
          No {testIdPrefix}s match "{nameFilter}".
        </div>
      ) : (
        /* `tabular-nums` plus a `w-full` Name column keeps the layout still while
           data refreshes: every other column is sized to its content, all slack
           lands in the name, and digits are equal width so a counter ticking from
           `9m` to `10m` (or a metric gaining a digit) no longer re-lays out the
           whole table under the pointer. */
        <table className="w-full text-sm tabular-nums">
          {header}
          <tbody data-testid={tableBodyTestId ?? `${testIdPrefix}s-table-body`}>
            {visibleRows.map((row) => {
              const rowKey = getKey(row);
              const isSelected = selectedKey === rowKey;
              return (
                <tr
                  key={rowKey}
                  data-testid={`${testIdPrefix}-row-${row.name}`}
                  className={`border-b last:border-0 ${
                    clickable ? "cursor-pointer hover:bg-accent/50" : "hover:bg-accent/30"
                  } ${isSelected ? "bg-accent" : ""} ${getRowClassName?.(row) ?? ""}`}
                  tabIndex={clickable ? 0 : undefined}
                  aria-selected={clickable ? isSelected : undefined}
                  onClick={() => onRowClick?.(row)}
                  onKeyDown={
                    onRowClick
                      ? (e) => {
                          if (e.key === "Enter" || e.key === " ") {
                            e.preventDefault();
                            onRowClick(row);
                          }
                        }
                      : undefined
                  }
                  onContextMenu={(e) => {
                    if (onRowContextMenu) {
                      e.preventDefault();
                      onRowContextMenu(e, row);
                    }
                  }}
                >
                  <td className="w-full py-2 pr-4 font-medium">{row.name}</td>
                  {isMulti && (
                    <td className="whitespace-nowrap py-2 pr-4 text-xs text-muted-foreground">
                      {row.namespace ?? "—"}
                    </td>
                  )}
                  {columns.map((col, i) => (
                    <td key={i} className={col.className ?? "whitespace-nowrap py-2 pr-4"}>
                      {col.cell(row)}
                    </td>
                  ))}
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </div>
  );
}

export const ResourceTable = memo(ResourceTableInner) as typeof ResourceTableInner;
