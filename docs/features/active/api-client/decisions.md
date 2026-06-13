# Decisions — API Client

---

## DEC-1: Two separate files — `collections.json` and `environments.json`

**Decision:** Persist collections and environments in two separate files, each with their own
`CollectionRepository` and `EnvironmentRepository`.

**Rationale:** Collections can grow very large (hundreds of requests, deep folder nesting).
Environments are small and change frequently (active token refresh, switching active env, updating
a variable). A single file means every token update rewrites the full collection tree. The pattern
of separate-file repositories is already established: `profiles.json`, `ui-state.json`,
`user-settings.json`, `releases.json`, `scheduled-messages.json` are all separate.

**Tradeoff:** Two extra repository classes and two extra files in the bundle. Acceptable — the
pattern and the plumbing are already proven in the codebase.

---

## DEC-2: SwebKit-native JSON as the internal format; Postman and Bruno are projections only

**Decision:** `collections.json` uses `SwebKitCollectionV1` — a SwebKit-owned schema. Postman
Collection v2.1 and Bruno `.bru` are export/import targets only; they do not influence the
internal domain model.

**Rationale:** Postman's schema carries test scripts, pre-request hooks, authorization scopes,
and complex variable-scope rules that have no SwebKit equivalent. Coupling the internal model to
Postman's evolution would be a maintenance liability. Bruno's file-per-request filesystem layout
is suited for git-tracked folder repos but not for a single-file database approach.
A SwebKit-native schema evolves independently and stays minimal.

**Implication for users:** Export to Postman or Bruno is a lossy projection. Fields with no
target-format equivalent are silently dropped. Auth secrets are never included in exports —
only the `CredentialKey` reference is exported, and users must re-enter credentials after import.
This limitation must be surfaced in the import/export UI.

---

## DEC-3: Monaco Editor over CodeMirror 6

**Decision:** Monaco Editor for all code editing surfaces (request body, response viewer, GraphQL
query, variables).

**Rationale:** Monaco is already used in SwebKit for YAML highlighting (AKS). Extending the
existing `wwwroot/js/` interop wrapper is significantly lower cost than introducing CodeMirror 6
as a second editor library with its own module structure. Monaco has first-class GraphQL support
via `monaco-graphql`. Desktop app bundle size is not a meaningful constraint.

**Implication:** Monaco must be lazy-loaded via dynamic `import()` on first `/api-client` visit
to avoid impacting app startup time. The existing Monaco YAML integration already demonstrates
the lazy-load pattern.

---

## DEC-4: Auth secrets stored in `ICredentialStore` only — never in `collections.json`

**Decision:** `AuthConfig` in the domain model stores only a `CredentialKey` string (a reference
into the Windows Credential Store). Actual tokens, passwords, and client secrets are never
written to `collections.json`, never exported in SwebKit-native bundles, and never included in
Postman/Bruno exports.

**Rationale:** `collections.json` is a plain JSON file in `%APPDATA%`. Postman and Bruno
exports are files users share. Including credentials in either would be a security vulnerability
(OWASP A02 Cryptographic Failures). This follows the same pattern as existing Service Bus and
Redis credential references in `profiles.json`.

**Implication:** On import, users must re-enter auth credentials. The import UI shows a clear
warning: "Auth credentials are not included in exports. You will need to re-enter them after
import."

---

## DEC-5: API Client auto-collapses the global left nav on entry, restores on exit

**Decision:** When `OnLocationChanged` transitions into the `api-client` area, `MainLayout`
auto-collapses `IsNavExpanded` to `false`. When the user navigates away from `api-client` to
any other area, `IsNavExpanded` is restored to `true` — unless the user had already explicitly
collapsed it beforehand (tracked by a separate `_userCollapsedNav` flag).

A `[Show nav]` icon button is surfaced in the `ApiClientPage` toolbar so users can manually
expand the global nav at any time. The existing `ToggleNavAsync` flow handles that.

**Rationale:** The API Client has its own left-rail (collection tree), which competes with the
global left nav. The feature should feel immersive — like an IDE layout rather than one of many
pages. Auto-collapsing reclaims ~220 px of horizontal workspace without breaking the shell
primitives or requiring a new nav mode.

