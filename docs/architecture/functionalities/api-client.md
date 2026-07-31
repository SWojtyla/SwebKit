# API Client

## What Is Supported

- **Local collections and requests** — named collections with folder/request hierarchy, persisted through `CollectionRepository` to `collections.json` using atomic write and `.bak` recovery.
- **Git repositories for API files** — user-picked repository folders, persisted per machine with an optional API subpath. The repository is treated as a folder that happens to hold API files; SwebKit does not load collections *from* it (see Current Deferrals).
- **Safe Git actions** — branch awareness and switching, branch creation, a changed-file list with staged/unstaged/conflicted sections, per-file stage/unstage/revert, in-app original/current diff preview, commit with a preview of the exact staged files, push, pull, and provider-inferred remote compare links for GitHub/Azure DevOps. Staging and committing are scoped to the configured API subpath, and a commit is refused when files outside it are staged.
- **Workflow trust UI** — commit preview listing the exact staged files, a confirmation naming every file before a revert, and save-conflict actions on the collections file: Reload, Overwrite, Save as copy.
- **Variable substitution** — `{{token}}` syntax in URL, headers, body, GraphQL query, and GraphQL variables. Environment variables override collection variables on the same key.
- **Generated variables** — safe building blocks for integer, decimal, boolean, GUID, date/time, list, Bogus-backed fake data, and templates. Definitions are persisted; generated sample values are not.
- **Secrets** — Windows Credential Store and Azure Key Vault references. Secret values are resolved at send time and are not persisted to app-local JSON, linked files, or exports.
- **Environments** — named environment sets with plain, Windows Credential Store, Key Vault, and generated variables. Active environment is persisted in API client UI state.
- **REST execution** — all common HTTP methods, headers/query/body editing, auth injection, variable substitution, response status/timing/size/header/body display, and per-tab response history.
- **Syntax highlighting** — JSON and XML highlighting in both the request body editor and the response viewer, driven by `--cm-*` CSS custom properties so a single CodeMirror `HighlightStyle` serves the `light`, `dark` and `fancy` themes without reconfiguring mounted editors. Response bodies get line numbers, code folding, in-body search (`Ctrl+F`), a wrap toggle and a download action.
- **Response body size policy** — bodies under 2 kB render as a span-highlighted `<pre>`; larger bodies use a virtualized read-only CodeMirror view; above 512 kB language parsing is dropped (with a visible notice and an opt-in override) so the 4 MB sidecar cap stays scrollable.
- **Method and status colour vocabulary** — one shared `METHOD_META` table supplies each method's short label and tone, and response statuses map onto the same tones. All colour comes from Aurora design tokens, so badges follow the active theme.
- **Panel layout** — a three-pane split where the collections tree stays roughly fixed and the request/response panes share the leftover width as fractions. Widths persist per user, and the dividers are keyboard-operable (`role="separator"`, arrow keys, `Home`/`End`, double-click to reset).
- **Authentication** — Bearer Token, API Key, Basic, OAuth 2 client credentials, and OAuth 2 authorization code with PKCE through MAUI `WebAuthenticator` using `sweb://oauth`.
- **Post-request capture** — JSONPath, response header, and status-code capture rules that write into collection or environment variables without scripting.
- **GraphQL** — query and variables editors, operation parsing, schema introspection cache, GraphQL error rendering, and `graphql-ws` subscriptions.
- **WebSocket** — URL/headers/subprotocol, connection state, bounded virtualized message log, text/binary composer, and saved message templates.
- **Export/import** — SwebKit-native JSON, Postman v2.1 subset import/export, Bruno export, standalone environment import, and full configuration bundle integration.
- **cURL portability** — copy selected REST/GraphQL requests as masked cURL commands and import cURL commands into the active request target collection.
- **Variable inspector** — list request tokens with source metadata and masked/resolved values.
- **Response examples** — save named response examples onto a request, persisted with the collection. `Authorization`, `Set-Cookie` and similar headers are dropped and secret-looking header values redacted before an example is written, because collections can be committed to Git. Saved examples are clickable and shown with a "viewing saved example" banner and a return-to-live action.
- **Request tabs** — always on. A tab strip keeps several requests open at once, each with its own draft, dirty state, in-flight send and response history.

## Current Deferrals

- Pre-request scripts, arbitrary code execution, hosted collaboration, mock servers, gRPC, and automatic cookie jar remain out of scope.
- **Linked `.swebkit-api` collection roots** — the Blazor model where collections and environments loaded *from* a repository, with content-stamp conflict detection on linked files, has no React equivalent. The React app treats a repository as a folder containing API files and scopes Git operations to a configured subpath instead. See `docs/features/active/api-client-git-completion/decisions.md` DEC-G4.
- **Git rebase, stash, merge and conflict resolution** — conflicted files are listed and can be diffed, but not resolved in-app. `pull` and `push` are implemented.

