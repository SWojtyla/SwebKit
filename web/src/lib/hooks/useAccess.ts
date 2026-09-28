import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
    fetchAccessReport,
    fetchAccessWebhook,
    refreshAccessEntry,
    requestAccessArtifact,
    saveAccessWebhook,
    sendAccessRequest,
} from "../api";
import { useNotification } from "@/components/layout/notification-context";
import type {
    AccessReport,
    AccessReportEntry,
    AccessRequestArtifact,
    AccessRequestArtifactInput,
    AccessRequestSendInput,
    AccessRequestSendResult,
    AccessRequestWebhookConfig,
    AccessRequestWebhookInput,
} from "../types";

// ── Access-awareness report + request artifacts ──────────────────────────────
// Query key root: ["access", "report"] — demo-mode/profile changes elsewhere
// invalidate broadly (qc.invalidateQueries()) which already covers this.

/** The per-environment access report — one probe row per capability per connection.
 * The sidecar caches probes for 5 minutes; this is a plain fetch of that cache. */
export function useAccessReport() {
    return useQuery({
        queryKey: ["access", "report"],
        queryFn: ({ signal }) => fetchAccessReport(false, signal),
        retry: false,
    });
}

/** Force-refresh: re-probes every capability (`?refresh=true`) and writes the fresh
 * report into the cache — a mutation rather than refetch so the button can show pending. */
export function useAccessReportRefresh() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: () => fetchAccessReport(true),
        onSuccess: (report) => qc.setQueryData(["access", "report"], report),
        onError: (error) =>
            notify("error", "Couldn't refresh access report", String(error)),
    });
}

/** Re-probes one connection's rows and splices the returned entry into the cached report. */
export function useAccessEntryRefresh() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation({
        mutationFn: (entry: Pick<AccessReportEntry, "featureArea" | "connectionKey">) =>
            refreshAccessEntry(entry.featureArea, entry.connectionKey),
        onSuccess: (fresh) => {
            qc.setQueryData<AccessReport>(["access", "report"], (prev) =>
                prev
                    ? {
                          ...prev,
                          entries: prev.entries.map((e) =>
                              e.featureArea === fresh.featureArea &&
                              e.connectionKey === fresh.connectionKey
                                  ? fresh
                                  : e,
                          ),
                      }
                    : prev,
            );
        },
        onError: (error) =>
            notify("error", "Couldn't refresh entry", String(error)),
    });
}

/** POST /api/access/request — builds the copyable artifact for one denied row. */
export function useAccessRequestArtifact() {
    const { notify } = useNotification();
    return useMutation<AccessRequestArtifact, Error, AccessRequestArtifactInput>({
        mutationFn: requestAccessArtifact,
        onError: (error) =>
            notify("error", "Couldn't build access request", String(error)),
    });
}

// ── Phase 4 — request webhook ────────────────────────────────────────────────

/** The webhook config view for the Access settings card. `hasUrl` (not `enabled`)
 * tells whether a trigger URL is actually stored in the OS credential store. */
export function useAccessWebhook() {
    return useQuery({
        queryKey: ["access", "webhook"],
        queryFn: ({ signal }) => fetchAccessWebhook(signal),
        retry: false,
    });
}

/** Saves webhook config and writes the returned view straight into the cache — the
 * response *is* the new state, so no refetch round-trip. */
export function useSaveAccessWebhook() {
    const qc = useQueryClient();
    const { notify } = useNotification();
    return useMutation<AccessRequestWebhookConfig, Error, AccessRequestWebhookInput>({
        mutationFn: saveAccessWebhook,
        onSuccess: (view) => qc.setQueryData(["access", "webhook"], view),
        onError: (error) =>
            notify("error", "Couldn't save webhook settings", String(error)),
    });
}

/** POST /api/access/request/send — the result object carries the honest outcome
 * (sent/failed + status); a rejected promise means the endpoint itself failed. */
export function useSendAccessRequest() {
    const { notify } = useNotification();
    return useMutation<AccessRequestSendResult, Error, AccessRequestSendInput>({
        mutationFn: sendAccessRequest,
        onError: (error) =>
            notify("error", "Couldn't send access request", String(error)),
    });
}
