import { describe, it, expect } from "vitest";
import { filterAndSortHistory } from "./historyFilterSort";
import type { AlertFiredEvent, AlertHistoryEntry } from "../../lib/api";

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

  // monitoring-closed-loop 4a: the panel now consumes durable AlertHistoryEntry rows
  // (Fired/Suppressed/Resolved) — the filter/sort must work on that shape too.
  it("works on durable AlertHistoryEntry rows of every kind", () => {
    const entries: AlertHistoryEntry[] = [
      { id: "1", ruleId: "a", ruleName: "A", source: "AksPodHealth", severity: "Warning", kind: "Fired", at: "2026-01-03T00:00:00Z", message: "m" },
      { id: "2", ruleId: "b", ruleName: "B", source: "AksPodHealth", severity: "Critical", kind: "Resolved", at: "2026-01-02T00:00:00Z", message: "m" },
      { id: "3", ruleId: "c", ruleName: "C", source: "AksPodHealth", severity: "Warning", kind: "Suppressed", at: "2026-01-01T00:00:00Z", message: "m" },
    ];

    expect(filterAndSortHistory(entries, "Critical", "time").map((e) => e.id)).toEqual(["2"]);
    expect(filterAndSortHistory(entries, "All", "severity").map((e) => e.id)).toEqual(["2", "1", "3"]);
  });
});
