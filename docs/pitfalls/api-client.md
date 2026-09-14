# API Client Pitfalls

## Every field that goes on the wire must be substituted — and shown

`{{variable}}` substitution is applied field by field in `HttpRequestExecutor`, not to the
request as a whole. Any field added to `HttpRequestEntry` or `AuthConfig` that reaches the
network is therefore **unsubstituted until someone remembers it**, and the symptom is always
the same: the server rejects the request and nothing in the app points at the cause, because
`VariableSubstitutionService.Substitute` leaves an unresolved token as its literal text.

This has now cost two sessions:

- the request **body** — substituted at send time but with no highlighting, so an undefined
  variable was invisible (`api-client-variable-scoping`);
- the **auth config** — not substituted at all, so `Bearer {{AUTH_PI2_KEY}}` went on the wire
  (`api-client-auth-variables`).

When touching either side, check all four:

1. Does `HttpRequestExecutor` (or the builder it delegates to) substitute the field?
2. Does `substitutedText` in `RequestEditor.tsx` include it, so the unresolved-variable
   banner names it before the user sends?
3. Does `buildCurl` render it substituted? A panel whose whole purpose is reproducing the
   request must not mix resolved and unresolved text.
4. Is the field's UI variable-aware (`VariableInput`, or the CodeMirror extension for the
   body)? A `type="password"` input shows dots whether or not a variable is inside it.

Known remaining gaps, deliberately: `GraphQlPanel` and `WebSocketPanel` message bodies are
plain `<textarea>`s whose content *is* substituted at send time but is not highlighted; and
`IOAuth2TokenManager` in the legacy Blazor app reads its client secret from the credential
store by key, so it never sees the scope.

## A write-only secret field hides more than the secret

A bare `type="password"` with no reveal makes a value impossible to check, compare or
correct — and it hides *what kind* of value it is. An auth token entered as
`{{AUTH_PI2_KEY}}` rendered as eight dots, so the variable bug above had no visible symptom
in the panel at all. Secret inputs get a reveal toggle, and revealed they use
`VariableInput` so token resolution state is visible too.
