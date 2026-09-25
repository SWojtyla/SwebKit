import { useEffect, useState } from "react";

/**
 * Current time as React state, refreshed on an interval. Reading `Date.now()` during render is
 * impure — React may re-render or abandon a render at any point, so a render-time read is both a
 * lint violation and a staleness bug (the value freezes until something else re-renders the
 * component). Keep the clock in state instead: every re-render derives from an explicit tick.
 *
 * The interval only runs while `enabled` — pass false (or null-ish conditions like "no timestamp
 * yet") so idle components don't wake up every second for a label nobody is looking at.
 */
export function useNow(intervalMs = 1000, enabled = true): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    if (!enabled) return;
    const id = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(id);
  }, [intervalMs, enabled]);

  return now;
}
