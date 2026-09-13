import { describe, it, expect } from "vitest";
import { hasActiveFilters } from "./filterLogic";
import { createFilterRule, type AdvancedFilterRule } from "./filterTypes";

describe("hasActiveFilters", () => {
  it("is false when nothing is set", () => {
    expect(hasActiveFilters("", null, [])).toBe(false);
  });

  it("is true when the text filter has non-whitespace content", () => {
    expect(hasActiveFilters("order", null, [])).toBe(true);
  });

  it("is false when the text filter is only whitespace", () => {
    expect(hasActiveFilters("   ", null, [])).toBe(false);
  });

  it("is true when a session is pinned", () => {
    expect(hasActiveFilters("", "session-1", [])).toBe(true);
  });

  it("is true when an advanced rule is configured", () => {
    const rule: AdvancedFilterRule = { ...createFilterRule(), propertyName: "orderId", value: "ORD-1" };
    expect(hasActiveFilters("", null, [rule])).toBe(true);
  });

  it("ignores advanced rules that aren't configured yet (e.g. an added-but-empty rule)", () => {
    const rule = createFilterRule();
    expect(hasActiveFilters("", null, [rule])).toBe(false);
  });
});
