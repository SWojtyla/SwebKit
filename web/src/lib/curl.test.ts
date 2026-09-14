import { describe, it, expect } from "vitest";
import { buildCurl } from "./curl";
import type { AuthConfig, HttpRequestEntry } from "./types";

function auth(overrides: Partial<AuthConfig>): AuthConfig {
  return {
    type: "None",
    credentialKey: "sw-secret:test",
    credentialSecret: "totally-secret-value",
    apiKeyParamName: null,
    apiKeyLocation: "Header",
    basicUsername: null,
    oAuth2ClientId: null,
    oAuth2GrantType: "ClientCredentials",
    oAuth2TokenUrl: null,
    oAuth2AuthUrl: null,
    oAuth2Scopes: null,
    ...overrides,
  };
}

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

describe("buildCurl auth", () => {
  const MASK = "••••••••";
  const SECRET = "totally-secret-value";

  it("adds nothing for None", () => {
    const curl = buildCurl(request({ auth: auth({ type: "None" }) }), "https://example.test/", scope);
    expect(curl).not.toContain("Authorization");
    expect(curl).not.toContain("-u ");
  });

  it("adds nothing when auth is null", () => {
    const curl = buildCurl(request({ auth: null }), "https://example.test/", scope);
    expect(curl).not.toContain("Authorization");
  });

  it("masks a Bearer token", () => {
    const curl = buildCurl(request({ auth: auth({ type: "BearerToken" }) }), "https://example.test/", scope);
    expect(curl).toContain(`-H "Authorization: Bearer ${MASK}"`);
    expect(curl).not.toContain(SECRET);
  });

  it("masks an OAuth2 client secret as a Bearer header, since that's what actually reaches the target", () => {
    const curl = buildCurl(request({ auth: auth({ type: "OAuth2" }) }), "https://example.test/", scope);
    expect(curl).toContain(`-H "Authorization: Bearer ${MASK}"`);
    expect(curl).not.toContain(SECRET);
  });

  it("masks Basic auth via curl's own -u flag", () => {
    const curl = buildCurl(
      request({ auth: auth({ type: "Basic", basicUsername: "alice" }) }),
      "https://example.test/",
      scope,
    );
    expect(curl).toContain(`-u "alice:${MASK}"`);
    expect(curl).not.toContain(SECRET);
  });

  it("masks an API key sent as a header", () => {
    const curl = buildCurl(
      request({ auth: auth({ type: "ApiKey", apiKeyParamName: "X-Api-Key", apiKeyLocation: "Header" }) }),
      "https://example.test/",
      scope,
    );
    expect(curl).toContain(`-H "X-Api-Key: ${MASK}"`);
    expect(curl).not.toContain(SECRET);
  });

  it("masks an API key sent as a query param, appending to a bare URL", () => {
    const curl = buildCurl(
      request({ auth: auth({ type: "ApiKey", apiKeyParamName: "api_key", apiKeyLocation: "QueryParam" }) }),
      "https://example.test/",
      scope,
    );
    expect(curl).toContain(`"https://example.test/?api_key=${MASK}"`);
    expect(curl).not.toContain(SECRET);
  });

  it("masks a query-param API key by appending with & when the URL already has a query string", () => {
    const curl = buildCurl(
      request({ auth: auth({ type: "ApiKey", apiKeyParamName: "api_key", apiKeyLocation: "QueryParam" }) }),
      "https://example.test/?a=1",
      scope,
    );
    expect(curl).toContain(`"https://example.test/?a=1&api_key=${MASK}"`);
  });

  it("substitutes a Basic username written as a variable", () => {
    const curl = buildCurl(
      request({ auth: auth({ type: "Basic", basicUsername: "{{AUTH_SP}}" }) }),
      "https://example.test/",
      scope,
    );
    expect(curl).toContain(`-u "brio:${MASK}"`);
  });

  it("substitutes an API key header name written as a variable", () => {
    const curl = buildCurl(
      request({ auth: auth({ type: "ApiKey", apiKeyParamName: "{{AUTH_SP}}", apiKeyLocation: "Header" }) }),
      "https://example.test/",
      scope,
    );
    expect(curl).toContain(`-H "brio: ${MASK}"`);
  });

  it("substitutes an API key query-param name written as a variable", () => {
    const curl = buildCurl(
      request({ auth: auth({ type: "ApiKey", apiKeyParamName: "{{AUTH_SP}}", apiKeyLocation: "QueryParam" }) }),
      "https://example.test/",
      scope,
    );
    expect(curl).toContain(`"https://example.test/?brio=${MASK}"`);
  });

  it("adds nothing for an API key with no param name configured yet", () => {
    const curl = buildCurl(
      request({ auth: auth({ type: "ApiKey", apiKeyParamName: null }) }),
      "https://example.test/",
      scope,
    );
    expect(curl).not.toContain(MASK);
  });

  it("notes an inherited auth as a leading comment rather than silently showing nothing", () => {
    const curl = buildCurl(request({ auth: auth({ type: "Inherited" }) }), "https://example.test/", scope);
    const lines = curl.split("\n");
    expect(lines[0].startsWith("# Auth is inherited")).toBe(true);
    // The comment must be its own line, not inside the `\`-continued command — a `#` there
    // would swallow the trailing backslash as comment text and orphan every line after it.
    expect(lines[0].endsWith("\\")).toBe(false);
    expect(lines[1].startsWith("curl")).toBe(true);
  });
});
