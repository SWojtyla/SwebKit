import { describe, it, expect } from "vitest";
import { scrubHeaders, buildResponseExample } from "./response-example";
import type { ApiClientExecutionResponse } from "./types";

function response(overrides: Partial<ApiClientExecutionResponse> = {}): ApiClientExecutionResponse {
  return {
    resolvedUrl: "https://api.example.com/x",
    method: "Get",
    statusCode: 200,
    statusText: "OK",
    errorMessage: null,
    elapsedMs: 120,
    contentLength: 34,
    contentType: "application/json",
    responseBody: '{"ok":true}',
    responseBodyTruncated: false,
    headers: [],
    captureWarnings: [],
    graphQlErrors: null,
    ...overrides,
  };
}

describe("scrubHeaders", () => {
  it("drops credential-bearing headers outright", () => {
    const scrubbed = scrubHeaders([
      { name: "Authorization", value: "Bearer abc123" },
      { name: "Set-Cookie", value: "session=xyz" },
      { name: "Content-Type", value: "application/json" },
    ]);
    expect(scrubbed.map((h) => h.key)).toEqual(["Content-Type"]);
  });

  it("is case-insensitive about blocked header names", () => {
    const scrubbed = scrubHeaders([{ name: "AUTHORIZATION", value: "Bearer abc" }]);
    expect(scrubbed).toHaveLength(0);
  });

  it("redacts values of headers whose name looks secret but keeps the header", () => {
    const scrubbed = scrubHeaders([{ name: "X-Refresh-Token", value: "super-secret" }]);
    expect(scrubbed).toHaveLength(1);
    expect(scrubbed[0].key).toBe("X-Refresh-Token");
    expect(scrubbed[0].value).not.toContain("super-secret");
  });

  it("leaves ordinary headers untouched", () => {
    const scrubbed = scrubHeaders([{ name: "ETag", value: '"abc"' }]);
    expect(scrubbed[0]).toEqual({ key: "ETag", value: '"abc"', isEnabled: true });
  });
});

describe("buildResponseExample", () => {
  it("captures the response shape", () => {
    const example = buildResponseExample("id-1", "happy path", response(), "2026-07-31T00:00:00Z");
    expect(example.id).toBe("id-1");
    expect(example.name).toBe("happy path");
    expect(example.statusCode).toBe(200);
    expect(example.body).toBe('{"ok":true}');
    expect(example.capturedAt).toBe("2026-07-31T00:00:00Z");
    expect(example.environmentName).toBeNull();
  });

  it("never persists a credential from the response headers", () => {
    const example = buildResponseExample(
      "id-2",
      "with auth",
      response({ headers: [{ name: "Authorization", value: "Bearer leak-me" }] }),
      "2026-07-31T00:00:00Z",
    );
    expect(JSON.stringify(example)).not.toContain("leak-me");
  });
});
