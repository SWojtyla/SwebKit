import { describe, it, expect } from "vitest";
import { formatBytes, formatElapsed } from "./api-client-format";

describe("formatBytes", () => {
  it("renders the sidecar's unknown-length sentinel as a dash", () => {
    expect(formatBytes(-1)).toBe("—");
  });

  it("renders zero explicitly", () => {
    expect(formatBytes(0)).toBe("0 B");
  });

  it("renders sub-kilobyte values as whole bytes", () => {
    expect(formatBytes(512)).toBe("512 B");
  });

  it("renders kilobytes with one decimal", () => {
    expect(formatBytes(1536)).toBe("1.5 kB");
  });

  it("renders the 4 MB sidecar body cap", () => {
    expect(formatBytes(4 * 1024 * 1024)).toBe("4.0 MB");
  });

  it("handles non-finite input without throwing", () => {
    expect(formatBytes(Number.NaN)).toBe("—");
    expect(formatBytes(Number.POSITIVE_INFINITY)).toBe("—");
  });
});

describe("formatElapsed", () => {
  it("keeps sub-second timings in whole milliseconds", () => {
    expect(formatElapsed(190)).toBe("190 ms");
    expect(formatElapsed(999)).toBe("999 ms");
  });

  it("renders zero as milliseconds, not a dash", () => {
    expect(formatElapsed(0)).toBe("0 ms");
  });

  it("switches to seconds at one second", () => {
    expect(formatElapsed(1000)).toBe("1.0 s");
    expect(formatElapsed(2400)).toBe("2.4 s");
  });

  it("switches to minutes at one minute", () => {
    expect(formatElapsed(60_000)).toBe("1m 0s");
    expect(formatElapsed(65_000)).toBe("1m 5s");
  });

  it("carries rather than rendering 60 seconds", () => {
    expect(formatElapsed(119_800)).toBe("2m 0s");
  });
});
