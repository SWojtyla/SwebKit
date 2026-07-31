import { describe, it, expect } from "vitest";
import { METHOD_META, methodMeta, statusTone } from "./method-badge";
import type { ApiRequestMethod } from "@/lib/types";

const ALL_METHODS: ApiRequestMethod[] = [
  "Get", "Post", "Put", "Patch", "Delete", "Head", "Options", "GraphQl", "WebSocket",
];

describe("METHOD_META", () => {
  it("covers every ApiRequestMethod", () => {
    for (const method of ALL_METHODS) {
      expect(METHOD_META[method], `missing entry for ${method}`).toBeDefined();
    }
    expect(Object.keys(METHOD_META)).toHaveLength(ALL_METHODS.length);
  });

  it("uses conventional short labels", () => {
    expect(METHOD_META.Delete.short).toBe("DEL");
    expect(METHOD_META.Options.short).toBe("OPT");
    expect(METHOD_META.GraphQl.short).toBe("GQL");
    expect(METHOD_META.WebSocket.short).toBe("WS");
    expect(METHOD_META.Patch.short).toBe("PATCH");
  });

  /**
   * Regression guard for `method.toUpperCase().slice(0, 4)`, which produced DELE,
   * PATC, OPTI, GRAP and WEBS — abbreviations that read as rendering bugs.
   */
  it("never renders a truncated word", () => {
    const truncated = ["DELE", "PATC", "OPTI", "GRAP", "WEBS"];
    for (const method of ALL_METHODS) {
      expect(truncated).not.toContain(METHOD_META[method].short);
    }
  });

  it("maps methods onto the shared tone vocabulary", () => {
    expect(METHOD_META.Get.tone).toBe("info");
    expect(METHOD_META.Post.tone).toBe("success");
    expect(METHOD_META.Delete.tone).toBe("destructive");
    expect(METHOD_META.Head.tone).toBe("neutral");
    expect(METHOD_META.Options.tone).toBe("neutral");
  });
});

describe("methodMeta", () => {
  it("falls back for unknown strings, since tab state carries a plain string", () => {
    expect(methodMeta("NotAMethod").short).toBe("?");
    expect(methodMeta("NotAMethod").tone).toBe("neutral");
  });

  it("resolves known methods", () => {
    expect(methodMeta("Get").short).toBe("GET");
  });
});

describe("statusTone", () => {
  it("maps status classes onto tones", () => {
    expect(statusTone(200)).toBe("success");
    expect(statusTone(204)).toBe("success");
    expect(statusTone(301)).toBe("info");
    expect(statusTone(404)).toBe("warning");
    expect(statusTone(500)).toBe("destructive");
  });

  it("treats the transport-failure sentinel as destructive", () => {
    expect(statusTone(0)).toBe("destructive");
  });
});
