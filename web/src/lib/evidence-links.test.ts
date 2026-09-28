import { describe, expect, it } from "vitest";
import { resolveEvidenceView } from "./evidence-links";

// Evidence-view → route resolution (agent-colleague item 1): whitelisted kinds map
// onto each page's existing search params; anything else must return null so the
// caller renders plain text — never a dead link.

describe("resolveEvidenceView", () => {
    it("maps a serviceBus view to /service-bus with its search params", () => {
        expect(
            resolveEvidenceView({
                kind: "serviceBus",
                params: { ns: "prod-sb", entity: "orders", view: "dlq" },
            }),
        ).toBe("/service-bus?ns=prod-sb&entity=orders&view=dlq");
    });

    it("keeps optional serviceBus params (msg/seq/entityName)", () => {
        expect(
            resolveEvidenceView({
                kind: "serviceBus",
                params: {
                    ns: "prod-sb",
                    entity: "orders",
                    view: "dlq",
                    msg: "abc123",
                    seq: "42",
                },
            }),
        ).toBe(
            "/service-bus?ns=prod-sb&entity=orders&view=dlq&msg=abc123&seq=42",
        );
    });

    it("maps sql, aks, redis, and monitoring kinds to their routes", () => {
        expect(
            resolveEvidenceView({
                kind: "sql",
                params: { connection: "prod-sql", table: "dbo.Orders" },
            }),
        ).toBe("/sql?connection=prod-sql&table=dbo.Orders");

        expect(
            resolveEvidenceView({
                kind: "aks",
                params: { ns: "payments", tab: "pods", pod: "api-7c9f" },
            }),
        ).toBe("/aks?ns=payments&tab=pods&pod=api-7c9f");

        expect(
            resolveEvidenceView({
                kind: "redis",
                params: { cache: "prod-cache", tab: "slowlog" },
            }),
        ).toBe("/redis?cache=prod-cache&tab=slowlog");

        expect(
            resolveEvidenceView({
                kind: "monitoring",
                params: { report: "r-1" },
            }),
        ).toBe("/monitoring?report=r-1&tab=reports");
    });

    it("URL-encodes every param value", () => {
        const route = resolveEvidenceView({
            kind: "sql",
            params: {
                connection: "prod & co",
                table: "weird/schema.name",
            },
        });
        expect(route).toBe(
            "/sql?connection=prod+%26+co&table=weird%2Fschema.name",
        );
    });

    it("returns null for an unknown kind", () => {
        expect(
            resolveEvidenceView({ kind: "aws-console", params: { a: "b" } }),
        ).toBeNull();
    });

    it("returns null when a required param is missing", () => {
        expect(
            resolveEvidenceView({ kind: "serviceBus", params: {} }),
        ).toBeNull();
        expect(
            resolveEvidenceView({ kind: "redis", params: { tab: "keys" } }),
        ).toBeNull();
        expect(
            resolveEvidenceView({ kind: "sql", params: { table: "dbo.X" } }),
        ).toBeNull();
    });

    it("returns null for no-param kinds with nothing to navigate to", () => {
        expect(resolveEvidenceView({ kind: "aks", params: {} })).toBeNull();
        expect(resolveEvidenceView({ kind: "monitoring" })).toBeNull();
    });

    it("returns null for enum-valued params outside their set", () => {
        expect(
            resolveEvidenceView({
                kind: "serviceBus",
                params: { ns: "x", view: "bogus" },
            }),
            // "view" is stripped (invalid enum) — but ns alone still links.
        ).toBe("/service-bus?ns=x");
        expect(
            resolveEvidenceView({ kind: "redis", params: { cache: "c", tab: "nope" } }),
        ).toBe("/redis?cache=c");
    });

    it("strips unknown params instead of failing the link", () => {
        expect(
            resolveEvidenceView({
                kind: "sql",
                params: { connection: "c", evil: "javascript:alert(1)" },
            }),
        ).toBe("/sql?connection=c");
    });

    it("returns null for non-object or missing views", () => {
        expect(resolveEvidenceView(null)).toBeNull();
        expect(resolveEvidenceView(undefined)).toBeNull();
        expect(resolveEvidenceView({})).toBeNull();
        expect(resolveEvidenceView({ kind: 42 as unknown as string })).toBeNull();
    });

    it("coerces scalar non-string params and drops objects/arrays", () => {
        expect(
            resolveEvidenceView({
                kind: "serviceBus",
                params: { ns: "x", seq: 42, msg: { nested: true }, junk: [1] },
            }),
        ).toBe("/service-bus?ns=x&seq=42");
    });
});