## Core Runtime Flow

```text
ApiClientPage (React; owns collections, tabs, per-tab TabState)
  ├── useCollections / useEnvironments / useExecuteRequest  (react-query → sidecar)
  ├── toolbar (environment selector, collection variables, Git toggle)
  ├── ResizablePanels  (fractional widths, persisted, keyboard-operable)
  │     ├── CollectionTree            — panel 0, roughly fixed width
  │     ├── request pane              — panel 1, "1fr"
  │     │     ├── RequestTabStrip
  │     │     └── RequestEditor
  │     │           ├── URL bar + MethodBadge + Send/Save
  │     │           ├── RequestNameHeading (inline-editable title)
  │     │           ├── Params / Headers / Body / Auth / Capture
  │     │           ├── BodyCodeEditor (CodeMirror, swebkitHighlighting)
  │     │           ├── GraphQlPanel
  │     │           └── WebSocketPanel
  │     └── response pane             — panel 2, "1fr"
  │           └── ResponseViewer
  │                 ├── status bar (statusTone, formatBytes, formatElapsed)
  │                 ├── ResponseBodyViewer (pre | CodeMirror | CodeMirror-plain)
  │                 ├── headers / per-tab history + sparkline
  │                 └── saved response examples
  └── GitDrawer (backdrop, Escape, focus trap, resizable)
        └── GitPanel
              ├── branch switcher / create, pull, push, commit
              ├── GitFileList (staged / unstaged / conflicted, per-file actions)
              └── GitDiffPane (original vs current, unified or side-by-side)
```

`ApiClientPage` owns page-level truth: collections, the open tab list, and a `TabState` per tab
holding its draft, latest response, in-flight flag and response history. Child components are
presentational — they read props and raise callbacks. Response history lives in `TabState` rather
than inside `ResponseViewer` so it survives a remount and stays scoped per tab.

Shared presentation modules, so no surface invents its own vocabulary:

| Module                                            | Responsibility                                            |
| ------------------------------------------------- | --------------------------------------------------------- |
| `components/api-client/method-badge.tsx`          | `METHOD_META`, `MethodBadge`, `statusTone`, `CountBadge`   |
| `lib/codemirror-theme.ts`                         | `swebkitHighlighting()` — one theme for every editor       |
| `lib/response-body.ts`                            | language selection and the render-mode size thresholds     |
| `lib/bodyHighlight.ts`                            | token-based highlighting for the small-body `<pre>` path    |
| `lib/api-client-format.ts`                        | `formatBytes`, `formatElapsed`                             |
| `lib/response-example.ts`                         | example construction and header scrubbing                  |
| `lib/git-remote.ts` / `lib/git-status-format.ts`  | compare-URL inference; file section/action/label rules      |
| `components/ui/resizable-widths.ts`               | pure `fr` resolution and drag maths                        |

## Send Path

1. `ApiClientPage` loads collections and environments through react-query.
2. User selects or creates a collection/request; selecting one opens a tab.
3. `RequestEditor` routes send based on request method: REST, GraphQL HTTP, GraphQL subscription, or WebSocket.
4. The sidecar's `HttpRequestExecutor` builds a resolved variable scope, applies auth, sends the request, parses GraphQL errors, and runs capture rules.
5. The result flows back into the tab's `TabState`, is prepended to that tab's history, and renders through `ResponseViewer`.

## Git Action Path

All Git work runs through Tauri commands in `src-tauri/src/git.rs`. Each shells out to `git` with a
fixed argument array, passes paths after `--`, and validates the repository directory against
`AllowedRoots` before running.

1. The user picks a repository through the native directory dialog. That pick is what grants access:
   the path is added to the in-memory `AllowedRoots` **and** appended to a Rust-side persisted grant
   list, so it can be re-admitted after a restart via `restore_allowed_root`. The frontend persists
   only which repository is selected, never authorization.
2. `git_status` and `git_changed_files` run `git status --porcelain=v2 --branch` and share one pure
   parser, `parse_porcelain_v2`, which splits the two-character `XY` field into index and worktree
   state. A file changed in both counts once as staged and once as modified.
3. `git_changed_files` filters to the configured API subpath, matching on a path-segment boundary so
   `apixyz/` is not treated as being inside `api/`.
4. The UI shows the branch, per-state counters, and staged/unstaged/conflicted file sections with
   per-file actions.
