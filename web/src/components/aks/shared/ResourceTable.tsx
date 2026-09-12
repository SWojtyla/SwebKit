import { memo, type ReactNode, type MouseEvent, type JSX } from "react";
import { AlertCircle } from "lucide-react";
import { SkeletonTableRows } from "@/components/shared/Skeleton";

export interface Column<T> {
  header: ReactNode;
  cell: (row: T) => ReactNode;
  className?: string;
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
}: ResourceTableProps<T>): JSX.Element {
  const columnCount = 1 + (isMulti ? 1 : 0) + columns.length;

  // Only a real onRowClick makes a row clickable. A context-menu-only row must not carry the
  // same pointer-cursor/hover/keyboard-activation affordance as one with a left-click action —
  // that mismatch (every row "looked" clickable, most weren't) was the root cause of the
  // reported "have to right-click to do anything" bug.
  const clickable = Boolean(onRowClick);

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
        <th className="w-full py-2 pr-4">Name</th>
        {isMulti && <th className="whitespace-nowrap py-2 pr-4">Namespace</th>}
        {columns.map((col, i) => (
          <th key={i} className={col.className ?? "whitespace-nowrap py-2 pr-4"}>
            {col.header}
          </th>
        ))}
      </tr>
    </thead>
  );

  if (isLoading) {
    return (
      <div className="p-4">
        <table className="w-full text-sm tabular-nums">
          {header}
          <tbody>
            <SkeletonTableRows columns={columnCount} />
          </tbody>
        </table>
      </div>
    );
  }

  const rows = data ?? [];
  if (rows.length === 0) {
    return <div className="p-4 text-sm text-muted-foreground">{emptyMessage}</div>;
  }

  return (
    <div className="p-4">
      {/* `tabular-nums` plus a `w-full` Name column keeps the layout still while
          data refreshes: every other column is sized to its content, all slack
          lands in the name, and digits are equal width so a counter ticking from
          `9m` to `10m` (or a metric gaining a digit) no longer re-lays out the
          whole table under the pointer. */}
      <table className="w-full text-sm tabular-nums">
        {header}
        <tbody data-testid={tableBodyTestId ?? `${testIdPrefix}s-table-body`}>
          {rows.map((row) => {
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
    </div>
  );
}

export const ResourceTable = memo(ResourceTableInner) as typeof ResourceTableInner;
