import { describe, expect, it } from "vitest";
import {
    aksUrl,
    mergeScopedQueryResults,
    normalizeTargets,
    type ScopedQuerySlice,
} from "./useAks";

// Multi-context fan-out contracts. `useAksScopedList` is a hook, so the merge and
// target-normalization live in these exported pure functions — the tests exercise
// the exact code the workspace's merged tables depend on without a renderer.

describe("aksUrl", () => {
    it("appends ?context= to a bare path", () => {
        expect(aksUrl("/api/aks/default/pods", "staging")).toBe(
            "/api/aks/default/pods?context=staging",
        );
    });

    it("appends &context= when the path already has a query", () => {
        expect(aksUrl("/api/aks/default/pods?tail=50", "prod")).toBe(
            "/api/aks/default/pods?tail=50&context=prod",
        );
    });

    it("encodes context names with reserved characters", () => {
        expect(aksUrl("/api/aks/namespaces", "arn:aws:eks/x")).toBe(
            `/api/aks/namespaces?context=${encodeURIComponent("arn:aws:eks/x")}`,
        );
    });

    it("leaves the path untouched without a context", () => {
        expect(aksUrl("/api/aks/default/pods")).toBe("/api/aks/default/pods");
        expect(aksUrl("/api/aks/default/pods", null)).toBe(
            "/api/aks/default/pods",
        );
    });
});

describe("normalizeTargets", () => {
    it("passes an explicit target array through unchanged", () => {
        const targets = [
            { context: "dev", ns: "*" },
            { context: "prod", ns: "web" },
        ];
        expect(normalizeTargets(targets, "dev")).toBe(targets);
    });

    it("wraps a bare ns token with the caller's context", () => {
        expect(normalizeTargets("ecommerce", "dev", "staging")).toEqual([
            { context: "staging", ns: "ecommerce" },
        ]);
    });

    it("falls back to the configured context for bare tokens", () => {
        expect(normalizeTargets("ecommerce", "dev")).toEqual([
            { context: "dev", ns: "ecommerce" },
        ]);
    });

    it("returns no targets for an empty scope", () => {
        expect(normalizeTargets(null, "dev")).toEqual([]);
        expect(normalizeTargets(undefined, "dev")).toEqual([]);
        expect(normalizeTargets("", "dev")).toEqual([]);
    });
});

describe("mergeScopedQueryResults", () => {
    const targets = [
        { context: "dev", ns: "*" },
        { context: "prod", ns: "web" },
    ];
    const slice = <T>(over: Partial<ScopedQuerySlice<T>>): ScopedQuerySlice<T> => ({
        data: undefined,
        isPending: false,
        isFetching: false,
        error: null,
        ...over,
    });

    it("merges rows from every settled target", () => {
        const merged = mergeScopedQueryResults(targets, [
            slice<{ name: string }>({ data: [{ name: "a" }] }),
            slice<{ name: string }>({ data: [{ name: "b" }, { name: "c" }] }),
        ]);
        expect(merged.data?.map((r) => r.name)).toEqual(["a", "b", "c"]);
        expect(merged.error).toBeNull();
        expect(merged.contextErrors).toEqual([]);
        expect(merged.isPending).toBe(false);
    });

    it("keeps survivors' rows while a failed target lands in contextErrors", () => {
        const failure = new Error("RBAC denied");
        const merged = mergeScopedQueryResults(targets, [
            slice<{ name: string }>({ data: [{ name: "a" }] }),
            slice<{ name: string }>({ error: failure }),
        ]);
        expect(merged.data?.map((r) => r.name)).toEqual(["a"]);
        expect(merged.error).toBeNull();
        expect(merged.contextErrors).toEqual([
            { context: "prod", error: failure },
        ]);
    });

    it("reports error only when nothing resolved", () => {
        const failure = new Error("cluster unreachable");
        const merged = mergeScopedQueryResults(targets, [
            slice({ error: failure }),
            slice({ error: new Error("timeout") }),
        ]);
        expect(merged.data).toBeUndefined();
        expect(merged.error).toBe(failure);
        expect(merged.contextErrors).toHaveLength(2);
    });

    it("is pending while unresolved targets are still loading", () => {
        const merged = mergeScopedQueryResults(targets, [
            slice({ data: [] }),
            slice({ isPending: true, isFetching: true }),
        ]);
        // An empty but resolved list is still data — the merge is settled.
        expect(merged.data).toEqual([]);
        expect(merged.isPending).toBe(false);
        expect(merged.isFetching).toBe(true);
    });

    it("is loading only when every target is pending", () => {
        const merged = mergeScopedQueryResults(targets, [
            slice({ isPending: true, isFetching: true }),
            slice({ isPending: true, isFetching: true }),
        ]);
        expect(merged.isLoading).toBe(true);
        expect(merged.isPending).toBe(true);
        expect(merged.data).toBeUndefined();
    });

    it("produces no loading state for an empty target set", () => {
        const merged = mergeScopedQueryResults<{ name: string }>([], []);
        expect(merged.isLoading).toBe(false);
        expect(merged.data).toBeUndefined();
    });
});
