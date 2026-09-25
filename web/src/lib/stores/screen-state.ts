import { useEffect, useEffectEvent } from "react";
import { postScreenState } from "../api";

/**
 * Screen-state publisher (agent-workspace-awareness Module 1): feature pages register a
 * serializer that emits a bounded snapshot of what's rendered — selection plus the visible
 * slice of already-fetched data. The sidecar holds it latest-wins (ScreenStateStore) and the
 * agent pulls it on demand via the get_screen_state tool.
 *
 * Design notes:
 * - Newest-registered provider wins; unmounting it falls back to the provider underneath (a
 *   page under a detail panel keeps its snapshot rather than dropping to route-only).
 * - Serializers WHITELIST fields — never dump a whole query-cache entry (decisions.md D5:
 *   no auth headers, tokens, connection strings, or full message bodies).
 * - Publish triggers: dep changes (debounced) + a heartbeat while a provider is active so the
 *   sidecar TTL sees a live publisher; a dead webview's snapshot expires on its own.
 */

export interface ScreenStateSnapshotPayload {
  route: string;
  featureArea?: string;
  capturedAt: string;
  snapshot: unknown;
}

export type ScreenStateProvider = () => { featureArea?: string; snapshot: unknown } | null;

const HEARTBEAT_MS = 60_000;
const PUBLISH_DEBOUNCE_MS = 1_500;

/** Registered providers, keyed by id. The newest registration wins while several are mounted at
 * once (a detail panel on top of its page); unmounting the top one falls back to the provider
 * underneath rather than to route-only state. */
const providers = new Map<string, { fn: ScreenStateProvider; order: number }>();
let orderCounter = 0;
let heartbeat: ReturnType<typeof setInterval> | null = null;
let debounce: ReturnType<typeof setTimeout> | null = null;
let lastPublishedJson = "";

function activeProvider(): { id: string; fn: ScreenStateProvider } | null {
  let best: { id: string; fn: ScreenStateProvider; order: number } | null = null;
  for (const [id, p] of providers) {
    if (!best || p.order > best.order) best = { id, ...p };
  }
  return best;
}

function currentRoute(): string {
  return window.location.pathname;
}

async function publish() {
  const built = activeProvider()?.fn() ?? { featureArea: undefined, snapshot: { title: document.title } };
  if (built === null) return;
  const payload: ScreenStateSnapshotPayload = {
    route: currentRoute(),
    featureArea: built.featureArea,
    capturedAt: new Date().toISOString(),
    snapshot: built.snapshot,
  };
  // Skip identical republishes — the heartbeat shouldn't POST unchanged snapshots every minute.
  const json = JSON.stringify(payload.snapshot);
  if (json === lastPublishedJson) return;
  lastPublishedJson = json;
  try {
    await postScreenState(payload);
  } catch {
    // Best-effort by design — a failed publish must never surface to the user.
    lastPublishedJson = "";
  }
}

function schedulePublish() {
  if (debounce) clearTimeout(debounce);
  debounce = setTimeout(() => void publish(), PUBLISH_DEBOUNCE_MS);
}

function ensureHeartbeat() {
  if (heartbeat) return;
  heartbeat = setInterval(() => void publish(), HEARTBEAT_MS);
}

/**
 * Route-change trigger for the no-provider fallback: pages that register no serializer (the
 * dashboard, settings, /agent itself) still publish a route+title snapshot so the agent knows
 * where the user navigated. Called from AppLayout's location effect.
 */
export function notifyScreenRouteChanged() {
  // Force the next publish even if the snapshot JSON is identical — the route changed.
  lastPublishedJson = "";
  schedulePublish();
}

/**
 * Registers a screen-state provider for the calling component's lifetime. `build` runs at
 * publish time (not at render) so it always sees the latest closure values — pass the reactive
 * inputs it reads in `deps` to trigger a debounced republish when they change.
 */
export function useScreenStateProvider(
  id: string,
  featureArea: string | undefined,
  build: () => unknown,
  deps: readonly unknown[],
) {
  const buildEvent = useEffectEvent(build);

  useEffect(() => {
    providers.set(id, {
      fn: () => {
        const snapshot = buildEvent();
        return snapshot == null ? null : { featureArea, snapshot };
      },
      order: ++orderCounter,
    });
    ensureHeartbeat();
    schedulePublish();
    return () => {
      providers.delete(id);
      lastPublishedJson = "";
      // Republish so the provider underneath (or the route-only fallback) takes over promptly
      // instead of leaving this screen's data parked until it expires.
      schedulePublish();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  // Dep-driven republish — debounced, and skipped entirely when the snapshot JSON is unchanged.
  useEffect(() => {
    if (activeProvider()?.id === id) schedulePublish();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);
}
