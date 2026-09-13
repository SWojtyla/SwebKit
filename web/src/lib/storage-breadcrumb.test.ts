import { describe, it, expect } from "vitest";
import { buildStorageBreadcrumbAncestors, storageCurrentPrefixLabel } from "./storage-breadcrumb";

// Regression coverage for a real navigation bug: labels were computed by
// string-replacing each ancestor prefix against `currentPrefix` (the wrong,
// constant reference) instead of against that ancestor's own immediate parent.
describe("storage breadcrumb label computation", () => {
  describe("at the container root", () => {
    it("has no ancestor crumbs and no current-segment label", () => {
      expect(buildStorageBreadcrumbAncestors([])).toEqual([]);
      expect(storageCurrentPrefixLabel([], "")).toBe("");
    });
  });

  describe("one level deep", () => {
    const prefixHistory = [""];
    const currentPrefix = "a/";

    it("renders no ancestor crumb for the root sentinel (no blank crumb)", () => {
      expect(buildStorageBreadcrumbAncestors(prefixHistory)).toEqual([]);
    });

    it("labels the current segment correctly, without a trailing slash", () => {
      expect(storageCurrentPrefixLabel(prefixHistory, currentPrefix)).toBe("a");
    });
  });

  describe("two levels deep", () => {
    const prefixHistory = ["", "a/"];
    const currentPrefix = "a/b/";

    it("labels the single ancestor crumb with just its own segment, not concatenated", () => {
      const ancestors = buildStorageBreadcrumbAncestors(prefixHistory);
      expect(ancestors).toEqual([{ prefix: "a/", navigateIndex: 2, label: "a" }]);
    });

    it("labels the current segment with just its own segment", () => {
      expect(storageCurrentPrefixLabel(prefixHistory, currentPrefix)).toBe("b");
    });
  });

  describe("three levels deep", () => {
    const prefixHistory = ["", "a/", "a/b/"];
    const currentPrefix = "a/b/c/";

    it("labels every ancestor crumb with only its own segment", () => {
      const ancestors = buildStorageBreadcrumbAncestors(prefixHistory);
      expect(ancestors).toEqual([
        { prefix: "a/", navigateIndex: 2, label: "a" },
        { prefix: "a/b/", navigateIndex: 3, label: "b" },
      ]);
    });

    it("labels the current segment with only its own segment", () => {
      expect(storageCurrentPrefixLabel(prefixHistory, currentPrefix)).toBe("c");
    });
  });

  describe("navigation targets stay correct", () => {
    it("each ancestor's navigateIndex resolves back to that ancestor's own prefix", () => {
      // handleBreadcrumb(index) navigates to prefixHistory[index - 1] — mirrored here
      // to prove label and click target describe the same destination.
      const prefixHistory = ["", "a/", "a/b/"];
      const ancestors = buildStorageBreadcrumbAncestors(prefixHistory);
      for (const ancestor of ancestors) {
        expect(prefixHistory[ancestor.navigateIndex - 1]).toBe(ancestor.prefix);
      }
    });
  });
});
