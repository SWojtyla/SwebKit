import { apiFetch, apiSend } from "./transport";

// ── Settings import/export ─────────────────────────────────────────────────────

export async function exportSettings(): Promise<unknown> {
    return apiFetch<unknown>("/api/config/export");
}

export async function importSettings(bundle: unknown): Promise<void> {
    return apiSend("/api/config/import", "POST", bundle);
}

// ── Team workspace pack ──────────────────────────────────────────────────────

/** A secret *reference* the imported pack needs re-linked — name only, never a value. */
export interface TeamPackCredentialRef {
    key: string;
    kind: "credentialStore" | "azureKeyVault" | "oauthToken" | string;
    hint?: string | null;
}

export interface TeamPackImportResult {
    dryRun: boolean;
    added: number;
    updated: number;
    skipped: number;
    conflicts: string[];
    requiredCredentialRefs: TeamPackCredentialRef[];
    warnings: string[];
}

export async function exportTeamPack(): Promise<unknown> {
    return apiFetch<unknown>("/api/config/team-pack/export");
}

export async function importTeamPack(
    pack: unknown,
    opts: { dryRun?: boolean; strategy?: "merge" | "replace" } = {},
): Promise<TeamPackImportResult> {
    const params = new URLSearchParams();
    if (opts.dryRun) params.set("dryRun", "true");
    if (opts.strategy && opts.strategy !== "merge") params.set("strategy", opts.strategy);
    const qs = params.toString();
    return apiSend<TeamPackImportResult>(
        `/api/config/team-pack/import${qs ? `?${qs}` : ""}`,
        "POST",
        pack,
    );
}
