/// Query-key helpers for the AKS workspace.
///
/// TanStack Query matches an `invalidateQueries` key by comparing array *elements*,
/// not by string prefix. `{ queryKey: ["aks-"] }` therefore matches only a query
/// keyed exactly `["aks-"]` — and every AKS query is keyed `["aks-pods", ns]`,
/// `["aks-deployments", ns]`, and so on, so that literal matched nothing and both
/// the Refresh button and the auto-refresh timer were silent no-ops. Refreshing
/// "everything AKS" has to go through a predicate; keeping it here makes it
/// unit-testable and stops the broken literal creeping back.

import type { QueryClient } from "@tanstack/react-query";

export const AKS_KEY_PREFIX = "aks-";

/**
 * Cluster-level queries that are not per-namespace resources. `aks-namespaces` in
 * particular is by far the slowest AKS endpoint (~18s cold on a large cluster), so
 * it is excluded from the "is a refresh in flight?" signal — otherwise the toolbar
 * spinner would be pinned on for most of a session.
 */
const BOOTSTRAP_KEYS = new Set(["aks-namespaces", "aks-contexts", "aks-test"]);

export function isAksQueryKey(queryKey: readonly unknown[]): boolean {
  const head = queryKey[0];
  return typeof head === "string" && head.startsWith(AKS_KEY_PREFIX);
}

/** True for the namespace-scoped resource queries a refresh should visibly report on. */
export function isAksResourceQueryKey(queryKey: readonly unknown[]): boolean {
  const head = queryKey[0];
  return typeof head === "string" && head.startsWith(AKS_KEY_PREFIX) && !BOOTSTRAP_KEYS.has(head);
}

/**
 * Invalidates the namespaced resource queries — what "refresh the view" means.
 *
 * Deliberately not the bootstrap queries: `aks-namespaces` is a cluster-scoped
 * call that can take ~18s on a large cluster, and putting it on a 10-second timer
 * would keep one of the browser's six connections to the sidecar permanently
 * occupied for a list that almost never changes.
 *
 * Active queries refetch immediately; inactive ones are marked stale so they
 * refetch when their tab is next opened.
 */
export function invalidateAksResourceQueries(queryClient: QueryClient): Promise<void> {
  return queryClient.invalidateQueries({ predicate: (query) => isAksResourceQueryKey(query.queryKey) });
}

/**
 * Invalidates every AKS query, cluster-scoped ones included. For explicit user
 * actions — the Refresh button, applying edited YAML — where picking up a brand
 * new namespace is worth the cost.
 */
export function invalidateAksQueries(queryClient: QueryClient): Promise<void> {
  return queryClient.invalidateQueries({ predicate: (query) => isAksQueryKey(query.queryKey) });
}
