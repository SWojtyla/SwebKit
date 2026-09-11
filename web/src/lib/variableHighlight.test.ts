import { describe, it, expect } from "vitest";
import {
  tokenizeVariables,
  classifyVariable,
  hasUnresolvedVariables,
  unresolvedVariableNames,
  describeVariableToken,
  variableMarkClass,
  variableHoverAt,
  type VariableToken,
} from "./variableHighlight";

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

describe("unresolvedVariableNames", () => {
  it("names only the variables that are missing", () => {
    expect(
      unresolvedVariableNames("{{Phone_Api_Url}} {{ApiSecret}} {{Nope}}", scope),
    ).toEqual(["Nope"]);
  });

  it("reports a repeated name once, in first-seen order", () => {
    expect(unresolvedVariableNames("{{B}} {{A}} {{B}}", scope)).toEqual(["B", "A"]);
  });

  it("is empty for text with nothing missing", () => {
    expect(unresolvedVariableNames("plain text", scope)).toEqual([]);
  });
});

describe("describeVariableToken", () => {
  const describe1 = (text: string) => describeVariableToken(tokenizeVariables(text, scope)[0]);

  it("states the value of a resolved variable", () => {
    expect(describe1("{{Empty}}")).toBe("Empty = ");
  });

  it("says a missing variable is not defined", () => {
    expect(describe1("{{Nope}}")).toBe("Nope — not defined");
  });

  it("says a deferred variable resolves at send time rather than calling it missing", () => {
    expect(describe1("{{ApiSecret}}")).toBe("ApiSecret — resolved when sent");
  });

  it("masks a value whose name looks like a secret", () => {
    expect(describeVariableToken({ text: "{{x}}", kind: "resolved", name: "MyToken", value: "hunter2" }))
      .toBe("MyToken = ••••••••");
  });

  it("describes nothing for a plain-text run", () => {
    expect(describeVariableToken({ text: "plain", kind: "text" })).toBeNull();
  });
});

describe("variableMarkClass", () => {
  it("maps each state onto its stylesheet class", () => {
    expect(variableMarkClass("Phone_Api_Url", scope)).toBe("var-tok-resolved");
    expect(variableMarkClass("ApiSecret", scope)).toBe("var-tok-deferred");
    expect(variableMarkClass("Nope", scope)).toBe("var-tok-unresolved");
  });

  it("tolerates padding inside the braces", () => {
    expect(variableMarkClass("  Phone_Api_Url  ", scope)).toBe("var-tok-resolved");
  });

  it("declines to mark a token that names nothing", () => {
    expect(variableMarkClass("", scope)).toBeNull();
    expect(variableMarkClass("   ", scope)).toBeNull();
  });
});

describe("variableHoverAt", () => {
  const line = `  "sp": "{{AUTH_SP}}",`;
  const hoverScope: Record<string, string | null> = { AUTH_SP: "brio" };

  it("finds the token containing the offset and reports its bounds", () => {
    expect(variableHoverAt(line, line.indexOf("AUTH_SP"), hoverScope)).toEqual({
      from: line.indexOf("{{AUTH_SP}}"),
      to: line.indexOf("{{AUTH_SP}}") + "{{AUTH_SP}}".length,
      text: "AUTH_SP = brio",
    });
  });

  it("covers the braces as well as the name", () => {
    const start = line.indexOf("{{AUTH_SP}}");
    expect(variableHoverAt(line, start, hoverScope)).not.toBeNull();
  });

  it("treats the closing brace boundary as outside the token", () => {
    const end = line.indexOf("{{AUTH_SP}}") + "{{AUTH_SP}}".length;
    expect(variableHoverAt(line, end, hoverScope)).toBeNull();
  });

  it("returns nothing for an offset in plain text", () => {
    expect(variableHoverAt(line, 3, hoverScope)).toBeNull();
  });

  it("picks the right token when a line holds several", () => {
    const two = "{{A}}-{{B}}";
    expect(variableHoverAt(two, 7, { A: "1", B: "2" })?.text).toBe("B = 2");
  });

  it("describes a missing variable, which is the case that matters", () => {
    expect(variableHoverAt("{{AUTH_SP}}", 4, {})?.text).toBe("AUTH_SP — not defined");
  });
});
