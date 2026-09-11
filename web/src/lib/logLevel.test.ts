import { describe, it, expect } from "vitest";
import { getLogLineClass } from "./logLevel";

describe("getLogLineClass", () => {
  it("classifies by severity keyword", () => {
    expect(getLogLineClass("2026-09-08 [ERR] boom")).toBe("log-level-error");
    expect(getLogLineClass("CRITICAL failure")).toBe("log-level-error");
    expect(getLogLineClass("2026-09-08 [WRN] careful")).toBe("log-level-warn");
    expect(getLogLineClass("[DBG] noisy")).toBe("log-level-debug");
    expect(getLogLineClass("just a message")).toBe("log-level-default");
  });

  it("leaves JSON lines uncoloured so their tokens carry the meaning", () => {
    expect(getLogLineClass('{"level":"ERROR"}')).toBe("log-level-default");
  });

  it("dims stack frames and end-of-trace separators", () => {
    expect(getLogLineClass("   at Portima.Brio.Security.TokenCreator.CreateTokenAsync()")).toBe(
      "log-level-frame",
    );
    expect(getLogLineClass("at Foo.Bar()")).toBe("log-level-frame");
    expect(getLogLineClass("--- End of inner exception stack trace ---")).toBe("log-level-frame");
  });

  it("does not dim a message that merely starts with the word at", () => {
    expect(getLogLineClass("at last the queue drained")).toBe("log-level-default");
  });

  it("dims a frame even when the trace mentions an error keyword", () => {
    // A 40-frame trace under one exception header should not render as 40 red lines.
    expect(getLogLineClass("   at Foo.HandleError(String message)")).toBe("log-level-frame");
  });

  it("returns the default class for an empty line", () => {
    expect(getLogLineClass("")).toBe("log-level-default");
  });
});
