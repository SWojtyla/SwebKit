import { describe, expect, it } from "vitest";
import {
    normalizeRedisCacheName,
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
            normalizeServiceBusNamespace("sb-dev-shared-sb-weu.servicebus.windows.net"),
        ).toBe("sb-dev-shared-sb-weu.servicebus.windows.net");
    });

    it("keeps custom/private-endpoint hostnames as typed", () => {
        expect(normalizeServiceBusNamespace("sb.internal.contoso.local")).toBe(
            "sb.internal.contoso.local",
        );
    });

    it("strips scheme, trailing slash and whitespace", () => {
        expect(
            normalizeServiceBusNamespace("  sb://sb-dev-shared-sb-weu.servicebus.windows.net/  "),
        ).toBe("sb-dev-shared-sb-weu.servicebus.windows.net");
        expect(normalizeServiceBusNamespace("https://myns.servicebus.windows.net")).toBe(
            "myns.servicebus.windows.net",
        );
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

describe("normalizeRedisCacheName", () => {
    it("passes a bare name through", () => {
        expect(normalizeRedisCacheName("my-cache")).toBe("my-cache");
    });

    it("strips the suffix from a pasted hostname", () => {
        expect(
            normalizeRedisCacheName("my-cache.redis.cache.windows.net"),
        ).toBe("my-cache");
    });

    it("handles scheme, port and whitespace", () => {
        expect(
            normalizeRedisCacheName("  rediss://my-cache.redis.cache.windows.net:6380  "),
        ).toBe("my-cache");
        expect(normalizeRedisCacheName("my-cache:6380")).toBe("my-cache");
    });

    it("leaves non-standard hostnames as typed", () => {
        expect(normalizeRedisCacheName("redis.internal.contoso.local")).toBe(
            "redis.internal.contoso.local",
        );
    });

    it("returns empty for empty input", () => {
        expect(normalizeRedisCacheName("")).toBe("");
    });
});
