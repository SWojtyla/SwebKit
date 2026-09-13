import { describe, it, expect } from "vitest";
import { filterAndSortHistory } from "./historyFilterSort";
import type { AlertFiredEvent } from "../../lib/api";

function event(overrides: Partial<AlertFiredEvent>): AlertFiredEvent {
  return {
    ruleId: "r1",
    ruleName: "Rule",
    source: "AksPodHealth",
    severity: "Warning",
    message: "msg",
    detail: "",
    firedAt: "2026-01-01T00:00:00Z",
    profileName: "default",
    ...overrides,
  };
}

describe("filterAndSortHistory", () => {
  const events = [
    event({ ruleId: "a", severity: "Warning", firedAt: "2026-01-03T00:00:00Z" }),
    event({ ruleId: "b", severity: "Critical", firedAt: "2026-01-02T00:00:00Z" }),
    event({ ruleId: "c", severity: "Warning", firedAt: "2026-01-01T00:00:00Z" }),
    event({ ruleId: "d", severity: "Critical", firedAt: "2026-01-04T00:00:00Z" }),
  ];

  it("returns events unchanged when filter is All and sort is time", () => {
    expect(filterAndSortHistory(events, "All", "time")).toEqual(events);
  });

  it("filters down to a single severity", () => {
    const result = filterAndSortHistory(events, "Critical", "time");
    expect(result.map((e) => e.ruleId)).toEqual(["b", "d"]);
  });

  it("sorts Critical before Warning while preserving relative (time) order within each severity", () => {
    const result = filterAndSortHistory(events, "All", "severity");
    expect(result.map((e) => e.ruleId)).toEqual(["b", "d", "a", "c"]);
  });

  it("combines filter and sort", () => {
    const result = filterAndSortHistory(events, "Warning", "severity");
    expect(result.map((e) => e.ruleId)).toEqual(["a", "c"]);
  });

  it("does not mutate the input array", () => {
    const copy = [...events];
    filterAndSortHistory(events, "All", "severity");
    expect(events).toEqual(copy);
  });
});
