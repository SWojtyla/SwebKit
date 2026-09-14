# API Client Auth: Variable Substitution & Secret Visibility — Test Plan

## Automated

### `tests/SwebKit.Core.Tests/AuthConfigSubstitutionTests.cs` (new)

| Scenario | Expectation |
| --- | --- |
| Every non-secret field holds a `{{token}}` | All resolved, including a token embedded in a URL (`{{BASE}}/token`) |
| Non-text fields (type, key location, grant type, credential key) | Copied unchanged |
| `CredentialSecret` holds a `{{token}}` | Left alone — resolved later by `SubstituteSecret`, never twice |
| Null scope / empty scope | The same instance is returned, not a copy |
| `SubstituteSecret` with a known variable | Resolved |
| `SubstituteSecret` with an unknown variable | Left literal, matching `VariableSubstitutionService` everywhere else |
| `SubstituteSecret` with null/empty input, or no scope | Returned unchanged |

### `tests/SwebKit.Sidecar.Tests/SidecarAuthHeaderBuilderTests.cs` (extended)

The pre-existing precedence and OAuth2 cases are unchanged; `Build()` now supplies a real
`VariableSubstitutionService` so the substitution path is exercised end to end.

| Scenario | Expectation |
| --- | --- |
| Bearer token is `{{AUTH_PI2_KEY}}` (transient secret) | `Authorization: Bearer <resolved>` — the reported defect |
| Bearer token is `{{AUTH_PI2_KEY}}` **in the credential store** | Same, so all three secret sources behave alike |
| Bearer token names an undefined variable | Header keeps the literal token rather than going empty |
| Bearer token is a variable but no scope is passed | Verbatim — a caller without a scope opts out |
| API key: header name and value both variables | Both resolved; the header lands under the resolved name |
| API key as a query param, value a variable | Resolved before URL-encoding (`secret 123` → `secret%20123`) |
| Basic: username and password both variables | Both resolved inside the base64 credentials |
| OAuth2 client credentials: token URL, client id, secret and scopes all variables | Token request goes to the resolved URL with the resolved form fields |

### `web/src/lib/auth-variables.test.ts` (new)

Per auth type, that `authSubstitutedText` reports exactly the fields that type sends: the
bearer token; Basic username + password; API key name + value; the OAuth2 fields, with the
authorization URL included only for the authorization-code grant. Nothing at all for `None`,
`Inherited` or a null config, whatever stale values the other fields still hold.

### `web/src/lib/curl.test.ts` (extended)

A Basic username, an API key header name and an API key query-param name written as
variables each render resolved rather than as `{{tokens}}`. The existing masking assertions
are untouched.

### `web/e2e/api-client.spec.ts` (extended)

"an auth secret can be revealed and hidden again": a bearer token is typed, the field is
`type="password"` and holds the value; the reveal button switches it to the text-based
variable input with the same value; clicking again masks it.

## Manual — owner: Sebastien

Needs the real `DEV (via APIM)` environment and Portima credentials, so it cannot be
automated here.

1. Open the `Get Token 15000 - 01` request. On the Auth tab, the bearer token field is
   masked; click the eye — `{{AUTH_PI2_KEY}}` appears, coloured as resolved (green) with the
   environment selected, red when no environment is active.
2. Send with the environment selected. The request succeeds instead of returning
   `400 Bad Request`.
3. Open the cURL panel and click "Reveal secrets": the `Authorization` header shows the real
   token value, not `Bearer {{AUTH_PI2_KEY}}`.
4. Deselect the environment. The banner above the tabs names `AUTH_PI2_KEY` among the
   undefined variables before anything is sent.
5. Reload the app, reopen the request, reveal the token: the value is still there, confirming
   the blur save from the revealed field reached the secret store.
