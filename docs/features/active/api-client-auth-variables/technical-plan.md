# API Client Auth: Variable Substitution & Secret Visibility — Technical Plan

## 1. Substitution reaches the auth config

**New — `src/SwebKit.Core/Services/AuthConfigSubstitution.cs`.** Two pure entry points,
shared by both `IAuthHeaderBuilder` implementations so they cannot drift:

- `Substitute(auth, substitution, scope)` — returns a copy of the `AuthConfig` with tokens
  resolved in every **non-secret** field (`ApiKeyParamName`, `BasicUsername`,
  `OAuth2ClientId`, `OAuth2TokenUrl`, `OAuth2AuthUrl`, `OAuth2Scopes`). Returns the original
  instance when the scope is null or empty, so nothing is allocated on the no-variable path.
- `SubstituteSecret(secret, substitution, scope)` — resolves tokens in a secret **after** it
  has been read.

The split exists because the secret is only known once the credential store has been
consulted. `SidecarAuthHeaderBuilder.ResolveSecret` has three sources in documented
precedence (transient `CredentialSecret` → OS credential store → the legacy literal
`CredentialKey`); substituting the resolved value rather than each source means all three
behave the same and the precedence logic is untouched. `Substitute` deliberately leaves
`CredentialSecret` alone so it is never resolved twice.

**`IAuthHeaderBuilder.ApplyAsync`** gains an `IReadOnlyDictionary<string, string?>? scope`
parameter before the cancellation token, defaulted to `null` — a call without a scope leaves
every auth field verbatim, which is what the GraphQL schema/subscription services and the
existing no-op test doubles want.

**`HttpRequestExecutor`** passes the `scope` it already built for the URL, headers and body.
One scope, one build, so auth cannot resolve a variable differently from the body next to it.

**`SidecarAuthHeaderBuilder`** takes `IVariableSubstitutionService` (already registered in
`src-sidecar/Program.cs`), substitutes the config once at the top of `ApplyAsync`, and
threads the scope into each handler for the secret. **`SwebKit.App.Services.AuthHeaderBuilder`**
does the same; its OAuth2 path is the documented gap (see `index.md` non-goals).

## 2. The auth panel shows its secrets

**`web/src/components/api-client/request-editor/AuthPanel.tsx`.** A local `SecretField`
replaces the four bare `type="password"` inputs (bearer token, Basic password, API key
value, OAuth2 client secret). Hidden, it is the same password input as before. Revealed, it
is `VariableInput`, so a `{{token}}` is coloured by resolution state and hovering lists what
each one resolves to — a plain text input would have shown the token with nothing to say
whether it was real.

One `revealed` flag covers the whole panel: only one secret field is visible at a time. It
is component state, not persisted and not lifted, so it resets when the request changes.

**`VariableInput`** gains an optional `onBlur`. The secret is persisted to the secret store
on blur as well as on a debounce; without this the revealed field would have skipped the
blur save that the password input does.

**`RequestEditor`** passes `variableScope` to the panel. The `substitutedText` that feeds the
variable preview and the unresolved-variable banner moved below the `authSecretInput` state
declaration (it now reads it) and gained the auth fields.

**New — `web/src/lib/auth-variables.ts`.** `authSubstitutedText(auth, secret)` returns the
fields the *selected* auth type actually sends, mirroring `AuthConfigSubstitution`. Scoped by
type on purpose: a token URL left behind by an earlier OAuth2 setup must not raise a warning
on a request now sending a bearer token. It lives in `lib/` rather than inline because vitest
here runs node-only with no DOM, so only pure functions are testable.

Only variable *names* are collected from the secret, never values — the banner names
`AUTH_PI2_KEY`, it does not print what it resolves to.

## 3. cURL panel

`buildAuthParts` in `web/src/lib/curl.ts` now takes the scope and substitutes the API key
param name and the Basic username, which it previously printed as raw `{{tokens}}` — the
same half-resolved command the module's header comment already calls out as the bug it
exists to prevent. The secret itself is unchanged: still `••••••••` in the unsent preview,
still masked in the sent-headers rendering until "Reveal secrets".
