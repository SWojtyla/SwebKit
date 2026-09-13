import type { RequestTab } from "@/components/api-client/RequestTabStrip";

/**
 * Picks the tab that should become active after closing `closingId`: the tab
 * that slides into its slot, or the previous one if it was last. `null` when
 * none remain — the only case a blank editor is actually correct.
 */
export function pickNeighborTabId(tabs: RequestTab[], closingId: string): string | null {
  const idx = tabs.findIndex((t) => t.id === closingId);
  if (idx === -1) return null;
  const remaining = tabs.filter((t) => t.id !== closingId);
  if (remaining.length === 0) return null;
  const neighborIndex = Math.min(idx, remaining.length - 1);
  return remaining[neighborIndex].id;
}
