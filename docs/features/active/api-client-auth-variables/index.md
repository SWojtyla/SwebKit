---
status: Review
---

# API Client Auth: Variable Substitution & Secret Visibility

## Scope

Two defects in the request Auth tab, found in daily use of the Tauri build against the
Portima authentication API.

1. **Auth was the one part of a request the variable scope never reached.**
   `HttpRequestExecutor` substitutes `{{tokens}}` in the URL, the query string, the headers
   and the body, then hands the `AuthConfig` to `IAuthHeaderBuilder` untouched. A bearer
   token entered as `{{AUTH_PI2_KEY}}` was therefore sent as those sixteen characters:
   `Authorization: Bearer {{AUTH_PI2_KEY}}` went on the wire, the API answered
   `400 Bad Request`, and the cURL panel — which echoes the headers actually sent — showed
   the raw token as proof while every other line of the same command was fully resolved.
   The same held for the API key value and name, the Basic username and password, and every
   OAuth2 field.
2. **An auth secret was write-only.** Every secret field was a bare `type="password"` with
   no reveal. Once a token was set up it could not be read back, checked against the
   environment, or corrected — only retyped blind. This is also why (1) was invisible: the
   `{{AUTH_PI2_KEY}}` the user had typed was rendered as eight dots, so nothing in the panel
   suggested a variable was involved at all.

## Outcomes

- Any auth field may be a `{{variable}}` and is resolved at send time against the same
  merged scope as the rest of the request — collection variables, the global environment
  layer and the collection-scoped one, including Key Vault and credential-store values.
- An undefined variable in an auth field is left literal (as everywhere else) *and* named in
  the existing "not defined in this scope" banner, so the failure is visible before sending
  instead of arriving as a `400` from the server.
- A secret can be revealed and hidden again. Revealed, it is the variable-aware
  `VariableInput`, so `{{AUTH_PI2_KEY}}` is coloured by whether it will actually resolve —
  the same three-state treatment the URL bar and the body editor already give.
- The cURL panel substitutes the non-secret auth fields (API key header name, Basic
  username) it previously printed as raw tokens.

## Non-goals

- **The secret value is still never expanded into the cURL panel.** It stays `••••••••`
  until the panel's own "Reveal secrets" toggle, which shows what was actually sent, is
  used. That rule predates this feature and is unchanged.
- **`OAuth2TokenManager` in the legacy Blazor app.** `AuthHeaderBuilder` there substitutes
  the bearer/API-key/Basic secrets and the non-secret fields, but the OAuth2 client secret
  is read from the credential store inside `IOAuth2TokenManager`, which takes a key rather
  than a value and so never sees the scope. The sidecar — the path the shipping Tauri app
  uses — has no such gap. Threading the scope through the token manager is a follow-up for
  whenever that app is still maintained.
- **Reveal state is not remembered.** It is local to the panel and resets when the request
  changes, deliberately.

## Dependencies

- `web/src/components/api-client/VariableInput.tsx` — reused for the revealed field; gains
  an optional `onBlur` so the secret still persists when focus leaves.
- `web/src/lib/variableHighlight.ts` — `unresolvedVariableNames`, unchanged.
- `SwebKit.Core.Services.VariableSubstitutionService` — unchanged; the new
  `AuthConfigSubstitution` calls it.
- Builds directly on `api-client-variable-scoping/`, which closed the same blind spot for
  the request body and named the cURL panel's half-resolved output as the bug it was.
