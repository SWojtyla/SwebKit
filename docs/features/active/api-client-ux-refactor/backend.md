# Backend — API Client UX Refactor

## Scope

Most of this feature is frontend (see `frontend.md`). Backend work is limited to:

- Phase 3 (tabs): a small settings addition; no new services required.
- Phase 4 (cookie jar): a cookie store service and an execution-path integration.

The icon pass (Phase 1) and the page refactor (Phase 2) require **no** backend changes.

---

## Phase 3 — Settings for request tabs

- Add `ApiClientRequestTabs` (bool, default `false`) to the user settings model backing
  `UserSettingsRepository`.
- Mirror the existing `VerifyApiClientSsl` setting: same model, same persistence path, same
  read pattern (`UserSettings.Settings.ApiClientRequestTabs`).
- No migration concerns — a missing value must default to `false` (single-request model).

---

## Phase 4 — Cookie jar (deferrable)

### Concept

A cookie jar stores `Set-Cookie` values returned by a server and replays matching cookies on later
requests to the same domain/path, the way a browser does. Without it, session/CSRF-based APIs
require the user to hand-copy the session cookie into a header on every authenticated call.

### Design

- New service (indicative): `IApiCookieJar` / `ApiCookieJar` in `src/SwebKit.Core`.
  - Prefer wrapping `System.Net.CookieContainer` for correct domain/path/expiry/`Secure`/`HttpOnly`
    semantics rather than re-implementing cookie matching.
  - Keyed by domain; supports read (for a request URL), update (from response `Set-Cookie`),
    per-domain clear, and clear-all.
- Integration in the request execution path (`HttpRequestExecutor` / `IHttpRequestExecutor`):
  - When cookie capture is enabled, attach the jar's cookies to the outgoing request and record
    `Set-Cookie` from the response.
  - The named `ApiClient` `HttpClient` currently sends without an ambient cookie container; the jar
    must be applied explicitly per send so it stays opt-in and testable (do NOT enable
    `HttpClientHandler.UseCookies` globally, which would leak state across all API Client traffic).
- Scope/lifetime:
  - Enable is opt-in (per-request flag or global toggle), default off.
  - Storage is machine-local. MVP may keep the jar in-memory (session-scoped); optional persistence
    to app-local state is a follow-up, and if added must obey the secret rules below.

### Security & safety (hard constraints)

- Cookies are secret-adjacent. They must be **scrubbed from every projection**, matching the
  archived rule that secret safety is enforced in exports, cURL, examples, diffs, and linked files:
  - Never write cookie values into `collections.json`, linked `.swebkit-api/` files, exports,
    Copy-as-cURL output, saved response examples, or Git diffs.
  - If session persistence is added, store outside repo-tracked files (app-local only), never in
    linked roots.
- Respect the existing SSL verification setting; do not send cookies over channels the user marked
  insecure without the same warning surface already used for `VerifyApiClientSsl`.
- Cookie jar must degrade gracefully: a malformed `Set-Cookie` must not fail request execution
  (mirror the Key Vault graceful-degradation rule).

### Deferral note

Phase 4 is explicitly deferrable. Phases 1–3 deliver standalone value; the cookie jar can ship in a
later pass without blocking the rest of the feature.

## Affected Files (backend)

- `src/SwebKit.Core` request execution (`HttpRequestExecutor` / `IHttpRequestExecutor`).
- `src/SwebKit.Core/Domain/ApiClientModels.cs` (optional per-request cookie-capture flag).
- User settings model behind `UserSettingsRepository`.
- New: `IApiCookieJar` / `ApiCookieJar` under `src/SwebKit.Core/Services/`.
- DI registration in `MauiProgram.cs`.
