import { describe, it, expect } from "vitest";
import { tokenizeVariables, classifyVariable, hasUnresolvedVariables, type VariableToken } from "./variableHighlight";

const scope: Record<string, string | null> = {
  Phone_Api_Url: "https://api-dev.portima.be/apib/dev/api/notification/v1",
  Empty: "",
  ApiSecret: null,
};

function reassemble(tokens: VariableToken[]): string {
  return tokens.map((t) => t.text).join("");
}

describe("classifyVariable", () => {
  it("resolves a variable with a previewable value", () => {
    expect(classifyVariable("Phone_Api_Url", scope)).toBe("resolved");
  });

  it("treats an empty string as resolved, not missing", () => {
    expect(classifyVariable("Empty", scope)).toBe("resolved");
  });

  it("defers a variable whose value is only known at send time", () => {
    // Generated / credential-store / Key Vault values are null in the UI scope.
    expect(classifyVariable("ApiSecret", scope)).toBe("deferred");
  });

  it("reports a variable that is not in scope at all", () => {
    expect(classifyVariable("Nope", scope)).toBe("unresolved");
  });

  it("does not mistake an inherited Object property for a variable", () => {
    expect(classifyVariable("toString", scope)).toBe("unresolved");
    expect(classifyVariable("constructor", scope)).toBe("unresolved");
  });
});

describe("tokenizeVariables", () => {
  it("returns nothing for empty text", () => {
    expect(tokenizeVariables("", scope)).toEqual([]);
  });

  it("returns a single plain token when there are no variables", () => {
    expect(tokenizeVariables("https://example.com/a", scope)).toEqual([
      { text: "https://example.com/a", kind: "text" },
    ]);
  });

  it("preserves the source exactly", () => {
    const inputs = [
      "{{Phone_Api_Url}}/incomingcall?api-version=1.0",
      "prefix {{Nope}} middle {{ApiSecret}} suffix",
      "{{}}",
      "{{Phone_Api_Url}}{{Nope}}",
      "no variables here",
    ];
    for (const input of inputs) {
      expect(reassemble(tokenizeVariables(input, scope))).toBe(input);
    }
  });

  it("classifies each token in a mixed template", () => {
    const tokens = tokenizeVariables("{{Phone_Api_Url}}/x?k={{Nope}}&s={{ApiSecret}}", scope);
    expect(tokens.map((t) => t.kind)).toEqual([
      "resolved",
      "text",
      "unresolved",
      "text",
      "deferred",
    ]);
  });

  it("carries the name and resolved value for a tooltip", () => {
    const [token] = tokenizeVariables("{{Phone_Api_Url}}", scope);
    expect(token.name).toBe("Phone_Api_Url");
    expect(token.value).toBe(scope.Phone_Api_Url);
  });

  it("trims whitespace inside the braces when looking a variable up", () => {
    expect(tokenizeVariables("{{  Phone_Api_Url  }}", scope)[0].kind).toBe("resolved");
  });

  it("leaves an empty token as plain text rather than reporting a missing variable", () => {
    expect(tokenizeVariables("{{}}", scope)).toEqual([{ text: "{{}}", kind: "text" }]);
    expect(tokenizeVariables("{{   }}", scope)).toEqual([{ text: "{{   }}", kind: "text" }]);
  });

  it("leaves an unterminated token as plain text", () => {
    expect(tokenizeVariables("{{Phone_Api_Url", scope)).toEqual([
      { text: "{{Phone_Api_Url", kind: "text" },
    ]);
  });
});

describe("hasUnresolvedVariables", () => {
  it("is true only when a variable is genuinely missing", () => {
    expect(hasUnresolvedVariables("{{Phone_Api_Url}}/x", scope)).toBe(false);
    expect(hasUnresolvedVariables("{{ApiSecret}}", scope)).toBe(false);
    expect(hasUnresolvedVariables("{{Nope}}", scope)).toBe(true);
    expect(hasUnresolvedVariables("plain", scope)).toBe(false);
  });
});
