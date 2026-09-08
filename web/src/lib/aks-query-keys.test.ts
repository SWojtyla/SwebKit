import { describe, it, expect } from "vitest";
import { isAksQueryKey, isAksResourceQueryKey } from "./aks-query-keys";

describe("isAksQueryKey", () => {
  it("matches every namespaced AKS resource query", () => {
    expect(isAksQueryKey(["aks-pods", "default", undefined])).toBe(true);
    expect(isAksQueryKey(["aks-deployments", "default"])).toBe(true);
    expect(isAksQueryKey(["aks-helm-history", "default", "release"])).toBe(true);
  });

  it("matches the cluster-level bootstrap queries", () => {
    expect(isAksQueryKey(["aks-namespaces"])).toBe(true);
    expect(isAksQueryKey(["aks-contexts"])).toBe(true);
    expect(isAksQueryKey(["aks-test"])).toBe(true);
  });

  it("does not match other features' queries", () => {
    expect(isAksQueryKey(["profile"])).toBe(false);
    expect(isAksQueryKey(["storage-blobs", "container"])).toBe(false);
    expect(isAksQueryKey(["sb-queues"])).toBe(false);
  });

  it("tolerates a non-string or empty key head", () => {
    expect(isAksQueryKey([])).toBe(false);
    expect(isAksQueryKey([{ scope: "aks-pods" }])).toBe(false);
    expect(isAksQueryKey([42])).toBe(false);
  });

  it("is a prefix test, not equality — the bug this module exists to prevent", () => {
    // `invalidateQueries({ queryKey: ["aks-"] })` compared elements, so it matched
    // nothing and both Refresh and the auto-refresh timer were silent no-ops.
    expect(isAksQueryKey(["aks-"])).toBe(true);
    expect(isAksQueryKey(["aks-pods"])).toBe(true);
  });
});

describe("isAksResourceQueryKey", () => {
  it("is what the auto-refresh timer uses, so it must not pull in the ~18s namespace list", () => {
    expect(isAksResourceQueryKey(["aks-namespaces"])).toBe(false);
    expect(isAksQueryKey(["aks-namespaces"])).toBe(true);
  });

  it("excludes the slow cluster-level bootstrap queries", () => {
    expect(isAksResourceQueryKey(["aks-namespaces"])).toBe(false);
    expect(isAksResourceQueryKey(["aks-contexts"])).toBe(false);
    expect(isAksResourceQueryKey(["aks-test"])).toBe(false);
  });

  it("includes namespaced resource queries", () => {
    expect(isAksResourceQueryKey(["aks-pods", "default", undefined])).toBe(true);
    expect(isAksResourceQueryKey(["aks-pod-metrics", "default"])).toBe(true);
  });

  it("does not match other features' queries", () => {
    expect(isAksResourceQueryKey(["redis-keys"])).toBe(false);
  });
});
