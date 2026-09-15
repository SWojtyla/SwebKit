import { describe, it, expect } from "vitest";
import { authSubstitutedText } from "./auth-variables";
import { unresolvedVariableNames } from "./variableHighlight";
import type { AuthConfig } from "./types";

function auth(overrides: Partial<AuthConfig>): AuthConfig {
  return {
    type: "None",
    credentialKey: null,
    credentialSecret: null,
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

describe("authSubstitutedText", () => {
  it("includes the bearer token so an undefined variable in it is warned about", () => {
    const text = authSubstitutedText(auth({ type: "BearerToken" }), "{{AUTH_PI2_KEY}}");

    expect(unresolvedVariableNames(text.join("\n"), { OTHER: "x" })).toEqual(["AUTH_PI2_KEY"]);
  });

  it("includes the basic username alongside the password", () => {
    const text = authSubstitutedText(auth({ type: "Basic", basicUsername: "{{USER}}" }), "{{PASS}}");

    expect(text).toEqual(["{{USER}}", "{{PASS}}"]);
  });

  it("includes the api key name alongside its value", () => {
    const text = authSubstitutedText(auth({ type: "ApiKey", apiKeyParamName: "{{KEY_HEADER}}" }), "{{KEY_VALUE}}");

    expect(text).toEqual(["{{KEY_HEADER}}", "{{KEY_VALUE}}"]);
  });

  it("includes every OAuth2 field the client-credentials flow sends", () => {
    const text = authSubstitutedText(
      auth({
        type: "OAuth2",
        oAuth2GrantType: "ClientCredentials",
        oAuth2ClientId: "{{CLIENT_ID}}",
        oAuth2TokenUrl: "{{BASE}}/token",
        oAuth2AuthUrl: "{{BASE}}/authorize",
        oAuth2Scopes: "{{SCOPES}}",
      }),
      "{{CLIENT_SECRET}}",
    );

    // The authorization URL is not part of the client-credentials flow, so a variable only
    // used there must not be reported as a problem with this request.
    expect(text.join("\n")).not.toContain("authorize");
    expect(text).toContain("{{CLIENT_ID}}");
    expect(text).toContain("{{BASE}}/token");
    expect(text).toContain("{{SCOPES}}");
    expect(text).toContain("{{CLIENT_SECRET}}");
  });

  it("includes the authorization URL only for the authorization-code grant", () => {
    const text = authSubstitutedText(
      auth({
        type: "OAuth2",
        oAuth2GrantType: "AuthorizationCode",
        oAuth2AuthUrl: "{{BASE}}/authorize",
      }),
      "",
    );

    expect(text).toContain("{{BASE}}/authorize");
  });

  it("reports nothing for None or Inherited, whatever is left in the other fields", () => {
    const stale = { basicUsername: "{{USER}}", oAuth2TokenUrl: "{{BASE}}/token" };

    expect(authSubstitutedText(auth({ type: "None", ...stale }), "{{SECRET}}")).toEqual([]);
    expect(authSubstitutedText(auth({ type: "Inherited", ...stale }), "{{SECRET}}")).toEqual([]);
  });

  it("reports nothing when there is no auth config at all", () => {
    expect(authSubstitutedText(null, "{{SECRET}}")).toEqual([]);
    expect(authSubstitutedText(undefined, "{{SECRET}}")).toEqual([]);
  });
});
