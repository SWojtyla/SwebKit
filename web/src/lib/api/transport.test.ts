import { describe, it, expect, vi, afterEach } from "vitest";
import { ApiError, apiFetch, describeApiError } from "./transport";

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
