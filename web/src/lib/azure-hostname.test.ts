import { describe, expect, it } from "vitest";
import {
    normalizeRedisCacheHost,
    normalizeServiceBusNamespace,
} from "./azure-hostname";

describe("normalizeServiceBusNamespace", () => {
    it("appends the suffix to a bare name", () => {
        expect(normalizeServiceBusNamespace("sb-dev-shared-sb-weu")).toBe(
            "sb-dev-shared-sb-weu.servicebus.windows.net",
        );
    });

    it("passes a full hostname through untouched", () => {
        expect(
            normalizeServiceBusNamespace(
                "sb-dev-shared-sb-weu.servicebus.windows.net",
            ),
        ).toBe("sb-dev-shared-sb-weu.servicebus.windows.net");
    });

    it("keeps custom/private-endpoint hostnames as typed", () => {
        expect(normalizeServiceBusNamespace("sb.internal.contoso.local")).toBe(
            "sb.internal.contoso.local",
        );
    });

    it("strips scheme, trailing slash and whitespace", () => {
        expect(
            normalizeServiceBusNamespace(
                "  sb://sb-dev-shared-sb-weu.servicebus.windows.net/  ",
            ),
        ).toBe("sb-dev-shared-sb-weu.servicebus.windows.net");
        expect(
            normalizeServiceBusNamespace("https://myns.servicebus.windows.net"),
        ).toBe("myns.servicebus.windows.net");
    });

    it("strips a pasted port", () => {
        expect(normalizeServiceBusNamespace("myns:5671")).toBe(
            "myns.servicebus.windows.net",
        );
    });

    it("returns empty for empty input", () => {
        expect(normalizeServiceBusNamespace("   ")).toBe("");
    });
});

describe("normalizeRedisCacheHost", () => {
    it("passes a bare name through — the backend appends the suffix", () => {
        expect(normalizeRedisCacheHost("my-cache")).toBe("my-cache");
    });

    it("keeps a pasted hostname intact — used verbatim server-side", () => {
        expect(
            normalizeRedisCacheHost("my-cache.redis.cache.windows.net"),
        ).toBe("my-cache.redis.cache.windows.net");
    });

    it("keeps private-link and custom hostnames intact", () => {
        expect(
            normalizeRedisCacheHost(
                "cache.privatelink.redis.cache.windows.net",
            ),
        ).toBe("cache.privatelink.redis.cache.windows.net");
        expect(normalizeRedisCacheHost("redis.internal.contoso.local")).toBe(
            "redis.internal.contoso.local",
        );
    });

    it("strips scheme, port and whitespace", () => {
        expect(
            normalizeRedisCacheHost(
                "  rediss://my-cache.redis.cache.windows.net:6380  ",
            ),
        ).toBe("my-cache.redis.cache.windows.net");
        expect(normalizeRedisCacheHost("my-cache:6380")).toBe("my-cache");
    });

    it("returns empty for empty input", () => {
        expect(normalizeRedisCacheHost("")).toBe("");
    });
});