5. `git_stage_paths` / `git_unstage_paths` / `git_revert_paths` take an explicit file list — there is
   no "stage everything" command — and re-validate every path against live status before acting.
   `git_revert_paths` additionally refuses untracked files and requires a UI confirmation naming each
   file, as it is the only irreversible operation.
6. `git_diff_file` reads the committed version with `git show HEAD:<path>` and the working copy from
   disk, reporting `original: null` for a new file and detecting binary content by NUL byte.
7. `git_commit` re-reads status and refuses to commit when staged files fall outside the API subpath,
   naming them. The commit form shows the exact list it will commit.
8. Branch switching and creation use `git checkout` / `git checkout -b`; branch names are validated by
   `git check-ref-format --branch` rather than a hand-rolled pattern.
9. `git_remote_url` returns the raw remote; `lib/git-remote.ts` infers a GitHub or Azure DevOps
   compare URL and returns `null` for anything unrecognized so no broken link is rendered.

## State Persistence

| State                              | Location                                              | Lifetime                                    |
| ---------------------------------- | ----------------------------------------------------- | ------------------------------------------- |
| Collections and requests           | `AppData/collections.json`                            | Persistent                                  |
| Environments and API UI state      | `AppData/environments.json`                           | Persistent                                  |
| Response examples                  | `HttpRequestEntry.responseExamples`                   | Persistent with the request, secrets scrubbed |
| Secret values                      | OS keychain via `secrets.rs`                          | Persistent outside repo files               |
| Granted repository roots           | `<appConfigDir>/granted-roots.json` (Rust-side)       | Persistent — the Git authorization list     |
| Selected repository + API subpath  | `localStorage: api-client-git-repos`                  | Persistent, machine-local, selection only   |
| Panel widths                       | `localStorage: panel-widths:api-client-panels`        | Persistent, versioned (reset on version bump) |
| Response wrap / Git drawer width   | `localStorage: view-pref:*`                           | Persistent                                  |
| Response history                   | `TabState.history` in `ApiClientPage`                 | Session only, per tab, capped at 20         |
| Open request tabs                  | `ApiClientPage` state                                 | Session only (not persisted across restart) |
| WebSocket message log              | `WebSocketPanel` state                                | Session/request only                        |
| GraphQL subscription messages      | `GraphQlPanel` state                                  | Session/request only                        |
| Generated sample values            | request scope/preview only                            | Not persisted                               |

## Security and Safety Notes

- Export formats never include secret values.
- The transient `credentialSecret` is stripped from every write to `collections.json`, including
  response-example saves.
- Response examples drop `Authorization`, `Set-Cookie` and similar headers, and redact values of
  headers whose name looks secret — collections can be committed to Git.
- Generated variables are non-secret and cannot execute code.
- Git operations are fixed command builders, never arbitrary command execution, and paths are always
  passed after `--` so a filename cannot be read as a flag.
- Every Git command validates its repository directory against `AllowedRoots`, the same gate the
  filesystem commands use. A directory only enters that list when the user picks it in an OS dialog;
  a path written into `localStorage` is never sufficient to authorize access.
- Git staging and committing are scoped to the configured API subpath, and a commit is refused when
  staged files fall outside it.
- `git_revert_paths` — the only irreversible operation — re-validates against live status and refuses
  untracked files, and the UI confirms by naming every affected file.
- Key Vault failure degrades gracefully rather than crashing request execution.

## Validation Focus

Frontend units run under Vitest (`npm --prefix web run test:unit`); Rust units under
`cargo test --lib` in `src-tauri`; behaviour under Playwright (`npm --prefix web run test:e2e`).

- `parse_porcelain_v2` against captured real porcelain output for every state: staged-only,
  unstaged-only, both, renamed, unmerged, untracked, ignored, and paths containing spaces
- API-subpath filtering on a path-segment boundary, including Windows separators
- `AllowedRoots` rejection for every Git command, not just one
- compare-URL inference across HTTPS/SSH/legacy Azure forms, with credentials stripped and
  unrecognized remotes returning `null`
- byte/duration formatting, including the sidecar's `-1` unknown-length sentinel
- content-type → highlight-language selection, including body sniffing and binary
- response render-mode thresholds, asserted by constant rather than magic number
- versioned panel-width persistence, including the deliberate reset on a version bump
- fractional width resolution and drag clamping at panel minimums
- response-example header scrubbing — no credential may appear in a persisted example
- request execution with auth, variable substitution, generated values, and capture rules
- layout proportions on wide and narrow windows, keyboard-operable dividers, and highlighting
  producing distinct colours in every theme
