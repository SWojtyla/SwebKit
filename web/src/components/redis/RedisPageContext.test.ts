import { describe, it, expect } from "vitest";
import { buildNamespaceTree, collectAllNamespacePaths, defaultExpandedNamespacePaths } from "./RedisPageContext";

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

describe("defaultExpandedNamespacePaths", () => {
  it("expands only root-level namespaces, not nested descendants", () => {
    const tree = buildNamespaceTree(keys, ":");
    const paths = defaultExpandedNamespacePaths(tree);

    // Root-level namespaces only — "cache:products" (a child of "cache") must NOT be included,
    // which is exactly the "collapsed by default" behavior this initiative's Redis unit (2.1)
    // fixes: a multi-level keyspace should open showing its top-level groups, not every nested
    // folder at once.
    expect(paths).toEqual(new Set(["user", "cache", "(no prefix)"]));
    expect(paths.has("cache:products")).toBe(false);
  });

  it("returns an empty set for an empty tree", () => {
    expect(defaultExpandedNamespacePaths([])).toEqual(new Set());
  });
});