**Implication:** The `MainLayout` `OnLocationChanged` handler gains area-awareness. The previous
`IsNavExpanded` value before the auto-collapse is stored in `_navExpandedBeforeApiClient` so it
can be restored on exit.

---

## DEC-6: `WebSocketClientService` uses `System.Net.WebSockets` — no third-party library

**Decision:** Wrap `System.Net.WebSockets.ClientWebSocket` directly.

**Rationale:** The Phase 6 WebSocket use case is: connect, send text frames, receive text frames,
disconnect. No complex subprotocol negotiation, no binary protocol parsing, no reconnect policy
needed at this stage. A thin wrapper over the BCL type is sufficient. Adding a NuGet dependency
for a feature that is two methods thick is not justified.

**Revisit:** If binary protocol support (e.g., MessagePack over WS) or auto-reconnect with
backoff becomes a requirement, evaluate adding `websocket-sharp` or `System.Net.WebSockets`
extensions at that point.

---

## DEC-6: Key Vault integration is optional and gated behind a `KeyVaultUrl` prerequisite

**Decision:** `IKeyVaultSecretResolver` resolves to `NoopKeyVaultSecretResolver` when no
`KeyVaultUrl` is configured in `AppConfig`. Variable substitution degrades gracefully:
KV-type variables return `[KV_UNAVAILABLE:key]` and execution continues.

**Rationale:** Not all users operate Azure Key Vault. Making KV resolution a hard dependency
would break variable substitution for the majority. The same prerequisite-guard pattern already
exists for Service Bus, AKS, Redis, and Observability.

**KV setup:** A new `KeyVaultUrl` field is added to `AppConfig` and exposed in a new
"Key Vault" section in Settings. The existing `ConfigurationHealthService` adds KV to the
readiness check matrix.

---

## DEC-7: Postman import maps a subset; test scripts are silently dropped

**Decision:** `PostmanCollectionImporter` maps: collection name, folders, request name, method,
URL, headers, raw body, form-data body, and basic auth. Postman `event` arrays (test scripts,
pre-request scripts), Postman-specific auth flows with no SwebKit equivalent, and collection-level
variables beyond the top-level default environment are silently ignored.

**Rationale:** Implementing a Postman script execution engine is a separate multi-week effort.
Users migrating from Postman primarily need their request definitions, not their test assertions.
Importing silently and logging a summary of dropped fields is a better UX than failing with an
unsupported-format error.

**What is shown in the import UI:** After import completes, a summary panel lists: X requests
imported, Y test scripts skipped, Z auth configs requiring re-entry.

---

## DEC-8: Response body display capped at 500 KB; remainder available on demand

**Decision:** `HttpRequestExecutor` reads up to 500 KB of the response body into the
`HttpRequestResult`. A `ResponseBodyTruncated` flag signals whether more data is available.
`ResponseViewerPanel` shows a `[Load full response (X MB)]` affordance when truncated.

**Rationale:** Monaco handles large strings but Blazor DOM rendering is the bottleneck. A 500 KB
cap covers the vast majority of typical REST API responses. Loading a 10 MB JSON blob into the
DOM without a cap causes visible lag in bUnit tests and in practice.

---

## DEC-9: `FlatTreeNode` model for collection tree virtualisation

**Decision:** The collection tree uses a flattened `List<FlatTreeNode>` fed to `<Virtualize>`,
rather than recursive Blazor component rendering of `CollectionNode` trees.

**Rationale:** Recursive Blazor component trees at depth 3–4 with 500+ nodes create significant
DOM element counts. `<Virtualize>` requires a flat `ICollection<T>`; maintaining a live flattened
view (updated on expand/collapse) is cheaper than re-rendering the recursive tree. Expand/collapse
operations update the flattened list in place — no full re-projection of the tree structure is needed.

---

## DEC-10: `IWebSocketClientService` is `Transient` — one instance per panel

**Decision:** `IWebSocketClientService` is registered as `Transient` in DI. `ApiClientPage`
holds the instance and calls `DisposeAsync` on navigation away.

