import { describe, expect, it } from "vitest";
import {
    encodeContextParam,
    encodeNamespaceSelection,
    makeLogsParam,
    makeScopedKey,
    makeYamlKey,
    parseContextParam,
    parseLogsParam,
    parseNamespaceSelection,
    parseScopedKey,
    parseYamlKey,
} from "./aks-workspace-context";

// The multi-context URL contract. Bare `ns`/`ns/name` tokens belong to the primary
// context (the pre-multi-context format — old deep links and pins must keep working);
// secondary contexts qualify tokens as `ctx:ns` and `ctx:ns/name`, every segment
// URI-encoded so EKS-ARN-style context names containing `:` and `/` stay unambiguous.

const PRIMARY = "aks-dev";

describe("makeScopedKey / parseScopedKey", () => {
    it("encodes primary-context keys bare (legacy shape)", () => {
        expect(makeScopedKey(PRIMARY, PRIMARY, "web", "api")).toBe("web/api");
        expect(makeScopedKey(null, PRIMARY, "web", "api")).toBe("web/api");
        expect(makeScopedKey(undefined, PRIMARY, "web", "api")).toBe("web/api");
    });

    it("prefixes secondary-context keys with the context", () => {
        expect(makeScopedKey("prod", PRIMARY, "web", "api")).toBe(
            "prod:web/api",
        );
    });

    it("round-trips an attached-context key", () => {
        const key = makeScopedKey("prod", PRIMARY, "web", "api-1");
        expect(parseScopedKey(key)).toEqual({
            context: "prod",
            ns: "web",
            name: "api-1",
        });
    });

    it("decodes bare keys as the primary context (null)", () => {
        expect(parseScopedKey("web/api")).toEqual({
            context: null,
            ns: "web",
            name: "api",
        });
    });

    it("keeps colon-bearing context names unambiguous", () => {
        const ctx = "arn:aws:eks:eu:cluster/prod";
        const key = makeScopedKey(ctx, PRIMARY, "kube-system", "coredns");
        expect(parseScopedKey(key)).toEqual({
            context: ctx,
            ns: "kube-system",
            name: "coredns",
        });
    });

    it("rejects keys with no namespace/name separator", () => {
        expect(parseScopedKey("")).toBeNull();
        expect(parseScopedKey(null)).toBeNull();
        expect(parseScopedKey("noSlash")).toBeNull();
    });
});

describe("makeYamlKey / parseYamlKey", () => {
    it("round-trips kind + scoped identity", () => {
        const key = makeYamlKey("Pod", "prod", PRIMARY, "web", "api");
        expect(key).toBe("Pod:prod:web/api");
        expect(parseYamlKey(key)).toEqual({
            kind: "Pod",
            context: "prod",
            namespace: "web",
            name: "api",
        });
    });

    it("keeps the legacy kind:ns/name shape for the primary context", () => {
        const key = makeYamlKey("Deployment", PRIMARY, PRIMARY, "web", "api");
        expect(key).toBe("Deployment:web/api");
        expect(parseYamlKey(key)?.context).toBeNull();
    });
});

describe("namespace selection codec", () => {
    it("encodes primary picks bare — byte-identical to the legacy format", () => {
        expect(
            encodeNamespaceSelection(
                [
                    { context: PRIMARY, namespace: "ecommerce" },
                    { context: PRIMARY, namespace: "default" },
                ],
                PRIMARY,
            ),
        ).toBe("ecommerce,default");
    });

    it("encodes secondary picks as ctx:ns", () => {
        expect(
            encodeNamespaceSelection(
                [
                    { context: PRIMARY, namespace: "ecommerce" },
                    { context: "staging", namespace: "web" },
                ],
                PRIMARY,
            ),
        ).toBe("ecommerce,staging:web");
    });

    it("round-trips a mixed selection", () => {
        const sel = [
            { context: PRIMARY, namespace: "ecommerce" },
            { context: "staging", namespace: "web" },
            { context: "staging", namespace: "*" },
        ];
        const encoded = encodeNamespaceSelection(sel, PRIMARY);
        expect(parseNamespaceSelection(encoded, PRIMARY)).toEqual(sel);
    });

    it("binds bare legacy tokens to the primary context", () => {
        expect(parseNamespaceSelection("ecommerce,payments", PRIMARY)).toEqual([
            { context: PRIMARY, namespace: "ecommerce" },
            { context: PRIMARY, namespace: "payments" },
        ]);
    });

    it("decodes a legacy bare * as all-primary-namespaces", () => {
        expect(parseNamespaceSelection("*", PRIMARY)).toEqual([
            { context: PRIMARY, namespace: "*" },
        ]);
    });

    it("keeps an encoded secondary * as a scoped wildcard", () => {
        expect(parseNamespaceSelection("staging:*", PRIMARY)).toEqual([
            { context: "staging", namespace: "*" },
        ]);
    });

    it("returns null for an empty selection so the param clears", () => {
        expect(encodeNamespaceSelection([], PRIMARY)).toBeNull();
    });

    it("returns empty for a missing param", () => {
        expect(parseNamespaceSelection(null, PRIMARY)).toEqual([]);
        expect(parseNamespaceSelection("", PRIMARY)).toEqual([]);
    });
});

describe("ctxs param codec", () => {
    it("round-trips attached context names", () => {
        const encoded = encodeContextParam(["staging", "aks prod"]);
        expect(parseContextParam(encoded)).toEqual(["staging", "aks prod"]);
    });

    it("returns null when nothing is attached", () => {
        expect(encodeContextParam([])).toBeNull();
        expect(parseContextParam(null)).toEqual([]);
    });
});

describe("logs param codec", () => {
    it("writes scoped pod keys per pod", () => {
        const param = makeLogsParam(
            [
                { context: PRIMARY, namespace: "web", name: "api" },
                { context: "prod", namespace: "web", name: "api" },
            ],
            PRIMARY,
        );
        expect(param).toBe("web/api,prod:web/api");
    });

    it("parses comma-separated scoped keys", () => {
        expect(parseLogsParam("web/api,prod:web/api", null)).toEqual([
            { context: null, ns: "web", name: "api" },
            { context: "prod", ns: "web", name: "api" },
        ]);
    });

    it("reads the legacy logs + logsNs pair as primary pods", () => {
        expect(parseLogsParam("api,web", "web")).toEqual([
            { context: null, ns: "web", name: "api" },
            { context: null, ns: "web", name: "web" },
        ]);
    });

    it("returns empty without a logs param", () => {
        expect(parseLogsParam(null, "web")).toEqual([]);
    });
});

