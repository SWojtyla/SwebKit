import type { ReactNode, MouseEvent, JSX } from "react";

export interface Column<T> {
  header: ReactNode;
  cell: (row: T) => ReactNode;
  className?: string;
}

export interface ResourceTableProps<T extends { name: string; namespace?: string }> {
  data?: T[];
  isLoading: boolean;
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

export function ResourceTable<T extends { name: string; namespace?: string }>({
  data,
  isLoading,
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
  if (isLoading) {
    return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  }

  const rows = data ?? [];
  if (rows.length === 0) {
    return <div className="p-4 text-sm text-muted-foreground">{emptyMessage}</div>;
  }

  const getKey = keyExtractor ?? ((row: T) => (row.namespace ? `${row.namespace}/${row.name}` : row.name));

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            {isMulti && <th className="py-2 pr-4">Namespace</th>}
            {columns.map((col, i) => (
              <th key={i} className={col.className ?? "py-2 pr-4"}>
                {col.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody data-testid={tableBodyTestId ?? `${testIdPrefix}s-table-body`}>
          {rows.map((row) => {
            const rowKey = getKey(row);
            const isSelected = selectedKey === rowKey;
            const clickable = Boolean(onRowClick || onRowContextMenu);
            return (
              <tr
                key={rowKey}
                data-testid={`${testIdPrefix}-row-${row.name}`}
                className={`border-b last:border-0 ${
                  clickable ? "cursor-pointer hover:bg-accent/50" : "hover:bg-accent/30"
                } ${isSelected ? "bg-accent" : ""} ${getRowClassName?.(row) ?? ""}`}
                onClick={() => onRowClick?.(row)}
                onContextMenu={(e) => {
                  if (onRowContextMenu) {
                    e.preventDefault();
                    onRowContextMenu(e, row);
                  }
                }}
              >
                <td className="py-2 pr-4 font-medium">{row.name}</td>
                {isMulti && (
                  <td className="py-2 pr-4 text-xs text-muted-foreground">
                    {row.namespace ?? "—"}
                  </td>
                )}
                {columns.map((col, i) => (
                  <td key={i} className={col.className ?? "py-2 pr-4"}>
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
