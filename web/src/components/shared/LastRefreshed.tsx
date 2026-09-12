import { useEffect, useState } from "react";

interface LastRefreshedProps {
  at: number | null;
  isFetching: boolean;
  paused?: boolean;
  /** Tooltip shown while `paused` is true. Defaults to a generic explanation. */
  pausedReason?: string;
  testId?: string;
}

/**
 * "Updated 12s ago" next to a refresh control. Without it, auto-refresh is invisible — the
 * data usually looks identical between ticks, so there was no way to tell a working refresh
 * from a broken one. Originally built for AKS (see `lib/aks-query-keys.ts`'s query-key-matching
 * pitfall); extracted here so every polled feature area can show the same freshness signal.
 *
 * Fixed-width and `tabular-nums` so the counter ticking does not nudge neighboring controls.
 */
export function LastRefreshed({
  at,
  isFetching,
  paused = false,
  pausedReason = "Auto-refresh is currently paused",
  testId = "last-refreshed",
}: LastRefreshedProps) {
  const [, setTick] = useState(0);

  useEffect(() => {
    if (at === null) return;
    const id = setInterval(() => setTick((t) => t + 1), 1000);
    return () => clearInterval(id);
  }, [at]);

  const label = (() => {
    if (isFetching) return "refreshing…";
    if (paused) return "auto paused";
    if (at === null) return "";
    const seconds = Math.max(0, Math.round((Date.now() - at) / 1000));
    if (seconds < 60) return `updated ${seconds}s ago`;
    return `updated ${Math.floor(seconds / 60)}m ago`;
  })();

  return (
    <span
      className="w-[7.5rem] shrink-0 truncate text-right text-xs tabular-nums text-muted-foreground"
      title={paused ? pausedReason : label || undefined}
      data-testid={testId}
    >
      {label}
    </span>
  );
}
