import type { AuthConfig } from "./types";

/**
 * The auth fields that are substituted at send time, for the auth type actually selected.
 *
 * Used to feed the variable preview and the "not defined in this scope" banner, which
 * previously covered the URL, headers and body but not auth — so a bearer token entered as
 * `{{AUTH_API_KEY}}` produced a `400` from the server with nothing in the app pointing at the
 * cause. Only the fields the selected type uses are returned: a token URL left behind by an
 * earlier OAuth2 setup must not raise a warning on a request now sending a bearer token.
 *
 * Mirrors `AuthConfigSubstitution` on the backend. If the two lists diverge, the app warns
 * about variables it does not substitute, or substitutes ones it never warned about.
 *
 * @param secret The secret as typed in the auth panel, which `AuthConfig.credentialSecret`
 *   does not always hold — it is cleared before persisting so `collections.json` never stores it.
 */
export function authSubstitutedText(auth: AuthConfig | null | undefined, secret: string): string[] {
  if (!auth || auth.type === "None" || auth.type === "Inherited") return [];

  switch (auth.type) {
    case "BearerToken":
      return [secret];
    case "Basic":
      return [auth.basicUsername ?? "", secret];
    case "ApiKey":
      return [auth.apiKeyParamName ?? "", secret];
    case "OAuth2":
      return [
        auth.oAuth2ClientId ?? "",
        auth.oAuth2TokenUrl ?? "",
        auth.oAuth2GrantType === "AuthorizationCode" ? auth.oAuth2AuthUrl ?? "" : "",
        auth.oAuth2Scopes ?? "",
        secret,
      ];
    default:
      return [];
  }
}
