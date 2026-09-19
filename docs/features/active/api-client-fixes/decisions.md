# Decisions — API Client Fixes

## D1 — Env-var secrets resolve through the sidecar credential store, not the Tauri vault

**Chosen:** new sidecar endpoints (`POST/DELETE /api/api-client/credentials`,
`POST /api/api-client/preview-credential`) write to `ICredentialStore` — the
same store `VariableSubstitutionService` resolves `WindowsCredentialStore`
variables from.

- The alternative — frontend resolves secret vars via `getSecret` (Tauri
  keyring vault) and injects values into the execute request — would have
  required a resolved-secrets channel in `ExecuteRequestRequest`, and would
  diverge from Key Vault variables, which resolve sidecar-side. Keeping
  resolution in one place (the sidecar) is simpler and matches the documented
  design ("OS credential store").
- Two stores still exist (Tauri vault for auth `sw-secret:*`, KeySharp for
  env vars + connection strings), but each flow is now self-consistent:
  what you save through the variable editor is what the executor resolves.
- `preview-credential` mirrors `preview-keyvault-secret` — masked value, no
  raw secret over HTTP.

## D2 — The credential field keeps its key, gains a value

**Chosen:** `credentialKey` stays editable (users *can* reference a
pre-existing OS credential, e.g. a shared team key); a new value field saves
into the store under that key, auto-generating `sw-secret:<uuid>` when blank —
the same convention auth secrets use.

- A pure "type a value, we handle the key" design would have silently broken
  the legitimate reference-an-existing-key use.
- Debounced save (like `RequestEditor`'s auth secret) so there's no separate
  save click to forget.

## D3 — Date bounds are explicit `after`/`before` fields, not category forks

**Chosen:** optional `fakerDateAfter`/`fakerDateBefore` (ISO strings) applied
to every `date.*` category, plus a new `date.between` category that requires
both.

- Alternative considered: per-category bound params (`pastYears`, `futureYears`).
  Rejected — "before/after a date" is the user's mental model and maps 1:1 to
  `Date.Between`; relative-offset params would need UI for units the user
  didn't ask for.
- Defaults preserve today's behavior when unset: `past` → within the last
  ~year, `future` → within the next ~year, `recent` → within the last day.

## D4 — Collection-level secrets stay out of scope

`CollectionVariable` has no `secretSource`; adding one is a model + importer +
exporter change beyond a bugfix pass. Documented gap: secrets are
environment-level only — the workaround is a collection-scoped environment.

## D5 — Unresolved variables are detected on the wire image, not modeled from inputs

**Chosen:** after substitution and auth are applied, `HttpRequestExecutor`
scans the resolved URL, the sent headers, and the body for leftover
`{{tokens}}` and reports each as a send warning.

- Scanning outputs instead of inputs catches every channel uniformly (URL,
  headers, body, form fields, GraphQL payload, auth-applied headers) without
  modeling which fields get substituted — anything still bracketed is, by
  definition, going out literally.
- The warnings ride the existing `CaptureWarnings` result channel (doc-comment
  broadened to "send warnings") rather than a new DTO field; the UI label was
  generalized from "Capture warnings" to "Warnings" to match.
- Binary file bodies are skipped: potentially large, and file bytes can't
  carry tokens. (First implementation guarded on `ByteArrayContent`, which
  silently excluded `StringContent` — it inherits from it. Guard is now on the
  request's body *mode*.)
