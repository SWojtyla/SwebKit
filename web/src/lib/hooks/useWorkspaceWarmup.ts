import { useEffect, useRef } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "../api";
import { loadViewPreference } from "../stores/panel-preferences";
import { useDemoMode, useProfile, useUserSettings } from "./useProfile";
import type { DeploymentInfo, KubeContextInfo, SbEntityInfo } from "../types";

const TOPOLOGY_STALE_TIME = 5 * 60_000;

/**
 * App-startup data warm-up, mounted once in `AppLayout` so it fires on every launch
 * regardless of which route the workspace restores into — the dashboard's own queries
 * only warm things when the dashboard happens to be the landing page.
 *
 * Covers the first-entity topology nothing else fetches: the AKS context list, the
 * ~18s namespace list (now server-cached 5 min per context), the namespace the
 * workspace will restore into, and Service Bus queues/topics for the first
 * namespace. The footer status bar already warms the per-area *health* probes
 * (aks-test, sb-test, redis info, sql test, storage containers), so those are
 * deliberately not duplicated here. Selection-dependent data (Redis key SCANs,
 * blob listings, pods) can't be warmed — there's no deterministic target.
 *
 * Query keys and URLs mirror `useAks`/`useServiceBus` exactly — the prefetched
 * cache entries must be the same ones the pages' own queries would create.
 */
export function useWorkspaceWarmup() {
  const qc = useQueryClient();
  const { data: profile } = useProfile();
  const { data: demoMode } = useDemoMode();
  const { data: settings } = useUserSettings();
  const firedRef = useRef(false);

  useEffect(() => {
    // One-shot per session: waits until all three inputs resolve, then never
    // re-runs — the queries' own staleTime governs any later refresh.
    if (firedRef.current || !profile || !demoMode || !settings) return;
    firedRef.current = true;
    if (settings.warmupConnectionsOnStartup === false) return;

    const isDemo = demoMode.isDemoMode;

    if (isDemo || profile.config.aksConfig != null) {
      const ctx = profile.config.aksConfig?.kubeconfigContext ?? "default";
      void qc.prefetchQuery({
        queryKey: ["aks-contexts"],
        queryFn: ({ signal }) => apiFetch<KubeContextInfo[]>("/api/aks/contexts", { signal }),
        staleTime: TOPOLOGY_STALE_TIME,
      });
      void qc.prefetchQuery({
        queryKey: ["aks-namespaces", ctx],
        queryFn: ({ signal }) => apiFetch<string[]>("/api/aks/namespaces", { signal }),
        staleTime: TOPOLOGY_STALE_TIME,
      });

      // Prefetch deployments for the namespace the AKS workspace will restore
      // into — the configured default wins, then the persisted per-context pick.
      const configuredNs = profile.config.aksConfig?.defaultNamespace?.trim() || null;
      const persisted = loadViewPreference<string[]>(`aks-selected-ns:${ctx}`, []);
      const ns = configuredNs ?? (persisted.length === 1 && persisted[0] !== "*" ? persisted[0] : null);
      if (ns) {
        void qc.prefetchQuery({
          queryKey: ["aks-deployments", ctx, ns],
          queryFn: ({ signal }) => apiFetch<DeploymentInfo[]>(`/api/aks/${ns}/deployments`, { signal }),
        });
      }
    }

    const firstSbNs = profile.serviceBusNamespaces[0]?.id;
    if (firstSbNs) {
      void qc.prefetchQuery({
        queryKey: ["sb-queues", firstSbNs],
        queryFn: ({ signal }) => apiFetch<SbEntityInfo[]>(`/api/servicebus/${firstSbNs}/queues`, { signal }),
        staleTime: TOPOLOGY_STALE_TIME,
      });
      void qc.prefetchQuery({
        queryKey: ["sb-topics", firstSbNs],
        queryFn: ({ signal }) => apiFetch<SbEntityInfo[]>(`/api/servicebus/${firstSbNs}/topics`, { signal }),
        staleTime: TOPOLOGY_STALE_TIME,
      });
    }
  }, [profile, demoMode, settings, qc]);
}
