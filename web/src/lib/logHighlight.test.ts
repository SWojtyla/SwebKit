import { describe, it, expect } from "vitest";
import { tokenizeLogLine, type LogToken } from "./logHighlight";

/** Tokenizing must never lose or alter a character of the source. */
function reassemble(tokens: LogToken[]): string {
  return tokens.map((t) => t.text).join("");
}

function classOf(tokens: LogToken[], text: string): string | undefined {
  return tokens.find((t) => t.text === text)?.cls;
}

function classesIn(tokens: LogToken[]): string[] {
  return tokens.map((t) => t.cls);
}

describe("tokenizeLogLine", () => {
  it("returns nothing for an empty line", () => {
    expect(tokenizeLogLine("")).toEqual([]);
  });

  it("preserves the source exactly", () => {
    const lines = [
      "Portima.PhoneNotification.Api.Repository.Clients.BrioGraphQl.CreateTokenException: Not able to create a token for users: [",
      '   "Response": "{\u0022status\u0022:400}",',
      "   at Portima.Brio.GraphQL.Security.TokenCreator.CallTokenApiAsync(Uri uri, String body, String sessionId)",
      "--- End of stack trace from previous location ---",
      "2026-09-08T11:12:13.9556368Z [WRN] retrying in 5s",
      "plain text with no tokens at all",
    ];
    for (const line of lines) {
      expect(reassemble(tokenizeLogLine(line))).toBe(line);
    }
  });

  it("classifies a .NET exception header", () => {
    const tokens = tokenizeLogLine(
      "Portima.PhoneNotification.Api.Repository.Clients.BrioGraphQl.CreateTokenException: Not able to create a token",
    );
    expect(
      classOf(tokens, "Portima.PhoneNotification.Api.Repository.Clients.BrioGraphQl.CreateTokenException"),
    ).toBe("exception");
  });

  it("classifies a dotted type that is not an exception as a symbol", () => {
    const tokens = tokenizeLogLine("Portima.Brio.GraphQL.Security.TokenCreator.CallTokenApiAsync(Uri uri)");
    expect(classOf(tokens, "Portima.Brio.GraphQL.Security.TokenCreator.CallTokenApiAsync")).toBe("symbol");
  });

  it("classifies a bare exception type name", () => {
    expect(classOf(tokenizeLogLine("threw TokenApiException here"), "TokenApiException")).toBe("exception");
  });

  it("marks the leading `at ` of a stack frame as a keyword", () => {
    const tokens = tokenizeLogLine("   at Portima.Brio.Security.TokenCreator.CreateTokenAsync()");
    expect(tokens[0]).toEqual({ text: "   ", cls: "plain" });
    expect(tokens[1]).toEqual({ text: "at ", cls: "keyword" });
  });

  it("does not treat `at` in prose as a stack frame", () => {
    const tokens = tokenizeLogLine("Request arrived at the gateway");
    expect(classesIn(tokens)).not.toContain("keyword");
  });

  it("highlights source locations including the line number", () => {
    const tokens = tokenizeLogLine(
      "   at Foo.Bar() in /mnt/vss/_work/1/s/src/Clients/BrioGraphQlTokenProvider.cs:line 141",
    );
    expect(classOf(tokens, "/mnt/vss/_work/1/s/src/Clients/BrioGraphQlTokenProvider.cs:line 141")).toBe(
      "location",
    );
  });

  it("highlights a Windows source location", () => {
    const tokens = tokenizeLogLine(String.raw`   at Foo.Bar() in C:\src\App\Program.cs:line 12`);
    expect(classOf(tokens, String.raw`C:\src\App\Program.cs:line 12`)).toBe("location");
  });

  it("classifies the inner-exception arrow as a keyword", () => {
    expect(classOf(tokenizeLogLine(" ---> System.AggregateException: boom"), "--->")).toBe("keyword");
  });

  it("classifies an end-of-trace rule as a separator, not a keyword", () => {
    // Scaffolding between frame groups: it should recede rather than compete with
    // the exception header for attention.
    expect(
      classOf(
        tokenizeLogLine("--- End of stack trace from previous location ---"),
        "--- End of stack trace from previous location ---",
      ),
    ).toBe("separator");
  });

  it("classifies timestamps in both ISO and bare-clock form", () => {
    expect(classOf(tokenizeLogLine("2026-09-08T11:12:13.9556368Z ready"), "2026-09-08T11:12:13.9556368Z")).toBe(
      "timestamp",
    );
    expect(classOf(tokenizeLogLine("2026-09-08 11:12:13+02:00 ready"), "2026-09-08 11:12:13+02:00")).toBe(
      "timestamp",
    );
    expect(classOf(tokenizeLogLine("11:12:13.955 ready"), "11:12:13.955")).toBe("timestamp");
  });

  it("maps each level spelling onto a severity class", () => {
    expect(classOf(tokenizeLogLine("[ERR] boom"), "[ERR]")).toBe("level-error");
    expect(classOf(tokenizeLogLine("[FATAL] boom"), "[FATAL]")).toBe("level-error");
    expect(classOf(tokenizeLogLine("[WRN] careful"), "[WRN]")).toBe("level-warn");
    expect(classOf(tokenizeLogLine("WARNING careful"), "WARNING")).toBe("level-warn");
    expect(classOf(tokenizeLogLine("[INF] fine"), "[INF]")).toBe("level-info");
    expect(classOf(tokenizeLogLine("[DBG] noisy"), "[DBG]")).toBe("level-debug");
    expect(classOf(tokenizeLogLine("TRACE noisy"), "TRACE")).toBe("level-debug");
  });

  it("distinguishes JSON keys from string values", () => {
    const tokens = tokenizeLogLine('  "OfficeId": "29998",');
    expect(classOf(tokens, '"OfficeId"')).toBe("key");
    expect(classOf(tokens, '"29998"')).toBe("string");
    expect(classOf(tokens, ":")).toBe("punct");
  });

  it("classifies numbers, booleans and null in a JSON payload", () => {
    const tokens = tokenizeLogLine('{"StatusCode": 400, "ok": false, "body": null}');
    expect(classOf(tokens, "400")).toBe("number");
    expect(classOf(tokens, "false")).toBe("bool");
    expect(classOf(tokens, "null")).toBe("null");
  });

  it("classifies URLs and GUIDs", () => {
    const tokens = tokenizeLogLine(
      "POST https://exts-stgidp-cloud.portima.be/Pi2/Ip?x=1 id 2814a5f34-8692-46d5-9639-2f67e655294 done",
    );
    expect(classOf(tokens, "https://exts-stgidp-cloud.portima.be/Pi2/Ip?x=1")).toBe("url");

    const guid = "2814a5f3-8692-46d5-9639-2f67e6552945";
    expect(classOf(tokenizeLogLine(`guid ${guid} done`), guid)).toBe("guid");
  });

  it("does not split a URL at its scheme colon", () => {
    const tokens = tokenizeLogLine("see https://example.com/a.cs:line for details");
    expect(classOf(tokens, "https://example.com/a.cs:line")).toBe("url");
  });

  it("leaves an oversized line untouched rather than scanning it", () => {
    const huge = `x${"a".repeat(5000)}`;
    expect(tokenizeLogLine(huge)).toEqual([{ text: huge, cls: "plain" }]);
  });

  it("merges adjacent unclassified runs into a single plain token", () => {
    const tokens = tokenizeLogLine("just some ordinary words here");
    expect(tokens).toEqual([{ text: "just some ordinary words here", cls: "plain" }]);
  });
});
