import { describe, expect, it } from "vitest";
import { deriveAksConnectivity } from "./useServiceHealth";

// The dashboard used to call AKS "connected" the moment the ~1s API ping
// resolved, while the namespace list — the actual gate for the AKS page — was
// still loading for ~18s on a cold cluster. `deriveAksConnectivity` is the
// pure state machine behind the card, so every transition is pinned here.

const base = {
    configured: true,
    probing: true,
    testPending: false,
    namespacesPending: false,
};

describe("deriveAksConnectivity", () => {
    it("is not-configured without a cluster", () => {
        expect(
            deriveAksConnectivity({ ...base, configured: false }).connectivity,
        ).toBe("not-configured");
    });

    it("is on-demand when startup warm-up is off — no cluster calls for a badge", () => {
        const result = deriveAksConnectivity({ ...base, probing: false });
        expect(result.connectivity).toBe("on-demand");
        expect(result.connected).toBeNull();
    });

    it("is checking while the ping is in flight", () => {
        const result = deriveAksConnectivity({ ...base, testPending: true });
        expect(result.connectivity).toBe("checking");
        expect(result.connected).toBeNull();
    });

    it("is unavailable when the ping fails, with the classified detail", () => {
        const result = deriveAksConnectivity({
            ...base,
            testConnected: false,
            testDetail: "Cluster unreachable",
        });
        expect(result.connectivity).toBe("unavailable");
        expect(result.connected).toBe(false);
        expect(result.detail).toBe("Cluster unreachable");
    });

    it("is connecting — not connected — while namespaces are still loading", () => {
        const result = deriveAksConnectivity({
            ...base,
            testConnected: true,
            namespacesPending: true,
        });
        expect(result.connectivity).toBe("connecting");
        expect(result.connected).toBeNull();
        expect(result.detail).toBe("Loading namespaces…");
    });

    it("is degraded when the ping passes but namespace listing fails", () => {
        const result = deriveAksConnectivity({
            ...base,
            testConnected: true,
            namespacesError: "Forbidden: cannot list namespaces",
        });
        expect(result.connectivity).toBe("degraded");
        expect(result.connected).toBe(false);
        expect(result.detail).toBe("Forbidden: cannot list namespaces");
    });

    it("is connected only once the namespace list resolves", () => {
        const result = deriveAksConnectivity({
            ...base,
            testConnected: true,
            namespaceCount: 7,
        });
        expect(result.connectivity).toBe("connected");
        expect(result.connected).toBe(true);
        expect(result.detail).toBe("7 namespaces");
    });

    it("connects without a count detail when the list resolves empty", () => {
        const result = deriveAksConnectivity({
            ...base,
            testConnected: true,
            namespaceCount: 0,
        });
        expect(result.connectivity).toBe("connected");
        expect(result.detail).toBe("0 namespaces");
    });
});
