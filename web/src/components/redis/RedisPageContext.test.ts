import { describe, it, expect } from "vitest";
import { buildNamespaceTree, collectAllNamespacePaths, collectSubtreeKeys } from "./RedisPageContext";

// Fixture: a two-level keyspace (`user:*`, `cache:products:*`) plus one root-level bare key
// (`session`) that has no separator at all, so it falls into the synthetic "(no prefix)" bucket.
const keys = [
  "user:1001",
  "user:1002",
  "cache:products:1",
  "cache:products:2",
  "cache:categories",
  "session",
];

describe("collectAllNamespacePaths", () => {
  it("returns every namespace path in the tree, including nested ones", () => {
    const tree = buildNamespaceTree(keys, ":");
    const paths = collectAllNamespacePaths(tree);

    expect(paths).toEqual(new Set(["user", "cache", "cache:products", "(no prefix)"]));
  });

  it("returns an empty set for an empty tree", () => {
    expect(collectAllNamespacePaths([])).toEqual(new Set());
  });
});

describe("collectSubtreeKeys", () => {
  it("returns the node's own keys plus every descendant's", () => {
    const tree = buildNamespaceTree(keys, ":");
    const cache = tree.find((n) => n.path === "cache")!;
    expect(collectSubtreeKeys(cache).sort()).toEqual([
      "cache:categories",
      "cache:products:1",
      "cache:products:2",
    ]);
  });

  it("returns only the leaf's own keys for a leaf namespace", () => {
    const tree = buildNamespaceTree(keys, ":");
    const products = tree.find((n) => n.path === "cache")!.children.get("products")!;
    expect(collectSubtreeKeys(products).sort()).toEqual(["cache:products:1", "cache:products:2"]);
  });

  it("covers loose keys under the (no prefix) bucket", () => {
    const tree = buildNamespaceTree(keys, ":");
    const fallback = tree.find((n) => n.path === "(no prefix)")!;
    expect(collectSubtreeKeys(fallback)).toEqual(["session"]);
  });
});
