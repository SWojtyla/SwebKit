interface SkeletonProps {
  className?: string;
}

/** A single shimmering placeholder block. Compose with layout classes to match the content it replaces. */
function Skeleton({ className = "" }: SkeletonProps) {
  return <div className={`animate-pulse rounded bg-muted ${className}`} data-testid="skeleton" />;
}

/** A stack of row-shaped skeletons, for table/list loading states. */
export function SkeletonRows({ count = 5, className = "" }: { count?: number; className?: string }) {
  return (
    <div className={`space-y-2 p-4 ${className}`} data-testid="skeleton-rows">
      {Array.from({ length: count }).map((_, i) => (
        <Skeleton key={i} className="h-8 w-full" />
      ))}
    </div>
  );
}

/**
 * Skeleton rows for a real `<table>`, so a loading list keeps its column headers instead of
 * being replaced by an unrelated block (avoids the layout jump a bare "Loading..." text or a
 * generic `SkeletonRows` causes when it's swapped out for the real table on data arrival).
 * Use inside `<tbody>`, alongside real `<thead>` column headers rendered as usual.
 */
export function SkeletonTableRows({ columns, rows = 5 }: { columns: number; rows?: number }) {
  return (
    <>
      {Array.from({ length: rows }).map((_, r) => (
        <tr key={r} data-testid="skeleton-table-row">
          <td colSpan={columns} className="px-3 py-2">
            <Skeleton className="h-4 w-full" />
          </td>
        </tr>
      ))}
    </>
  );
}