**Rationale:** Unlike HTTP execution (stateless, scoped), a WebSocket connection is stateful and
tied to one panel's lifetime. Sharing a WebSocket service across the page's DI scope would
prevent connecting to multiple endpoints in sequence without restart. Transient lifetime keeps
the contract simple: create → connect → disconnect → dispose.

---

## DEC-11: Single-request focus model — one request open at a time

**Decision:** `ApiClientPage` holds one active request at a time. Navigation between requests
is via the collection tree (click) or the `RequestQuickNavPanel` overlay (`Ctrl+P`). No multi-tab
request model.

**Rationale:** A tab model requires per-tab state management (in-flight cancellation, dirty tracking,
editor instance lifecycle) that adds significant complexity for Phase 1. The design is intentionally
clean so tabs can be added later without structural changes. The quick-nav panel provides fast
request switching without the overhead of a tab strip. Keeping state simple also eliminates
concurrent-request race conditions at launch.

**Implication:** If a request is dirty and auto-save is off, switching to another request prompts
the user to save or discard changes.

---

## DEC-12: Two-level variable scope — collection vars and environment vars

**Decision:** Variables are resolved in this order (first match wins):

1. Active environment variables (if an environment is selected)
2. Collection-level variables (always active, no environment needed)

Environment variables override collection variables on the same key.

**Rationale:** This mirrors Bruno's approach and covers the common pattern of:

- Collection vars: base URL, API version, feature flags that apply to all requests regardless of environment
- Environment vars: environment-specific overrides (dev/staging/prod tokens, URLs)

No global scope (app-wide), no per-request local scope, no nested scope chains. Keeping it to
two levels makes the resolution model predictable and auditable in the preview UI.

---

## DEC-13: Post-request capture uses JSONPath building blocks — no code execution

**Decision:** `HttpRequestEntry.CaptureRules` holds a list of `CaptureRule` records. Each rule
specifies: source type (JsonPath / Header / StatusCode), source expression, target variable name,
and target scope (Collection or Environment). `PostRequestCaptureExecutor` applies the rules
sequentially after a successful response. No scripting engine, no JavaScript, no C# expression
evaluation.

**Rationale:** A no-code approach keeps the surface safe (no arbitrary code execution risk),
makes rules portable across users and exports, and is sufficient for the primary use case of
capturing auth tokens from OAuth responses. JSONPath via `JsonPath.Net` covers all practical
response extraction patterns.

**Scope guards:** No conditional rules, no chained rules, no loop constructs. Rules are flat
and independent. A rule that fails logs a warning and does not block other rules.

---

## DEC-14: GraphQL subscriptions implemented over `graphql-ws` protocol

**Decision:** When the selected operation is a `subscription`, the executor switches to
`IGraphQlSubscriptionService`, which wraps `IWebSocketClientService` with `graphql-ws` protocol
framing (`graphql-transport-ws` subprotocol). No separate NuGet package is needed — the protocol
framing is thin JSON message construction on top of the existing WebSocket service.

**Rationale:** `graphql-ws` is the current standard (`graphql-transport-ws` subprotocol).
The legacy `subscriptions-transport-ws` protocol is excluded from scope. Reusing
`IWebSocketClientService` means subscriptions get the same lifecycle (cancellation, dispose on
navigate away) as Phase 6 WebSocket connections for free.

---

## DEC-15: Auto-save is an opt-in user setting, default off

**Decision:** `UserSettings.AutoSaveRequests: bool` (default: `false`). When enabled, any
change to the active request triggers a debounced 500 ms save to `CollectionRepository`. When
disabled, a dirty indicator (asterisk) appears in the panel header and the user saves explicitly.

**Rationale:** Auto-save is the right default for power users who iterate quickly, but some users
prefer explicit control when exploring or experimenting. Making it a user setting respects both
workflows without requiring a complex undo/history system at Phase 1.

---

## DEC-16: Auth inheritance by null-propagation up the tree

**Decision:** `HttpRequestEntry.Auth = null` means "inherit". `RequestFolder.DefaultAuth` and
`Collection.DefaultAuth` are the two ancestor levels. `IAuthInheritanceResolver.Resolve` walks:
request → direct parent folder → collection. The first non-null value is used.

