import { describe, it, expect } from "vitest";
import {
  selectBodyLanguage,
  selectBodyRenderMode,
  downloadExtension,
  PRE_MAX_BYTES,
  HIGHLIGHT_MAX_BYTES,
} from "./response-body";

describe("selectBodyLanguage", () => {
  it("recognizes JSON content types", () => {
    expect(selectBodyLanguage("application/json", "{}")).toBe("json");
    expect(selectBodyLanguage("application/json; charset=utf-8", "{}")).toBe("json");
  });

  it("recognizes JSON suffix content types", () => {
    expect(selectBodyLanguage("application/problem+json", "{}")).toBe("json");
  });

  it("sniffs JSON when no content type is declared", () => {
    expect(selectBodyLanguage(null, '  {"a":1}')).toBe("json");
    expect(selectBodyLanguage(null, "  [1,2]")).toBe("json");
  });

  it("recognizes XML and HTML", () => {
    expect(selectBodyLanguage("application/xml", "<a/>")).toBe("xml");
    expect(selectBodyLanguage("text/html", "<html>")).toBe("xml");
    expect(selectBodyLanguage(null, "<root/>")).toBe("xml");
  });

  it("sniffs a JSON body served as text/plain", () => {
    expect(selectBodyLanguage("text/plain", '{"a":1}')).toBe("json");
  });

  it("leaves genuine plain text unhighlighted", () => {
    expect(selectBodyLanguage("text/plain", "hello")).toBe("none");
  });

  it("never highlights hex-encoded binary from the sidecar", () => {
    expect(selectBodyLanguage("application/octet-stream", "89504e47")).toBe("none");
  });

  it("handles an empty body without throwing", () => {
    expect(selectBodyLanguage("application/json", "")).toBe("none");
    expect(selectBodyLanguage(null, "   ")).toBe("none");
  });
});

describe("selectBodyRenderMode", () => {
  it("uses a plain pre for small bodies", () => {
    expect(selectBodyRenderMode(0)).toBe("pre");
    expect(selectBodyRenderMode(PRE_MAX_BYTES - 1)).toBe("pre");
  });

  it("switches to CodeMirror at the pre threshold", () => {
    expect(selectBodyRenderMode(PRE_MAX_BYTES)).toBe("codemirror");
    expect(selectBodyRenderMode(HIGHLIGHT_MAX_BYTES - 1)).toBe("codemirror");
  });

  it("drops language parsing above the highlight threshold", () => {
    expect(selectBodyRenderMode(HIGHLIGHT_MAX_BYTES)).toBe("codemirror-plain");
    expect(selectBodyRenderMode(4 * 1024 * 1024)).toBe("codemirror-plain");
  });

  it("honours the user's explicit override on a large body", () => {
    expect(selectBodyRenderMode(4 * 1024 * 1024, true)).toBe("codemirror");
  });
});

describe("downloadExtension", () => {
  it("maps languages to file extensions", () => {
    expect(downloadExtension("json")).toBe("json");
    expect(downloadExtension("xml")).toBe("xml");
    expect(downloadExtension("none")).toBe("txt");
  });
});
