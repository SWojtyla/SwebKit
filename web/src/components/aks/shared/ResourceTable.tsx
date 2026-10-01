import {
    memo,
    useMemo,
    useRef,
    useState,
    type ReactNode,
    type MouseEvent,
    type JSX,
} from "react";
import {
    AlertCircle,
    ArrowDown,
    ArrowUp,
    ArrowUpDown,
    Search,
} from "lucide-react";
import { SkeletonTableRows } from "@/components/shared/Skeleton";
import { useGridKeyboardNav } from "@/lib/hooks/useGridKeyboardNav";
import { extractSortText } from "@/lib/sort-text";

export interface Column<T> {
    header: ReactNode;
    cell: (row: T) => ReactNode;
    className?: string;
    /** Explicit sort key. When omitted the rendered cell's text is used, so every column sorts
     * by default; supply this only when the visible text would order wrong (e.g. status
     * severity, durations that need a canonical value). */
    sortValue?: (row: T) => string | number;
    /** Set false for columns with no meaningful order — action buttons, icon-only cells. */
    sortable?: boolean;
}

export interface ResourceTableProps<
    T extends { name: string; namespace?: string; context?: string },
> {
    data?: T[];
    isLoading: boolean;
    /** Distinct from "no items" — an RBAC-denied list or a sidecar error must not render as empty. */
    error?: unknown;
    isMulti?: boolean;
    /** Merged multi-cluster view: adds the Context column so a row's origin is visible. */
    showContext?: boolean;
    /** Per-context failures from the scoped fan-out — rendered as a warning banner above
     * the table so one unreachable cluster never blanks the others' rows. */
    contextErrors?: { context: string; error: unknown }[];
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
    defaultSort?: {
        sortValue: (row: T) => string | number;
        direction?: "asc" | "desc";
    };
    /** Set to hide the built-in name filter for a tab where it wouldn't make sense. */
    hideNameFilter?: boolean;
    /**
     * For tabs with few, small columns. The default `w-full` table smears leftover width
     * across every column, which on a sparse table reads as disconnected fragments (and a
     * `w-full` column to absorb it explodes against `min-w-max` into a 2×-wide table).
     * Compact mode drops `w-full` so the table hugs its content and whitespace sits after it.
     */
    compact?: boolean;
}

type SortKey = "name" | "namespace" | "context" | number;
type SortState = { key: SortKey; direction: "asc" | "desc" } | null;

function compareValues(a: string | number, b: string | number): number {
    if (typeof a === "number" && typeof b === "number") return a - b;
    return String(a).localeCompare(String(b), undefined, {
        numeric: true,
        sensitivity: "base",
    });
}

function ResourceTableInner<
    T extends { name: string; namespace?: string; context?: string },
