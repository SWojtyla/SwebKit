import { describe, it, expect } from "vitest";
import { tryPrettifyJson } from "./pretty-json";

describe("tryPrettifyJson", () => {
  it("indents a minified object", () => {
    expect(tryPrettifyJson('{"a":1,"b":{"c":2}}')).toBe(
      '{\n  "a": 1,\n  "b": {\n    "c": 2\n  }\n}',
    );
  });

  it("indents an array root", () => {
    expect(tryPrettifyJson("[1,2]")).toBe("[\n  1,\n  2\n]");
  });

  it("formats JSON regardless of the extension or content type it arrived under", () => {
    // The reason this sniffs content: log blobs named *.txt hold JSON payloads.
    expect(tryPrettifyJson('{"requestForQuotation":{"officeId":"22909"}}')).toContain("\n  ");
  });

  it("tolerates a leading UTF-8 BOM", () => {
    expect(tryPrettifyJson(`${String.fromCharCode(0xfeff)}{"a":1}`)).toBe('{\n  "a": 1\n}');
  });

  it("tolerates surrounding whitespace", () => {
    expect(tryPrettifyJson('\n  {"a":1}\n')).toBe('{\n  "a": 1\n}');
  });

  it("returns null for plain text", () => {
    expect(tryPrettifyJson("2026-04-22 032145 Handler started")).toBeNull();
  });

  it("returns null for XML", () => {
    expect(tryPrettifyJson("<?xml version=\"1.0\"?><root />")).toBeNull();
  });

  it("returns null for a scalar root, which JSON.parse would otherwise accept", () => {
    expect(tryPrettifyJson("42")).toBeNull();
    expect(tryPrettifyJson('"just a string"')).toBeNull();
  });

  it("returns null for truncated JSON rather than throwing", () => {
    // The content endpoint caps large blobs, so a half-object is a routine input.
    expect(tryPrettifyJson('{"a":1,"b":{"c":')).toBeNull();
  });

  it("returns null for empty content", () => {
    expect(tryPrettifyJson("")).toBeNull();
  });
});