**Rationale:** Mirrors the common pattern in Postman and Bruno where a collection-level auth
(e.g., Bearer token) applies to all requests unless overridden. The null sentinel is clean and
serialisation-transparent — a request with no auth simply omits the `Auth` property in JSON.

**Implication:** Setting a request's auth to `AuthType.None` explicitly opts out of inheritance
(the auth tab shows [None] and inheritance is suppressed). This is distinct from `Auth = null`
(which inherits). The UI makes the difference visible.

---

## PENDING-1: OAuth 2 redirect URI scheme — RESOLVED → DEC-17

See DEC-17 below.

---

## DEC-17: OAuth 2 redirect URI uses custom MAUI scheme `sweb://oauth`

**Decision:** The authorization code (PKCE) flow redirect URI is `sweb://oauth`. MAUI's
`WebAuthenticator.AuthenticateAsync` registers the scheme automatically via protocol
activation and handles the callback without manual URI registration in `AppxManifest`.

**Rationale:** Custom scheme via `WebAuthenticator` is the standard MAUI pattern (documented in
Microsoft docs). It avoids localhost port-binding races and works across Windows App SDK packaging
modes (packaged and unpackaged). The URI `sweb://oauth` is short and unique to this app.

**Implication:** Client apps registered with the identity provider must have `sweb://oauth` listed
as an allowed redirect URI. This is a one-time setup step the user performs in their IDP console.
A tooltip or help link in the `OAuth2AuthForm` surfaces this requirement.

---

## DEC-18: Git-linked collections use a SwebKit-owned folder format

**Decision:** Add a separate SwebKit-native folder format for git-linked collection roots. The
existing `.sweb.json` bundle remains the portable import/export format. Bruno and Postman remain
projection formats only.

**Rationale:** The current single-file SwebKit JSON is good for app persistence and backup, but
not ideal for Git review or merge workflows. A folder format with one request per file and optional
body/query sidecars gives readable diffs while staying under SwebKit's schema control.

**Implication:** Linked roots are not synchronized copies of local `collections.json`. For linked
roots, the files on disk are the source of truth and SwebKit edits those files directly.

---

## DEC-19: Linked root files store secret references only

**Decision:** Git-linked environment and request files may store secret names/references, but never
secret values. Values continue to resolve from `ICredentialStore`, Key Vault, or future local
machine-only providers at send time.

**Rationale:** Linked roots are expected to live in team Git repositories. Persisting bearer tokens,
client secrets, passwords, or API keys would create a direct credential leak. This preserves the
existing API client security model from DEC-4.

**Implication:** The UI must make missing local secrets easy to configure. A linked root can be
valid even when some secrets are unresolved on the current machine.

---

## DEC-20: Git integration is staged and scoped to linked API roots

**Decision:** Start with Git awareness (branch, clean/dirty, changed linked-root files), then add
safe actions: create/switch branch, commit selected SwebKit API files, and push the current branch.
Do not implement pull, rebase, stash, arbitrary commands, or PR creation in the first Git action
slice.

**Rationale:** Users should not need a separate tool for common collection edits, but Git actions
can affect unrelated project files if they are too broad. Scoping actions to the configured API root
keeps the feature useful without becoming a full Git client.

**Implication:** Use fixed command builders and explicit confirmation for branch switch, commit,
and push. Status and commit file lists are constrained to the linked API root path.

---

## DEC-21: Dynamic variables use safe building blocks, not scripts

**Decision:** Dynamic variables will be defined with explicit generator definitions such as integer
range, GUID, date/time, list pick, fake person fields, and templates. No arbitrary JavaScript, C#,
or shell execution is allowed.

**Rationale:** The API client already intentionally avoids script execution for post-request capture.
Generated variables should follow the same safety model: useful test data, predictable constraints,
and no code execution attack surface.

**Implication:** Use SwebKit-owned primitive generators for constraints and add `Bogus` only for
realistic fake data categories such as names and email addresses. Generated sample values are never
persisted; only generator definitions are stored.
