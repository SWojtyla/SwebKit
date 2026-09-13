import { describe, it, expect } from "vitest";
import { clampInt } from "./clamp-int";

describe("clampInt", () => {
  it("passes through an in-range value unchanged", () => {
    expect(clampInt("30", { min: 5, max: 3600, fallback: 30 })).toEqual({
      value: 30,
      clamped: false,
      invalid: false,
    });
  });

  it("clamps a negative number up to min instead of passing it through", () => {
    // The bug this replaces: `parseInt(v) || default` only catches falsy values, so a
    // negative auto-refresh interval or database index passed straight through.
    expect(clampInt("-5", { min: 0, max: 15, fallback: 0 })).toEqual({
      value: 0,
      clamped: true,
      invalid: false,
    });
  });

  it("clamps a value above max down to max", () => {
    expect(clampInt("99", { min: 0, max: 15, fallback: 0 })).toEqual({
      value: 15,
      clamped: true,
      invalid: false,
    });
  });

  it("clamps zero up to a positive min", () => {
    expect(clampInt("0", { min: 5, max: 3600, fallback: 30 })).toEqual({
      value: 5,
      clamped: true,
      invalid: false,
    });
  });

  it("falls back and flags invalid for non-numeric input", () => {
    expect(clampInt("abc", { min: 0, max: 15, fallback: 7 })).toEqual({
      value: 7,
      clamped: false,
      invalid: true,
    });
    expect(clampInt("", { min: 0, max: 15, fallback: 7 })).toEqual({
      value: 7,
      clamped: false,
      invalid: true,
    });
  });

  it("accepts the boundary values themselves without clamping", () => {
    expect(clampInt("0", { min: 0, max: 15, fallback: 0 }).clamped).toBe(false);
    expect(clampInt("15", { min: 0, max: 15, fallback: 0 }).clamped).toBe(false);
  });

  it("has no upper bound when max is omitted", () => {
    expect(clampInt("1000000", { min: 0, fallback: 0 })).toEqual({
      value: 1_000_000,
      clamped: false,
      invalid: false,
    });
  });
});
