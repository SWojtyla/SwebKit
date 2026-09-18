import { useCallback } from "react";
import { useSearchParams } from "react-router";

/**
 * Returns a writer for URL search params that builds on the *live* URL rather than
 * the render-time `searchParams` snapshot. Two writes inside one React commit —
 * picking a namespace and immediately clicking a tab, say — otherwise both build
 * on the same stale base and the second silently drops the first's parameter.
 * Safe with `<BrowserRouter>`, which pushes to history synchronously, so
 * `window.location` already reflects the previous write.
 *
 * `null`/`undefined`/`""` values delete the key — pass `null` to clear downstream
 * params when an upstream selection changes.
 */
export function useUpdateSearchParams() {
  const [, setSearchParams] = useSearchParams();
  return useCallback(
    (
      updates: Record<string, string | null | undefined>,
      options?: { replace?: boolean },
    ) => {
      const next = new URLSearchParams(window.location.search);
      for (const [key, value] of Object.entries(updates)) {
        if (value === null || value === undefined || value === "")
          next.delete(key);
        else next.set(key, value);
      }
      setSearchParams(next, {
        replace: options?.replace ?? false,
        preventScrollReset: true,
      });
    },
    [setSearchParams],
  );
}
