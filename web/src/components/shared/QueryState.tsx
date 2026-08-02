import type { ReactNode } from "react";
import { AlertCircle } from "lucide-react";
import { SkeletonRows } from "./Skeleton";
import { EmptyState } from "./EmptyState";

interface QueryStateProps<T> {
  isLoading: boolean;
  error?: unknown;
  data: T[] | undefined;
  emptyTitle: string;
  emptyDescription?: string;
  children: (data: T[]) => ReactNode;
  skeletonRows?: number;
}

/**
 * Renders a consistent loading/error/empty/content sequence for a React Query list result,
 * replacing the `{isLoading && ...}; {error && ...}; {data.length === 0 && ...}` idiom that was
 * previously hand-written per query across Redis/Storage/AKS/Monitoring.
 */
export function QueryState<T>({
  isLoading,
  error,
  data,
  emptyTitle,
  emptyDescription,
  children,
  skeletonRows = 5,
}: QueryStateProps<T>) {
  if (isLoading) {
    return <SkeletonRows count={skeletonRows} />;
  }

  if (error) {
    return (
      <div className="flex items-center gap-2 p-4 text-sm text-destructive" data-testid="query-error">
        <AlertCircle className="h-4 w-4 shrink-0" />
        <span>{error instanceof Error ? error.message : String(error)}</span>
      </div>
    );
  }

  if (!data || data.length === 0) {
    return <EmptyState title={emptyTitle} description={emptyDescription} />;
  }

  return <>{children(data)}</>;
}
