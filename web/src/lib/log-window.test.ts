import { describe, it, expect } from "vitest";
import {
  parseLogLine,
  timestampMs,
  formatLogTimestamp,
  filterLogEntries,
  computeLogWindow,
  windowSummary,
  mergeByTimestamp,
  type LogEntry,
} from "./log-window";

function entry(over: Partial<LogEntry> & { seq: number }): LogEntry {
  return { text: "line", ts: null, ...over };
}

describe("parseLogLine", () => {
  it("splits the RFC3339Nano prefix Kubernetes emits", () => {
    expect(parseLogLine("2026-09-09T10:22:30.118456789Z GET /health 200")).toEqual({
      ts: "2026-09-09T10:22:30.118456789Z",
      text: "GET /health 200",
    });
  });

  it("splits the space-separated prefix the demo client emits", () => {
    // Both wire shapes must parse or the parser is wrong against one of the clients.
    expect(parseLogLine("2026-09-09 10:22:30.118  [INF] started")).toEqual({
      ts: "2026-09-09 10:22:30.118",
      text: "[INF] started",
    });
  });

  it("accepts an offset in place of Z", () => {
    expect(parseLogLine("2026-09-09T10:22:30+02:00 message").text).toBe("message");
  });

  it("returns a line with no prefix untouched", () => {
    expect(parseLogLine("[INF] no timestamp here")).toEqual({
      ts: null,
      text: "[INF] no timestamp here",
    });
  });

  it("keeps the whole line when the prefix is shaped like a date but is not one", () => {
    expect(parseLogLine("2026-13-45T99:99:99Z something")).toEqual({
      ts: null,
      text: "2026-13-45T99:99:99Z something",
    });
  });

  it("handles an empty message after the timestamp", () => {
    expect(parseLogLine("2026-09-09T10:22:30Z ").text).toBe("");
  });

  it("handles an empty line", () => {
    expect(parseLogLine("")).toEqual({ ts: null, text: "" });
  });

  it("does not treat a timestamp appearing mid-line as a prefix", () => {
    const line = "[INF] job ran at 2026-09-09T10:22:30Z";
    expect(parseLogLine(line)).toEqual({ ts: null, text: line });
  });
});

describe("timestampMs", () => {
  it("is null for a line with no timestamp", () => {
    expect(timestampMs(null)).toBeNull();
  });

  it("reads both wire shapes as the same instant", () => {
    expect(timestampMs("2026-09-09T10:22:30.000Z")).toBe(Date.parse("2026-09-09T10:22:30.000Z"));
    expect(timestampMs("2026-09-09 10:22:30.000Z")).toBe(Date.parse("2026-09-09T10:22:30.000Z"));
  });
});

describe("formatLogTimestamp", () => {
  const ts = "2026-09-09T10:22:30.118Z";

  it("shows nothing when switched off", () => {
    expect(formatLogTimestamp(ts, "off")).toBe("");
  });

  it("shows the raw value in full mode", () => {
    expect(formatLogTimestamp(ts, "full")).toBe(ts);
  });

  it("keeps milliseconds in time mode, since they are what separates two pods", () => {
    expect(formatLogTimestamp(ts, "time")).toMatch(/^\d{2}:\d{2}:\d{2}\.118$/);
  });

  it("pads sub-100ms fractions", () => {
    expect(formatLogTimestamp("2026-09-09T10:22:30.007Z", "time")).toMatch(/\.007$/);
  });

  it("shows nothing for a line that has no timestamp", () => {
    expect(formatLogTimestamp(null, "time")).toBe("");
    expect(formatLogTimestamp(null, "full")).toBe("");
  });
});

describe("filterLogEntries", () => {
  const entries = [
    entry({ seq: 0, text: "GET /health 200" }),
    entry({ seq: 1, text: "POST /orders 500" }),
  ];

  it("returns everything for a blank term", () => {
    expect(filterLogEntries(entries, "   ")).toHaveLength(2);
  });

  it("matches case-insensitively on the message", () => {
    expect(filterLogEntries(entries, "HEALTH")).toHaveLength(1);
  });

  it("cannot match the timestamp, which is not part of the text", () => {
    const stamped = [entry({ seq: 0, text: "GET /health", ts: "2026-09-09T10:22:30Z" })];
    expect(filterLogEntries(stamped, "2026")).toHaveLength(0);
  });
});

