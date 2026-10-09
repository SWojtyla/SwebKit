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
    ApiRunEvent,
    ApiRunRequest,
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
 * A failed sidecar call. `message` stays the plain summary so every existing
 * `err.message` render keeps working; `kind`/`detail`/`hint` carry the classified
 * detail the server sends (`{ error, kind, detail, hint }`) so error surfaces can
 * show *why* it failed — timeout vs auth vs unreachable — instead of a bare "error".
 */
export class ApiError extends Error {
    constructor(
        message: string,
        public readonly status: number,
        public readonly kind?: string,
        public readonly detail?: string,
        public readonly hint?: string,
        /**
         * The parsed error body when the server sent JSON — lets callers read
         * fields beyond message/kind/detail/hint (e.g. the `currentContentStamp`
         * a 409 linked-request save conflict carries).
         */
        public readonly payload?: unknown,
    ) {
        super(message);
        this.name = "ApiError";
    }
}

/** Maps a failed response body to an {@link ApiError}. ASP.NET error payloads vary
 * in shape: `{ error }` (custom BadRequest bodies), `{ detail }`/`{ title }`
 * (ProblemDetails from Results.Problem), or plain text — and the global handler's
 * `{ error, kind, detail, hint }` fields are preserved when present. */
function toApiError(
    status: number,
    statusText: string,
    body: string,
): ApiError {
    if (body) {
        try {
            const parsed = JSON.parse(body);
            const message = parsed?.error ?? parsed?.detail ?? parsed?.title;
            if (typeof message === "string" && message.trim()) {
                return new ApiError(
                    message,
                    status,
                    typeof parsed?.kind === "string" ? parsed.kind : undefined,
                    typeof parsed?.detail === "string"
                        ? parsed.detail
                        : undefined,
                    typeof parsed?.hint === "string" ? parsed.hint : undefined,
                    parsed,
                );
            }
        } catch {
            // Not JSON — fall through to the raw body below.
        }
    }
    return new ApiError(
        body || statusText || `Request failed with status ${status}`,
        status,
    );
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
        throw toApiError(res.status, res.statusText, body);
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
        throw toApiError(res.status, res.statusText, text);
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
    /** agent-colleague item 4: `<area>.<kind>.<id>` → bounded per-entity detail. */
    entities?: Record<string, unknown>;
}

export async function postScreenState(
    body: ScreenStatePublishBody,
): Promise<void> {
    return apiSend<void>("/api/agent/screen-state", "POST", body);
}

/** Publish payload for POST /api/agent/screen-state — `entities` (agent-colleague item 4)
 * holds `<area>.<kind>.<id>` → bounded detail maps; see lib/stores/screen-state.ts. */
export interface AgentFeedbackBody {
    /** The `exchangeId` from the terminal "done" stream event of the turn being rated. */
    exchangeId: string;
    sentiment?: "down" | string;
    comment?: string;
    tags?: string[];
}

export interface AgentFeedbackResult {
    recorded: boolean;
    /** False when the server-side exchange buffer had already evicted the id. */
    exchangeFound: boolean;
    feedbackId: string;
}

/** POST /api/agent/feedback (agent-colleague item 5) — flushes the retained exchange for
 * `exchangeId` into agent-feedback.json. */
export async function postAgentFeedback(
    body: AgentFeedbackBody,
): Promise<AgentFeedbackResult> {
    return apiSend<AgentFeedbackResult>("/api/agent/feedback", "POST", body);
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
        throw toApiError(res.status, res.statusText, text);
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

/** Reads one SSE `data:` payload out of a raw record; null for comments/keep-alives. */
function sseDataPayload(record: string): string | null {
    const dataLine = record
        .split("\n")
        .find((line) => line.startsWith("data:"));
    if (!dataLine) return null;
    const json = dataLine.slice("data:".length).trim();
    return json || null;
}

/**
 * POSTs a request-run plan (`mode`: dependency chain, subtree, or explicit
 * selection) to `/api/api-client/run` and invokes `onEvent` for each
 * {@link ApiRunEvent} as it streams in — same fetch-reader pattern as
 * {@link streamAgentChat}, since `EventSource` cannot send a JSON body.
 *
 * Chunks are buffered and split on the SSE record separator (`\n\n`), so a
 * `data:` line split across two network reads is reassembled before parsing.
 * A trailing record without a closing separator is flushed when the stream
 * ends rather than being dropped.
 *
 * Rejects when the initial request fails — including the plan-time 400s the
 * endpoint returns before the stream opens (`dependency_cycle`,
 * `missing_dependency`, `cross_collection_dependency`, `unknown_request`,
 * `too_many_steps`, `empty_plan`) — surfacing the server's `detail`/`hint`
 * fields through the usual {@link ApiError}. Aborting `signal` cancels the
 * fetch mid-stream; the reader rejects with `AbortError`, which callers map to
 * a cancelled run rather than an error.
 */
export async function streamApiRun(
    req: ApiRunRequest,
    onEvent: (event: ApiRunEvent) => void,
    signal?: AbortSignal,
): Promise<void> {
    const res = await fetch(`${SIDECAR_BASE_URL}/api/api-client/run`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(req),
        signal,
    });

    if (!res.ok || !res.body) {
        const text = await res.text().catch(() => "");
        throw toApiError(res.status, res.statusText, text);
    }

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";

    const emitRecord = (record: string) => {
        const json = sseDataPayload(record);
        if (!json) return;
        onEvent(JSON.parse(json) as ApiRunEvent);
    };

    while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });

        let separatorIndex: number;
        while ((separatorIndex = buffer.indexOf("\n\n")) !== -1) {
            const record = buffer.slice(0, separatorIndex);
            buffer = buffer.slice(separatorIndex + 2);
            emitRecord(record);
        }
    }

    // Flush the streaming decoder's multi-byte tail, then deliver any record
    // the server left unterminated right before the stream ended.
    buffer += decoder.decode();
    const tail = buffer.trim();
    if (tail) emitRecord(tail);
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

/** Composes the fullest readable error text for a toast/notification body:
 * summary + classified detail + hint when the failure came back structured. */
export function describeApiError(err: unknown): string {
    if (err instanceof ApiError) {
        return [err.message, err.detail, err.hint].filter(Boolean).join("\n");
    }
    return err instanceof Error ? err.message : String(err);
}

export { SIDECAR_BASE_URL };
