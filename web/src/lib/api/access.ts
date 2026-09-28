import { apiFetch, apiSend } from "./transport";
import type {
    AccessReport,
    AccessReportEntry,
    AccessRequestArtifact,
    AccessRequestArtifactInput,
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
