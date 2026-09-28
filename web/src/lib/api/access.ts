import { apiFetch, apiSend } from "./transport";
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

/** `GET /api/access/report`. `refresh` re-probes every capability instead of serving
 * the sidecar's 5-minute cache — that's the "Refresh" button's path. The param is
 * always sent: the endpoint binds a non-nullable `bool`, so omitting it is a 400. */
export async function fetchAccessReport(refresh = false, signal?: AbortSignal): Promise<AccessReport> {
    return apiFetch<AccessReport>(`/api/access/report?refresh=${refresh}`, { signal });
}

/** Re-probes one connection's rows — cheaper than a full refresh. */
export async function refreshAccessEntry(
    featureArea: string,
    connectionKey: string,
): Promise<AccessReportEntry> {
    return apiFetch<AccessReportEntry>(
        `/api/access/report/${encodeURIComponent(featureArea)}/${encodeURIComponent(connectionKey)}`,
    );
}

/** Builds the copyable access-request artifact for one denied row (Phase 3a). */
export async function requestAccessArtifact(
    input: AccessRequestArtifactInput,
): Promise<AccessRequestArtifact> {
    return apiSend<AccessRequestArtifact>("/api/access/request", "POST", input);
}

// ── Phase 4 — request webhook config + send ──────────────────────────────────

/** The webhook config view — never carries the trigger URL itself (credential store),
 * only whether one is stored. */
export async function fetchAccessWebhook(signal?: AbortSignal): Promise<AccessRequestWebhookConfig> {
    return apiFetch<AccessRequestWebhookConfig>("/api/access/webhook", { signal });
}

/** Saves webhook config; a non-empty `url` is written to the credential store,
 * `clearUrl` removes it, neither leaves it alone. */
export async function saveAccessWebhook(
    input: AccessRequestWebhookInput,
): Promise<AccessRequestWebhookConfig> {
    return apiSend<AccessRequestWebhookConfig>("/api/access/webhook", "PUT", input);
}

/** Renders the template and POSTs it to the configured webhook. The result is honest:
 * `sent` means the trigger accepted the call, not that access was granted. `dryRun`
 * renders without sending (the settings test path). */
export async function sendAccessRequest(
    input: AccessRequestSendInput,
): Promise<AccessRequestSendResult> {
    return apiSend<AccessRequestSendResult>("/api/access/request/send", "POST", input);
}
