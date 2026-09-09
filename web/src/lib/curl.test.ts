import { describe, it, expect } from "vitest";
import { buildCurl } from "./curl";
import type { HttpRequestEntry } from "./types";

const scope: Record<string, string | null> = {
  AUTH_SP: "brio",
  AUTH_OFFICE_ID: "15000",
  VAULT_TOKEN: null,
};

function request(overrides: Partial<HttpRequestEntry> = {}): HttpRequestEntry {
  return {
    method: "Post",
    headers: [],
    body: { mode: "Json", rawContent: null, contentType: null },
    ...overrides,
  } as HttpRequestEntry;
}

describe("buildCurl", () => {
  it("substitutes variables in the body", () => {
    const curl = buildCurl(
      request({ body: { mode: "Json", rawContent: '{"sp":"{{AUTH_SP}}"}' } as HttpRequestEntry["body"] }),
      "https://api-dev.example.be/v1/auth/token/",
      scope,
    );
    expect(curl).toContain(`-d '{"sp":"brio"}'`);
    expect(curl).not.toContain("{{AUTH_SP}}");
  });

  it("substitutes variables in header values but never in header names", () => {
    const curl = buildCurl(
      request({
        headers: [{ key: "X-Office", value: "{{AUTH_OFFICE_ID}}", isEnabled: true }] as HttpRequestEntry["headers"],
      }),
      "https://example.test/",
      scope,
    );
    expect(curl).toContain(`-H "X-Office: 15000"`);
  });

  it("leaves a variable that is only known at send time as its token", () => {
    // Key Vault and credential-store values are deliberately null in the UI scope,
    // so the panel shows the token rather than expanding a secret into the clipboard.
    const curl = buildCurl(
      request({ body: { mode: "Json", rawContent: '{"t":"{{VAULT_TOKEN}}"}' } as HttpRequestEntry["body"] }),
      "https://example.test/",
      scope,
    );
    expect(curl).toContain("{{VAULT_TOKEN}}");
  });

  it("leaves an undefined variable as its token rather than emitting an empty string", () => {
    const curl = buildCurl(
      request({ body: { mode: "Json", rawContent: '{"x":"{{NOPE}}"}' } as HttpRequestEntry["body"] }),
      "https://example.test/",
      scope,
    );
    expect(curl).toContain("{{NOPE}}");
  });

  it("skips disabled headers and headers with no name", () => {
    const curl = buildCurl(
      request({
        headers: [
          { key: "X-Off", value: "1", isEnabled: false },
          { key: "", value: "2", isEnabled: true },
        ] as HttpRequestEntry["headers"],
      }),
      "https://example.test/",
      scope,
    );
    expect(curl).not.toContain("X-Off");
    expect(curl).not.toContain(": 2");
  });

  it("escapes a single quote so the command survives the shell", () => {
    const curl = buildCurl(
      request({ body: { mode: "Text", rawContent: "it's" } as HttpRequestEntry["body"] }),
      "https://example.test/",
      scope,
    );
    expect(curl).toContain(`-d 'it'\\''s'`);
  });

  it("falls back to a content type derived from the body mode", () => {
    expect(
      buildCurl(request({ body: { mode: "Xml", rawContent: "<a/>" } as HttpRequestEntry["body"] }), "u", scope),
    ).toContain(`-H "Content-Type: application/xml"`);
  });

  it("prefers an explicit content type over the derived one", () => {
    expect(
      buildCurl(
        request({
          body: { mode: "Text", rawContent: "x", contentType: "text/csv" } as HttpRequestEntry["body"],
        }),
        "u",
        scope,
      ),
    ).toContain(`-H "Content-Type: text/csv"`);
  });

  it("emits no body flags for a bodiless request", () => {
    const curl = buildCurl(
      request({ method: "Get", body: { mode: "None" } as HttpRequestEntry["body"] }),
      "https://example.test/",
      scope,
    );
    expect(curl).not.toContain("-d ");
    expect(curl).not.toContain("Content-Type");
  });

  it("uses the executed URL verbatim, since the backend folds in query parameters", () => {
    const curl = buildCurl(request(), "https://example.test/?a=1&b=2", scope);
    expect(curl).toContain(`"https://example.test/?a=1&b=2"`);
  });
});
