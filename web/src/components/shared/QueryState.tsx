import type { ReactNode } from "react";
import { AlertCircle } from "lucide-react";
import { ApiError } from "@/lib/api/transport";
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
        const apiError = error instanceof ApiError ? error : null;
        return (
            <div
                className="p-4 text-sm text-destructive"
                data-testid="query-error"
            >
                <div className="flex items-center gap-2">
                    <AlertCircle className="h-4 w-4 shrink-0" />
                    {apiError?.kind && (
                        <span
                            className="shrink-0 rounded border border-destructive/40 px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide"
                            data-testid="query-error-kind"
                        >
                            {apiError.kind}
                        </span>
                    )}
                    <span>
                        {error instanceof Error ? error.message : String(error)}
                    </span>
                </div>
                {(apiError?.detail || apiError?.hint) && (
                    <details
                        className="mt-1.5 pl-6 text-xs"
                        data-testid="query-error-details"
                    >
                        <summary className="cursor-pointer text-muted-foreground hover:text-foreground">
                            Details
                        </summary>
                        {apiError.detail && (
                            <pre className="mt-1 whitespace-pre-wrap break-all font-mono text-muted-foreground">
                                {apiError.detail}
                            </pre>
                        )}
                        {apiError.hint && (
                            <p className="mt-1 text-muted-foreground">
                                Hint: {apiError.hint}
                            </p>
                        )}
                    </details>
                )}
            </div>
        );
    }

    if (!data || data.length === 0) {
        return <EmptyState title={emptyTitle} description={emptyDescription} />;
    }

    return <>{children(data)}</>;
}
