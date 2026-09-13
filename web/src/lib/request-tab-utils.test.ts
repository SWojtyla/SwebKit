import { describe, it, expect } from "vitest";
import type { RequestTab } from "@/components/api-client/RequestTabStrip";
import { pickNeighborTabId } from "./request-tab-utils";

function tab(id: string, overrides: Partial<RequestTab> = {}): RequestTab {
  return { id, nodeId: id, collectionId: "col-1", name: id, method: "Get", dirty: false, ...overrides };
}

describe("pickNeighborTabId", () => {
  it("picks the tab that slides into the closed tab's slot", () => {
    const tabs = [tab("a"), tab("b"), tab("c")];
    expect(pickNeighborTabId(tabs, "b")).toBe("c");
  });

  it("falls back to the previous tab when closing the last one", () => {
    const tabs = [tab("a"), tab("b"), tab("c")];
    expect(pickNeighborTabId(tabs, "c")).toBe("b");
  });

  it("returns null when no tabs remain", () => {
    const tabs = [tab("only")];
    expect(pickNeighborTabId(tabs, "only")).toBeNull();
  });

  it("returns null when the closing id is not present", () => {
    const tabs = [tab("a"), tab("b")];
    expect(pickNeighborTabId(tabs, "missing")).toBeNull();
  });

  it("does not disturb an inactive tab's neighbor when closing a middle tab", () => {
    // Closing "a" (the first tab) should still resolve a sensible neighbor,
    // not silently fall through to null.
    const tabs = [tab("a"), tab("b"), tab("c")];
    expect(pickNeighborTabId(tabs, "a")).toBe("b");
  });
});
