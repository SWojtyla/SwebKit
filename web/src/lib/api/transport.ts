// Sidecar port is fixed (5199) only in dev, where the sidecar is started
// separately via `dotnet run`. In production Tauri lets the OS pick a free
// port and reports the real one via the `get_sidecar_port` command, so this
// must be re-resolved at startup (see `initSidecarBaseUrl`) before anything
// fetches — it can't be a one-shot module-load constant anymore.
import { getSidecarPort } from "../tauri-bridge";
import type {
    AgentChatContext,
    AgentChatMode,
    AgentChatScope,
    AgentStreamEvent,
} from "../types";

let SIDECAR_BASE_URL = (() => {
    const env = (
        import.meta as ImportMeta & { env?: Record<string, string | undefined> }
    ).env;
    return env?.VITE_SIDECAR_URL ?? "http://localhost:5199";
})();

/// Resolves the real sidecar port from Tauri (production: OS-assigned; dev:
/// fixed 5199) and updates `SIDECAR_BASE_URL` in place. No-op outside Tauri
/// (plain browser dev mode keeps the static default above). Must be awaited
/// before the app renders anything that calls `apiFetch`/`apiSend`.
export async function initSidecarBaseUrl(): Promise<void> {
    if (typeof window === "undefined" || !("__TAURI_INTERNALS__" in window)) {
        return;
    }
    const port = await getSidecarPort();
    if (port) {
        SIDECAR_BASE_URL = `http://127.0.0.1:${port}`;
    }
}

/**
 * Extracts a human-readable message from a failed API response body. ASP.NET error payloads vary
 * in shape: `{ error }` (custom BadRequest bodies), `{ detail }`/`{ title }` (ProblemDetails from
 * Results.Problem), or plain text — without this, callers surface the raw JSON blob to the user.
 */
function extractErrorMessage(
    status: number,
    statusText: string,
    body: string,
): string {
    if (body) {
        try {
            const parsed = JSON.parse(body);
            const message = parsed?.error ?? parsed?.detail ?? parsed?.title;
            if (typeof message === "string" && message.trim()) {
                return message;
            }
        } catch {
            // Not JSON — fall through to the raw body below.
        }
    }
    return body || statusText || `Request failed with status ${status}`;
}

/**
 * Pass TanStack Query's `signal` through as `apiFetch(url, { signal })` from every `queryFn`.
 *
 * Without it a superseded request — a retyped Redis filter, a different entity clicked, a page
 * navigated away from — keeps running to completion in the sidecar, holding its pooled client and
 * its backend round trips while nobody waits for the answer. ASP.NET binds the handlers'
 * `CancellationToken` to `HttpContext.RequestAborted`, and the Redis/Service Bus/Kubernetes clients
 * already thread it, so aborting here really does stop the work server-side.
 */
export async function apiFetch<T>(
    path: string,
    options?: RequestInit,
): Promise<T> {
    const res = await fetch(`${SIDECAR_BASE_URL}${path}`, {
        ...options,
        headers: {
            "Content-Type": "application/json",
            ...options?.headers,
        },
    });

    if (!res.ok) {
        const body = await res.text().catch(() => "");
        throw new Error(extractErrorMessage(res.status, res.statusText, body));
    }

    return res.json() as Promise<T>;
}

export async function apiSend<T>(
    path: string,
    method: "POST" | "PUT" | "PATCH" | "DELETE",
    body?: unknown,
    signal?: AbortSignal,
): Promise<T> {
    const res = await fetch(`${SIDECAR_BASE_URL}${path}`, {
        method,
        headers: { "Content-Type": "application/json" },
        body: body !== undefined ? JSON.stringify(body) : undefined,
        signal,
    });

    if (!res.ok) {
        const text = await res.text().catch(() => "");
        throw new Error(extractErrorMessage(res.status, res.statusText, text));
    }

    const text = await res.text().catch(() => "");
    return (text ? (JSON.parse(text) as T) : undefined) as T;
}

export interface StreamAgentChatBody {
    message: string;
    sessionId?: string;
    context?: AgentChatContext;
    mode?: AgentChatMode;
    scope?: AgentChatScope;
}

/** Publish payload for POST /api/agent/screen-state — see lib/stores/screen-state.ts. */
export interface ScreenStatePublishBody {
    route: string;
    featureArea?: string;
    capturedAt: string;
    snapshot: unknown;
}

export async function postScreenState(
    body: ScreenStatePublishBody,
): Promise<void> {
    return apiSend<void>("/api/agent/screen-state", "POST", body);
}

/**
 * Posts to the streaming agent chat endpoint and invokes `onEvent` for each
 * {@link AgentStreamEvent} as it arrives, in order — one call per SSE `data:` line.
 *
 * Browsers' built-in `EventSource` can only issue GET requests, and this endpoint needs a JSON
 * body (message/session/context/mode), so this reads the response body as a raw stream instead:
 * each chunk is decoded, buffered, and split on the SSE record separator (`\n\n`) so a `data:` line
 * split across two network chunks is still reassembled correctly before being parsed.
 *
 * Resolves once the stream ends (after a "done" or "error" event closes the response body) or
 * rejects if the initial request itself fails (non-2xx, or aborted before any bytes arrive).
 */
export async function streamAgentChat(
    body: StreamAgentChatBody,
    onEvent: (event: AgentStreamEvent) => void,
    signal?: AbortSignal,
): Promise<void> {
    const res = await fetch(`${SIDECAR_BASE_URL}/api/agent/chat/stream`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
        signal,
    });

    if (!res.ok || !res.body) {
        const text = await res.text().catch(() => "");
        throw new Error(extractErrorMessage(res.status, res.statusText, text));
    }

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";

    while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });

        let separatorIndex: number;
        while ((separatorIndex = buffer.indexOf("\n\n")) !== -1) {
            const record = buffer.slice(0, separatorIndex);
            buffer = buffer.slice(separatorIndex + 2);

            const dataLine = record
                .split("\n")
                .find((line) => line.startsWith("data:"));
            if (!dataLine) continue;
            const json = dataLine.slice("data:".length).trim();
            if (!json) continue;
            onEvent(JSON.parse(json) as AgentStreamEvent);
        }
    }
}

export function apiUpload<T>(
    path: string,
    file: File,
    onProgress?: (percent: number) => void,
): Promise<T> {
    return new Promise((resolve, reject) => {
        const request = new XMLHttpRequest();
        request.open("POST", `${SIDECAR_BASE_URL}${path}`);
        request.upload.onprogress = (event) => {
            if (event.lengthComputable) {
                onProgress?.(Math.round((event.loaded / event.total) * 100));
            }
        };
        request.onerror = () => reject(new Error("Upload failed"));
        request.onload = () => {
            const body = request.responseText || "";
            if (request.status < 200 || request.status >= 300) {
                reject(
                    new Error(
                        `API ${request.status}: ${body || request.statusText}`,
                    ),
                );
                return;
            }

            try {
                resolve((body ? JSON.parse(body) : undefined) as T);
            } catch {
                reject(new Error("Upload returned invalid JSON"));
            }
        };

        const form = new FormData();
        form.append("file", file, file.name);
        request.send(form);
    });
}

export { SIDECAR_BASE_URL };
