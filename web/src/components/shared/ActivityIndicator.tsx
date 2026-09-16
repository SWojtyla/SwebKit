import { useIsFetching, useIsMutating } from "@tanstack/react-query";
import { LoaderCircle } from "lucide-react";

export function ActivityIndicator() {
  const fetching = useIsFetching();
  const mutating = useIsMutating();
  if (fetching === 0 && mutating === 0) return null;

  return (
    <div
      className="flex items-center gap-1.5 whitespace-nowrap text-xs text-muted-foreground"
      data-testid="global-activity-indicator"
      role="status"
      aria-live="polite"
    >
      <LoaderCircle className="h-3.5 w-3.5 animate-spin" />
      {mutating > 0 ? "Saving…" : "Working…"}
    </div>
  );
}
