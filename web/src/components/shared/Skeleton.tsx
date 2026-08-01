interface SkeletonProps {
  className?: string;
}

/** A single shimmering placeholder block. Compose with layout classes to match the content it replaces. */
export function Skeleton({ className = "" }: SkeletonProps) {
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
