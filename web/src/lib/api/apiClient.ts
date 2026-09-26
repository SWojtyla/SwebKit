import { apiSend } from "./transport";
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