>({
    data,
    isLoading,
    error,
    isMulti,
    showContext,
    contextErrors,
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
    compact,
}: ResourceTableProps<T>): JSX.Element {
    const [nameFilter, setNameFilter] = useState("");
    const [sort, setSort] = useState<SortState>(null);

    const columnCount =
        1 + (isMulti ? 1 : 0) + (showContext ? 1 : 0) + columns.length;

    // Only a real onRowClick makes a row clickable. A context-menu-only row must not carry the
    // same pointer-cursor/hover/keyboard-activation affordance as one with a left-click action —
    // that mismatch (every row "looked" clickable, most weren't) was the root cause of the
    // reported "have to right-click to do anything" bug.
    const clickable = Boolean(onRowClick);

    const rawRows = useMemo(() => data ?? [], [data]);

    const visibleRows = useMemo(() => {
        const filtered = nameFilter.trim()
            ? rawRows.filter((row) =>
                  row.name
                      .toLowerCase()
                      .includes(nameFilter.trim().toLowerCase()),
              )
            : rawRows;

        const activeSort =
            sort ??
            (defaultSort
                ? {
                      key: "name" as SortKey,
                      direction: defaultSort.direction ?? "asc",
                  }
                : null);
        const sortValueFor = (() => {
            if (!sort) return defaultSort?.sortValue;
            if (sort.key === "name") return (row: T) => row.name;
            if (sort.key === "namespace")
                return (row: T) => row.namespace ?? "";
            if (sort.key === "context") return (row: T) => row.context ?? "";
            const col = columns[sort.key as number];
            if (!col || col.sortable === false) return undefined;
            return (
                col.sortValue ?? ((row: T) => extractSortText(col.cell(row)))
            );
        })();

        if (!activeSort || !sortValueFor) return filtered;
        // Keys are computed once per row — an extracted cell key would otherwise re-render
        // the cell on every comparison.
        const keyed = filtered.map((row) => ({
            row,
            key: sortValueFor(row),
        }));
        keyed.sort((a, b) => compareValues(a.key, b.key));
        if (activeSort.direction === "desc") keyed.reverse();
        return keyed.map((entry) => entry.row);
    }, [rawRows, nameFilter, sort, defaultSort, columns]);

    const toggleSort = (key: SortKey) => {
        setSort((prev) => {
            if (!prev || prev.key !== key) return { key, direction: "asc" };
            if (prev.direction === "asc") return { key, direction: "desc" };
            return null;
        });
    };

    const sortIcon = (key: SortKey) => {
        if (!sort || sort.key !== key)
            return <ArrowUpDown className="h-3 w-3 opacity-40" />;
        return sort.direction === "asc" ? (
            <ArrowUp className="h-3 w-3" />
        ) : (
            <ArrowDown className="h-3 w-3" />
        );
    };

    // Grid keyboard nav (ux-power-pack §4): j/k/arrows move a focused row,
    // e/Enter (and Space, kept for parity with the old row handler) run the
    // row's click action, `/` focuses the name filter, g/G jump first/last.
    // Rows carry `data-grid-nav-row` + a roving tabIndex; editable targets and
    // focused buttons/links are guarded inside the hook.
    const containerRef = useRef<HTMLDivElement | null>(null);
    const filterInputRef = useRef<HTMLInputElement | null>(null);
    const gridNav = useGridKeyboardNav({
        containerRef,
        itemCount: visibleRows.length,
        resetKey: data,
        onInspect: (index) => {
            const row = visibleRows[index];
            if (row !== undefined) onRowClick?.(row);
        },
        onToggleSelect: (index) => {
            const row = visibleRows[index];
            if (row !== undefined) onRowClick?.(row);
        },
        getFilterInput: () => filterInputRef.current,
    });

    if (error) {
        return (
            <div
                className="flex items-center gap-2 p-4 text-sm text-destructive"
                data-testid={`${testIdPrefix}s-error`}
            >
                <AlertCircle className="h-4 w-4 shrink-0" />
                <span>
                    {error instanceof Error ? error.message : String(error)}
                </span>
            </div>
        );
    }

    const getKey =
        keyExtractor ??
        ((row: T) =>
            `${row.context ? `${row.context}:` : ""}${row.namespace ? `${row.namespace}/` : ""}${row.name}`);

    const header = (
        <thead>
            <tr className="border-b text-left text-xs text-muted-foreground">
                <th className="max-w-[320px] py-2 pr-4">
                    <button
                        onClick={() => toggleSort("name")}
                        className="flex items-center gap-1 hover:text-foreground"
                        data-testid={`${testIdPrefix}s-sort-name`}
                    >
                        Name {sortIcon("name")}
                    </button>
                </th>
                {isMulti && (
                    <th className="whitespace-nowrap py-2 pr-4">
                        <button
                            onClick={() => toggleSort("namespace")}
                            className="flex items-center gap-1 hover:text-foreground"
                            data-testid={`${testIdPrefix}s-sort-namespace`}
                        >
                            Namespace {sortIcon("namespace")}
                        </button>
                    </th>
                )}
                {showContext && (
                    <th className="whitespace-nowrap py-2 pr-4">
                        <button
                            onClick={() => toggleSort("context")}
                            className="flex items-center gap-1 hover:text-foreground"
                            data-testid={`${testIdPrefix}s-sort-context`}
                        >
                            Context {sortIcon("context")}
                        </button>
                    </th>
                )}
                {columns.map((col, i) => (
                    <th
                        key={i}
                        className={
                            col.className ?? "whitespace-nowrap py-2 pr-4"
                        }
                    >
                        {col.sortable === false ? (
                            col.header
                        ) : (
                            <button
                                onClick={() => toggleSort(i)}
                                className="flex items-center gap-1 hover:text-foreground"
                                data-testid={`${testIdPrefix}s-sort-${i}`}
                            >
                                {col.header} {sortIcon(i)}
                            </button>
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
                ref={filterInputRef}
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
                <div className="overflow-x-auto">
                    <table className="w-full text-sm tabular-nums">
                        {header}
                        <tbody>
                            <SkeletonTableRows columns={columnCount} />
                        </tbody>
                    </table>
                </div>
            </div>
        );
    }

    if (rawRows.length === 0) {
        return (
            <div className="p-4 text-sm text-muted-foreground">
                {emptyMessage}
            </div>
        );
    }

    return (
        <div className="p-4" ref={containerRef}>
            {filterBar}
            {contextErrors && contextErrors.length > 0 && (
                <div
                    className="mb-2 flex flex-col gap-1"
                    data-testid={`${testIdPrefix}s-context-errors`}
                >
                    {contextErrors.map((e) => (
                        <div
                            key={e.context}
                            className="flex items-center gap-2 rounded border border-destructive/30 bg-destructive/10 px-2 py-1.5 text-xs text-destructive"
                            data-testid={`${testIdPrefix}s-context-error-${e.context}`}
                        >
                            <AlertCircle className="h-3.5 w-3.5 shrink-0" />
                            <span>
                                {e.context}:{" "}
                                {e.error instanceof Error
                                    ? e.error.message
                                    : String(e.error)}
                            </span>
                        </div>
                    ))}
                </div>
            )}
            {visibleRows.length === 0 ? (
                <div
                    className="p-4 text-sm text-muted-foreground"
                    data-testid={`${testIdPrefix}s-no-filter-match`}
                >
                    No {testIdPrefix}s match "{nameFilter}".
                </div>
            ) : (
                /* `tabular-nums` plus a `w-full` Name column keeps the layout still while
           data refreshes: every other column is sized to its content, all slack
           lands in the name, and digits are equal width so a counter ticking from
           `9m` to `10m` (or a metric gaining a digit) no longer re-lays out the
           whole table under the pointer.

           The wrapping `overflow-x-auto` plus a real `min-width` on the table matters
           whenever a side detail panel is open and narrows the available space: without
           it, `w-full` forces the table to always fit its container, so `whitespace-nowrap`
           columns (Status, Restarts, Actions, ...) get silently crushed/clipped with no way
           to reach them, instead of the table overflowing into a scrollbar. */
                <div className="overflow-x-auto">
                    <table
                        className={`${compact ? "min-w-max" : "w-full min-w-max"} text-sm tabular-nums`}
                    >
                        {header}
                        <tbody
                            data-testid={
                                tableBodyTestId ?? `${testIdPrefix}s-table-body`
                            }
                        >
                            {visibleRows.map((row, rowIndex) => {
                                const rowKey = getKey(row);
                                const isSelected = selectedKey === rowKey;
                                const isFocused =
                                    gridNav.focusedIndex === rowIndex;
                                return (
                                    <tr
                                        key={rowKey}
                                        data-testid={`${testIdPrefix}-row-${row.name}`}
                                        data-grid-nav-row={rowIndex}
                                        className={`border-b last:border-0 outline-none ${
                                            clickable
                                                ? "cursor-pointer hover:bg-accent/50"
                                                : "hover:bg-accent/30"
                                        } ${isSelected ? "bg-accent" : ""} ${isFocused ? "bg-accent/70" : ""} ${getRowClassName?.(row) ?? ""}`}
                                        tabIndex={isFocused ? 0 : -1}
                                        onFocus={() =>
                                            gridNav.setFocusedIndex(rowIndex)
                                        }
                                        aria-selected={
                                            clickable ? isSelected : undefined
                                        }
                                        onClick={() => onRowClick?.(row)}
                                        onContextMenu={(e) => {
                                            if (onRowContextMenu) {
                                                e.preventDefault();
                                                onRowContextMenu(e, row);
                                            }
                                        }}
                                    >
                                        <td className="max-w-[320px] py-2 pr-4 font-medium">
                                            <span
                                                className="block truncate"
                                                title={row.name}
                                            >
                                                {row.name}
                                            </span>
                                        </td>
                                        {isMulti && (
                                            <td className="whitespace-nowrap py-2 pr-4 text-xs text-muted-foreground">
                                                {row.namespace ?? "—"}
                                            </td>
                                        )}
                                        {showContext && (
                                            <td
                                                className="whitespace-nowrap py-2 pr-4 text-xs text-muted-foreground"
                                                data-testid={`${testIdPrefix}-cell-context-${row.name}`}
                                            >
                                                {row.context ?? "—"}
                                            </td>
                                        )}
                                        {columns.map((col, i) => (
                                            <td
                                                key={i}
                                                className={
                                                    col.className ??
                                                    "whitespace-nowrap py-2 pr-4"
                                                }
                                            >
                                                {col.cell(row)}
                                            </td>
                                        ))}
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                </div>
            )}
        </div>
    );
}

export const ResourceTable = memo(
    ResourceTableInner,
) as typeof ResourceTableInner;
