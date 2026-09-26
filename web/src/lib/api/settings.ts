import { apiFetch, apiSend } from "./transport";

// ── Settings import/export ─────────────────────────────────────────────────────

export async function exportSettings(): Promise<unknown> {
    return apiFetch<unknown>("/api/config/export");
}

export async function importSettings(bundle: unknown): Promise<void> {
    return apiSend("/api/config/import", "POST", bundle);
}