describe("computeLogWindow", () => {
  it("shows the newest page by default", () => {
    expect(computeLogWindow({ total: 500, pageFromNewest: 0, visible: 200 })).toEqual({
      start: 300,
      end: 500,
      maxPage: 2,
      safePage: 0,
    });
  });

  it("steps back a whole page", () => {
    const w = computeLogWindow({ total: 500, pageFromNewest: 1, visible: 200 });
    expect([w.start, w.end]).toEqual([100, 300]);
  });

  it("clamps a page beyond the end rather than showing nothing", () => {
    const w = computeLogWindow({ total: 500, pageFromNewest: 99, visible: 200 });
    expect(w.safePage).toBe(2);
    expect([w.start, w.end]).toEqual([0, 100]);
  });

  it("handles an empty log", () => {
    expect(computeLogWindow({ total: 0, pageFromNewest: 0, visible: 200 })).toEqual({
      start: 0,
      end: 0,
      maxPage: 0,
      safePage: 0,
    });
  });

  it("handles fewer lines than one page", () => {
    const w = computeLogWindow({ total: 12, pageFromNewest: 0, visible: 200 });
    expect([w.start, w.end, w.maxPage]).toEqual([0, 12, 0]);
  });

  it("handles a total that is an exact multiple of the page size", () => {
    const w = computeLogWindow({ total: 400, pageFromNewest: 0, visible: 200 });
    expect([w.start, w.end, w.maxPage]).toEqual([200, 400, 1]);
  });

  it("never produces a negative page size", () => {
    const w = computeLogWindow({ total: 10, pageFromNewest: -5, visible: 0 });
    expect(w.start).toBeGreaterThanOrEqual(0);
    expect(w.end).toBeGreaterThanOrEqual(w.start);
  });
});

describe("windowSummary", () => {
  it("counts from one, not zero", () => {
    expect(windowSummary(300, 500, 500)).toBe("Showing 301-500 of 500");
  });

  it("says so when there is nothing", () => {
    expect(windowSummary(0, 0, 0)).toBe("0 lines");
  });
});

describe("mergeByTimestamp", () => {
  it("orders by log time, not arrival order", () => {
    // The defect this exists for: jitter delivers the earlier line second.
    const merged = mergeByTimestamp([
      entry({ seq: 0, pod: "b", text: "second", ts: "2026-09-09T10:22:31.000Z" }),
      entry({ seq: 1, pod: "a", text: "first", ts: "2026-09-09T10:22:30.000Z" }),
    ]);
    expect(merged.map((e) => e.text)).toEqual(["first", "second"]);
  });

  it("falls back to arrival order for identical timestamps", () => {
    const merged = mergeByTimestamp([
      entry({ seq: 0, pod: "a", text: "one", ts: "2026-09-09T10:22:30.000Z" }),
      entry({ seq: 1, pod: "b", text: "two", ts: "2026-09-09T10:22:30.000Z" }),
    ]);
    expect(merged.map((e) => e.text)).toEqual(["one", "two"]);
  });

  it("keeps a stack frame attached to its header rather than letting it drift", () => {
    const merged = mergeByTimestamp([
      entry({ seq: 0, pod: "a", text: "Unhandled exception", ts: "2026-09-09T10:22:30.000Z" }),
      entry({ seq: 1, pod: "a", text: "   at Foo.Bar()" }),
      entry({ seq: 2, pod: "b", text: "later", ts: "2026-09-09T10:22:31.000Z" }),
    ]);
    expect(merged.map((e) => e.text)).toEqual([
      "Unhandled exception",
      "   at Foo.Bar()",
      "later",
    ]);
  });

  it("carries the timestamp forward per pod, not globally", () => {
    // Pod a's untimestamped continuation must not inherit pod b's later time and jump
    // past its own header's neighbours.
    const merged = mergeByTimestamp([
      entry({ seq: 0, pod: "a", text: "a-header", ts: "2026-09-09T10:22:30.000Z" }),
      entry({ seq: 1, pod: "b", text: "b-line", ts: "2026-09-09T10:22:40.000Z" }),
      entry({ seq: 2, pod: "a", text: "a-frame" }),
    ]);
    expect(merged.map((e) => e.text)).toEqual(["a-header", "a-frame", "b-line"]);
  });

  it("leaves entries with no timestamps at all in arrival order", () => {
    const merged = mergeByTimestamp([
      entry({ seq: 0, text: "one" }),
      entry({ seq: 1, text: "two" }),
      entry({ seq: 2, text: "three" }),
    ]);
    expect(merged.map((e) => e.text)).toEqual(["one", "two", "three"]);
  });

  it("returns an empty list unchanged", () => {
    expect(mergeByTimestamp([])).toEqual([]);
  });
});
