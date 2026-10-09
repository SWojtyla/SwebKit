import { describe, it, expect, vi, afterEach } from "vitest";
import {
    ApiError,
    apiFetch,
    describeApiError,
    streamApiRun,
} from "./transport";
import type { ApiRunEvent } from "../types";

/** A fetch Response whose body yields the given chunks — lets a `data:` record
 *  be split mid-JSON across network reads, the failure mode streamApiRun's
 *  buffering exists for. */
function sseResponse(chunks: string[]): Response {
    const encoder = new TextEncoder();
    const stream = new ReadableStream<Uint8Array>({
        start(controller) {
            for (const chunk of chunks)
                controller.enqueue(encoder.encode(chunk));
            controller.close();
        },
    });
    return new Response(stream, { status: 200 });
}

describe("apiFetch error mapping", () => {
    afterEach(() => vi.unstubAllGlobals());

    it("throws an ApiError carrying kind/detail/hint from the structured payload", async () => {
        vi.stubGlobal(
            "fetch",
            vi.fn(
                async () =>
                    new Response(
                        JSON.stringify({
                            error: "Service Bus request failed",
                            kind: "unreachable",
                            detail: "ServiceBusException: put_token failed",
                            hint: "Check the connection string",
                        }),
                        { status: 502 },
                    ),
            ),
        );

        const err = (await apiFetch("/x").catch((e) => e)) as ApiError;

        expect(err).toBeInstanceOf(ApiError);
        expect(err.message).toBe("Service Bus request failed");
        expect(err.status).toBe(502);
        expect(err.kind).toBe("unreachable");
        expect(err.detail).toBe("ServiceBusException: put_token failed");
        expect(err.hint).toBe("Check the connection string");
    });

    it("still throws an ApiError for plain-text bodies", async () => {
        vi.stubGlobal(
            "fetch",
            vi.fn(async () => new Response("boom", { status: 500 })),
        );

        const err = (await apiFetch("/x").catch((e) => e)) as ApiError;

        expect(err).toBeInstanceOf(ApiError);
        expect(err.message).toBe("boom");
        expect(err.kind).toBeUndefined();
    });

    it("reads ProblemDetails detail/title bodies", async () => {
        vi.stubGlobal(
            "fetch",
            vi.fn(
                async () =>
                    new Response(
                        JSON.stringify({ title: "Bad things happened" }),
                        { status: 400 },
                    ),
            ),
        );

        const err = (await apiFetch("/x").catch((e) => e)) as ApiError;

        expect(err.message).toBe("Bad things happened");
    });
});

describe("describeApiError", () => {
    it("joins message, detail and hint for toasts", () => {
        const err = new ApiError(
            "Request failed",
            502,
            "unreachable",
            "SocketException: refused",
            "Check VPN",
        );

        expect(describeApiError(err)).toBe(
            "Request failed\nSocketException: refused\nCheck VPN",
        );
    });

    it("degrades to Error.message for non-API errors", () => {
        expect(describeApiError(new TypeError("nope"))).toBe("nope");
        expect(describeApiError("raw")).toBe("raw");
    });
});

describe("streamApiRun", () => {
    afterEach(() => vi.unstubAllGlobals());

    const runReq = {
        mode: "explicit" as const,
        collectionId: "col",
        requestIds: ["r1", "r2"],
        stopOnError: true,
        delayMs: 0,
    };

    it("delivers each SSE data: record in order", async () => {
        vi.stubGlobal(
            "fetch",
            vi.fn(async () =>
                sseResponse([
                    'data: {"type":"plan","runId":"r","steps":[{"index":0,"requestId":"r1","name":"A"}]}\n\n',
                    'data: {"type":"done","completedSteps":1,"failedSteps":0,"durationMs":5}\n\n',
                ]),
            ),
        );

        const events: ApiRunEvent[] = [];
        await streamApiRun(runReq, (e) => events.push(e));

        expect(events.map((e) => e.type)).toEqual(["plan", "done"]);
    });

    it("reassembles a record split across chunks and skips keep-alive comments", async () => {
        vi.stubGlobal(
            "fetch",
            vi.fn(async () =>
                sseResponse([
                    ': keep-alive\n\ndata: {"type":"done","completedSte',
                    'ps":2,"failedSteps":0,"durationMs":5}\n\n',
                ]),
            ),
        );

        const events: ApiRunEvent[] = [];
        await streamApiRun(runReq, (e) => events.push(e));

        expect(events).toEqual([
            {
                type: "done",
                completedSteps: 2,
                failedSteps: 0,
                durationMs: 5,
            },
        ]);
    });

    it("flushes a final record the server left unterminated", async () => {
        vi.stubGlobal(
            "fetch",
            vi.fn(async () =>
                sseResponse([
                    'data: {"type":"aborted","reason":"cancelled","completedSteps":0}',
                ]),
            ),
        );

        const events: ApiRunEvent[] = [];
        await streamApiRun(runReq, (e) => events.push(e));

        expect(events).toEqual([
            { type: "aborted", reason: "cancelled", completedSteps: 0 },
        ]);
    });

    it("rejects with the structured ApiError a rejected plan returns", async () => {
        vi.stubGlobal(
            "fetch",
            vi.fn(
                async () =>
                    new Response(
                        JSON.stringify({
                            error: "Run plan rejected",
                            kind: "dependency_cycle",
                            detail: "A → B → A",
                            hint: "Remove the circular prerequisite",
                        }),
                        { status: 400 },
                    ),
            ),
        );

        const err = (await streamApiRun(runReq, () => {}).catch(
            (e) => e,
        )) as ApiError;

        expect(err).toBeInstanceOf(ApiError);
        expect(err.kind).toBe("dependency_cycle");
        expect(describeApiError(err)).toContain("A → B → A");
        expect(describeApiError(err)).toContain(
            "Remove the circular prerequisite",
        );
    });

    it("propagates a malformed event instead of swallowing it", async () => {
        vi.stubGlobal(
            "fetch",
            vi.fn(async () => sseResponse(["data: {not json\n\n"])),
        );

        await expect(streamApiRun(runReq, () => {})).rejects.toThrow();
    });
});
