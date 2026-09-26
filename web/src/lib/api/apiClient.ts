import { apiFetch, apiSend } from "./transport";
import type { CollectionImportResult } from "../types";

export interface KeyVaultPreviewResult {
    status: "ok" | "error";
    maskedValue: string | null;
    error: string | null;
}

export async function previewKeyVaultSecret(
    keyVaultName: string | null,
    secretName: string,
): Promise<KeyVaultPreviewResult> {
    return apiSend<KeyVaultPreviewResult>(
        "/api/api-client/preview-keyvault-secret",
        "POST",
        {
            keyVaultName: keyVaultName || null,
            secretName,
        },
    );
}

// Environment-variable "Secret Store" values live in the OS credential store via the sidecar —
// the same store the executor resolves WindowsCredentialStore variables from at send time.
export async function saveCredential(
    key: string,
    secret: string,
): Promise<void> {
    await apiSend("/api/api-client/credentials", "POST", { key, secret });
}

export async function deleteCredential(key: string): Promise<void> {
    // Query param, not a route segment — a credential key containing '/' would 404 otherwise.
    await apiSend(
        `/api/api-client/credentials?key=${encodeURIComponent(key)}`,
        "DELETE",
    );
}

export async function previewCredential(
    key: string,
): Promise<KeyVaultPreviewResult> {
    return apiSend<KeyVaultPreviewResult>(
        "/api/api-client/preview-credential",
        "POST",
        { key },
    );
}

// ── OAuth 2.0 authorization-code + PKCE (loopback flow) ──────────────────────

export interface OAuth2AuthorizeRequest {
    authUrl: string;
    tokenUrl: string;
    clientId: string;
    /** Credential-store key resolving to the client secret — confidential clients only. */
    credentialKey?: string | null;
    scopes?: string | null;
}

export interface OAuth2AuthorizeResult {
    transactionId: string;
    authorizeUrl: string;
}

export interface OAuth2FlowResult {
    status: "pending" | "done" | "error" | "expired";
    credentialKey: string | null;
    error: string | null;
}

export async function startOAuth2Authorize(
    req: OAuth2AuthorizeRequest,
): Promise<OAuth2AuthorizeResult> {
    return apiSend<OAuth2AuthorizeResult>(
        "/api/api-client/oauth/authorize",
        "POST",
        req,
    );
}

export async function getOAuth2Result(
    transactionId: string,
): Promise<OAuth2FlowResult> {
    return apiFetch<OAuth2FlowResult>(
        `/api/api-client/oauth/result/${transactionId}`,
    );
}

export async function importCollection(payload: {
    folderPath?: string | null;
    payloadBase64?: string | null;
}): Promise<CollectionImportResult> {
    return apiSend<CollectionImportResult>(
        "/api/config/collections/import",
        "POST",
        payload,
    );
}

export async function evaluateJsonPath(
    body: string,
    jsonPath: string,
): Promise<{ value: string | null; error: string | null }> {
    return apiSend<{ value: string | null; error: string | null }>(
        "/api/api-client/evaluate-jsonpath",
        "POST",
        {
            body,
            jsonPath,
        },
    );
}
