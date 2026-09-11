import { describe, it, expect } from "vitest";
import { formatBytes, formatBytesLong } from "./format-bytes";

describe("formatBytes", () => {
  it("renders whole bytes below 1K", () => {
    expect(formatBytes(0)).toBe("0B");
    expect(formatBytes(842)).toBe("842B");
    expect(formatBytes(1023)).toBe("1023B");
  });

  it("switches unit at each 1024 boundary", () => {
    expect(formatBytes(1024)).toBe("1.0K");
    expect(formatBytes(1024 * 1024)).toBe("1.0M");
    expect(formatBytes(1024 * 1024 * 1024)).toBe("1.00G");
  });

  it("renders a dash for an unknown size", () => {
    // Virtual folder rows carry no size.
    expect(formatBytes(null)).toBe("-");
    expect(formatBytes(undefined)).toBe("-");
  });

  it("keeps multi-gigabyte values readable rather than showing thousands of M", () => {
    expect(formatBytes(2 * 1024 * 1024 * 1024)).toBe("2.00G");
  });
});

describe("formatBytesLong", () => {
  it("spaces the unit and spells it out", () => {
    expect(formatBytesLong(842)).toBe("842 B");
    expect(formatBytesLong(1536)).toBe("1.5 KB");
    expect(formatBytesLong(1024 * 1024)).toBe("1.0 MB");
    expect(formatBytesLong(1024 * 1024 * 1024)).toBe("1.00 GB");
  });

  it("renders a dash for an unknown size", () => {
    expect(formatBytesLong(null)).toBe("-");
  });
});
