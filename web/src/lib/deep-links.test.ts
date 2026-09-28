import { describe, it, expect } from "vitest";
import { mapDeepLink, type DeepLinkContext } from "./deep-links";

const ctx: DeepLinkContext = {
    serviceBusNamespaces: [
        {
            id: "00000000-0000-0000-0000-000000000001",
            alias: "orders-dev",
            fullyQualifiedNamespace: "orders-dev.servicebus.windows.net",
        },
    ],
    sqlConnections: [
        { id: "demo-sql-prd", displayName: "orders-prd-sql (restricted)", server: "orders-prd-sql.database.windows.net" },
    ],
};

describe("mapDeepLink", () => {
    it("maps a servicebus queue link onto the page's search-param convention", () => {
        expect(
            mapDeepLink("swebkit://servicebus/queue?ns=orders-dev&entity=order-created&view=dlq", ctx),
        ).toBe(
            "/service-bus?ns=00000000-0000-0000-0000-000000000001&entity=order-created&view=dlq",
        );
    });

    it("resolves a namespace FQDN the same way as its alias", () => {
        expect(
            mapDeepLink("swebkit://servicebus?ns=orders-dev.servicebus.windows.net", ctx),
        ).toBe("/service-bus?ns=00000000-0000-0000-0000-000000000001");
    });

    it("maps an entity path segment for topic/subscription links", () => {
        expect(
            mapDeepLink("swebkit://servicebus/subscription/order-events/audit-sub?ns=orders-dev", ctx),
        ).toBe(
            "/service-bus?ns=00000000-0000-0000-0000-000000000001&entity=order-events%2Faudit-sub",
        );
    });

    it("drops the single-profile profile= parameter", () => {
        expect(mapDeepLink("swebkit://servicebus?profile=prod&ns=orders-dev", ctx)).toBe(
            "/service-bus?ns=00000000-0000-0000-0000-000000000001",
        );
        expect(mapDeepLink("swebkit://sql/demo-sql-prd?profile=prod", ctx)).toBe(
            "/sql?connection=demo-sql-prd",
        );
    });

    it("maps a settings tab link", () => {
        expect(mapDeepLink("swebkit://settings/access", ctx)).toBe("/settings?tab=access");
    });

    it("maps sql links by connection id or display name", () => {
        expect(mapDeepLink("swebkit://sql?connection=demo-sql-prd", ctx)).toBe(
            "/sql?connection=demo-sql-prd",
        );
        expect(
            mapDeepLink("swebkit://sql?connection=orders-prd-sql%20(restricted)", ctx),
        ).toBe("/sql?connection=demo-sql-prd");
    });

    it("maps bare pages and the bare root", () => {
        expect(mapDeepLink("swebkit://redis", ctx)).toBe("/redis");
        expect(mapDeepLink("swebkit://monitoring", ctx)).toBe("/monitoring");
        expect(mapDeepLink("swebkit://dashboard", ctx)).toBe("/");
        expect(mapDeepLink("swebkit://", ctx)).toBe("/");
    });

    it("rejects unknown hosts, other schemes, and malformed URLs", () => {
        expect(mapDeepLink("swebkit://definitely-not-a-page", ctx)).toBeNull();
        expect(mapDeepLink("https://evil.example/x", ctx)).toBeNull();
        expect(mapDeepLink(":::garbage", ctx)).toBeNull();
        expect(mapDeepLink("", ctx)).toBeNull();
    });
});
