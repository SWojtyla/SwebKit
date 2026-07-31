import { describe, it, expect } from "vitest";
import { resolvePanelWidths, resizePair, parseFraction, parseFixed } from "./resizable-widths";

describe("parseFraction", () => {
  it("parses fraction specs", () => {
    expect(parseFraction("1fr")).toBe(1);
    expect(parseFraction("2fr")).toBe(2);
    expect(parseFraction("0.5fr")).toBe(0.5);
  });

  it("treats null as a single share", () => {
    expect(parseFraction(null)).toBe(1);
    expect(parseFraction(undefined)).toBe(1);
  });

  it("returns null for fixed specs", () => {
    expect(parseFraction(300)).toBeNull();
    expect(parseFraction("300px")).toBeNull();
    expect(parseFraction("25%")).toBeNull();
  });
});

describe("parseFixed", () => {
  it("parses numbers, px and percentages", () => {
    expect(parseFixed(300, 1000)).toBe(300);
    expect(parseFixed("260px", 1000)).toBe(260);
    expect(parseFixed("25%", 1000)).toBe(250);
  });
});

describe("resolvePanelWidths", () => {
  const minWidths = [220, 420, 380];

  it("splits leftover space evenly between equal fractions", () => {
    const widths = resolvePanelWidths({
      specs: [300, "1fr", "1fr"],
      containerWidth: 1000,
      minWidths: [220, 300, 300],
      handlesWidth: 12,
    });
    expect(widths[0]).toBe(300);
    // (1000 - 12 - 300) / 2 = 344 each
    expect(widths[1]).toBe(344);
    expect(widths[2]).toBe(344);
    expect(widths[0] + widths[1] + widths[2]).toBe(988);
  });

  it("weights unequal fractions", () => {
    const widths = resolvePanelWidths({
      specs: [300, "2fr", "1fr"],
      containerWidth: 1200,
      minWidths: [220, 100, 100],
      handlesWidth: 12,
    });
    const leftover = 1200 - 12 - 300;
    expect(widths[1]).toBe(Math.round((leftover * 2) / 3));
    expect(widths[2]).toBe(Math.round(leftover / 3));
  });

  /** The original complaint: on a wide window the response pane took everything. */
  it("keeps request and response comparable on a wide container", () => {
    const widths = resolvePanelWidths({
      specs: [300, "1fr", "1fr"],
      containerWidth: 1920,
      minWidths,
      handlesWidth: 12,
    });
    expect(Math.abs(widths[1] - widths[2])).toBeLessThanOrEqual(1);
    expect(widths[1]).toBeGreaterThan(700);
  });

  it("never collapses a panel below its minimum", () => {
    const widths = resolvePanelWidths({
      specs: [300, "1fr", "1fr"],
      containerWidth: 700,
      minWidths,
      handlesWidth: 12,
    });
    expect(widths[0]).toBeGreaterThanOrEqual(220);
    expect(widths[1]).toBeGreaterThanOrEqual(420);
    expect(widths[2]).toBeGreaterThanOrEqual(380);
  });

  it("treats a null spec as a fraction", () => {
    const widths = resolvePanelWidths({
      specs: [300, null, null],
      containerWidth: 1000,
      minWidths: [220, 100, 100],
      handlesWidth: 12,
    });
    expect(widths[1]).toBe(widths[2]);
  });

  /** Backwards compatibility for any other caller passing the legacy pixel form. */
  it("still resolves the legacy numeric-only form", () => {
    const widths = resolvePanelWidths({
      specs: [260, 540, null],
      containerWidth: 1400,
      minWidths: [180, 360, 260],
      handlesWidth: 12,
    });
    expect(widths[0]).toBe(260);
    expect(widths[1]).toBe(540);
    expect(widths[2]).toBe(1400 - 12 - 260 - 540);
  });

  it("returns fixed widths untouched when there are no fractions", () => {
    const widths = resolvePanelWidths({
      specs: [300, 400],
      containerWidth: 1000,
      minWidths: [100, 100],
      handlesWidth: 6,
    });
    expect(widths).toEqual([300, 400]);
  });
});

describe("resizePair", () => {
  it("moves width from the right panel to the left", () => {
    expect(resizePair(500, 500, 100, 200, 200)).toEqual([600, 400]);
  });

  it("moves width from the left panel to the right", () => {
    expect(resizePair(500, 500, -100, 200, 200)).toEqual([400, 600]);
  });

  it("clamps at the left minimum", () => {
    expect(resizePair(500, 500, -400, 200, 200)).toEqual([200, 800]);
  });

  it("clamps at the right minimum", () => {
    expect(resizePair(500, 500, 400, 200, 200)).toEqual([800, 200]);
  });

  it("preserves the combined width", () => {
    const [left, right] = resizePair(300, 700, 137, 100, 100);
    expect(left + right).toBe(1000);
  });

  it("leaves an unsatisfiable pair alone rather than producing garbage", () => {
    expect(resizePair(100, 100, 50, 200, 200)).toEqual([100, 100]);
  });
});
