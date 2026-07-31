import { describe, it, expect } from "vitest";
import { tokenizeJson, tokenizeXml, tokenizeBody, type BodyToken } from "./bodyHighlight";

/** Tokenizing must never lose or alter a character of the source. */
function reassemble(tokens: BodyToken[]): string {
  return tokens.map((t) => t.text).join("");
}

function classOf(tokens: BodyToken[], text: string): string | undefined {
  return tokens.find((t) => t.text === text)?.cls;
}

describe("tokenizeJson", () => {
  it("distinguishes keys from string values", () => {
    const tokens = tokenizeJson('{"name":"value"}');
    expect(classOf(tokens, '"name"')).toBe("key");
    expect(classOf(tokens, '"value"')).toBe("string");
  });

  it("treats a key followed by whitespace before the colon as a key", () => {
    const tokens = tokenizeJson('{"name"  : 1}');
    expect(classOf(tokens, '"name"')).toBe("key");
  });

  it("classifies numbers, booleans and null", () => {
    const tokens = tokenizeJson('{"a":1,"b":-2.5e3,"c":true,"d":false,"e":null}');
    expect(classOf(tokens, "1")).toBe("number");
    expect(classOf(tokens, "-2.5e3")).toBe("number");
    expect(classOf(tokens, "true")).toBe("bool");
    expect(classOf(tokens, "false")).toBe("bool");
    expect(classOf(tokens, "null")).toBe("null");
  });

  it("classifies structural punctuation", () => {
    const tokens = tokenizeJson("{}[],:");
    expect(tokens.every((t) => t.cls === "punct")).toBe(true);
  });

  it("handles escaped quotes inside strings", () => {
    const tokens = tokenizeJson('{"a":"say \\"hi\\""}');
    expect(classOf(tokens, '"say \\"hi\\""')).toBe("string");
    expect(reassemble(tokens)).toBe('{"a":"say \\"hi\\""}');
  });

  it("does not hang on an unterminated string", () => {
    const tokens = tokenizeJson('{"a":"unterminated');
    expect(reassemble(tokens)).toBe('{"a":"unterminated');
  });

  it("preserves the source exactly", () => {
    const src = '{\n  "list": [1, 2, {"nested": null}],\n  "flag": true\n}';
    expect(reassemble(tokenizeJson(src))).toBe(src);
  });

  it("handles an empty document", () => {
    expect(tokenizeJson("")).toEqual([]);
  });
});

describe("tokenizeXml", () => {
  it("classifies tag names, attributes and values", () => {
    const tokens = tokenizeXml('<root id="1">text</root>');
    expect(classOf(tokens, "root")).toBe("tag");
    expect(classOf(tokens, "id")).toBe("attr");
    expect(classOf(tokens, '"1"')).toBe("string");
    expect(classOf(tokens, "text")).toBe("plain");
  });

  it("classifies comments", () => {
    const tokens = tokenizeXml("<!-- a note --><a/>");
    expect(classOf(tokens, "<!-- a note -->")).toBe("comment");
  });

  it("handles self-closing and closing tags", () => {
    const src = "<a><b/></a>";
    expect(reassemble(tokenizeXml(src))).toBe(src);
  });

  it("handles a declaration", () => {
    const tokens = tokenizeXml('<?xml version="1.0"?>');
    expect(classOf(tokens, "xml")).toBe("tag");
    expect(reassemble(tokens)).toBe('<?xml version="1.0"?>');
  });

  it("does not hang on an unterminated tag", () => {
    const src = "<root id=";
    expect(reassemble(tokenizeXml(src))).toBe(src);
  });

  it("does not hang on an unterminated comment", () => {
    const src = "<!-- never closed";
    expect(reassemble(tokenizeXml(src))).toBe(src);
  });

  it("preserves the source exactly", () => {
    const src = '<?xml version="1.0"?>\n<root>\n  <item id="1">a b</item>\n</root>';
    expect(reassemble(tokenizeXml(src))).toBe(src);
  });
});

describe("tokenizeBody", () => {
  it("dispatches on language", () => {
    expect(tokenizeBody('{"a":1}', "json").some((t) => t.cls === "key")).toBe(true);
    expect(tokenizeBody("<a/>", "xml").some((t) => t.cls === "tag")).toBe(true);
  });

  it("returns a single plain token for unknown languages", () => {
    expect(tokenizeBody("hello world", "none")).toEqual([{ text: "hello world", cls: "plain" }]);
  });
});
