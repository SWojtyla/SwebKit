# Status — API Client Git Completion

## Current State

`Review` — implemented on branch `feat/api-client-ux-and-git` on 2026-07-31, alongside
[api-client-ux-overhaul](../api-client-ux-overhaul/index.md). Not committed.

**Jira:** not linked

## Origin

User asked on 2026-07-31 whether the API Client's Git integration was fully implemented. It is not.
A code-level audit found seven defects and six documented-but-missing capabilities, listed with line
references in [index.md](index.md). Three findings are worse than incomplete — they are actively
misleading:

- The staged/modified counters are always wrong (the porcelain-v2 parse can never match its own
  comparison).
- "Stage All & Commit" stages the **whole repository**, so it can commit unrelated work.
- "Re-import Bruno" and "Export Bruno" do nothing real and report success.

## Progress

### Module 1 — Fixes
- [x] 1.1 (D2) `parse_porcelain_v2` extracted as a pure function; `XY` split into index/worktree state
- [x] 1.1 `conflicted` counter added to `GitStatus`; `!` ignored lines handled explicitly
- [x] 1.2 (D1) `"."` default removed; first-run state with **Choose repository**
- [x] 1.2 Repo path + `apiSubpath` persisted; `git_is_repo` validation on load
- [x] 1.2 `restore_allowed_root` with a Rust-side granted-roots list (DEC-G3)
- [x] 1.2 Repo list plumbing so multi-repo needs no later migration
- [x] 1.3 (D3) Both Bruno buttons and their handlers deleted; unused imports removed
- [x] 1.4 (D4) Five distinct error/empty states; `gitStatus` throws instead of returning `null`
- [x] 1.5 (D5) `git_stage_all` replaced by `git_stage_paths`; commit refuses out-of-scope staged files
- [x] 1.5 Commit form shows the exact file list it will commit
- [x] 1.6 (D6) Overlay converted to a drawer: backdrop, Escape, focus trap, in-flow close, resizable
- [x] 1.7 (D7) `validate_dir_within_roots` added; every git command gated on `AllowedRoots`

### Module 2 — New Tauri commands
- [x] `git_is_repo`
- [x] `git_changed_files` (reusing the Module 1.1 parser)
- [x] `git_stage_paths` / `git_unstage_paths` / `git_revert_paths` — all `--`-separated
- [x] `git_diff_file` with binary detection
- [x] `git_checkout_branch` / `git_create_branch` (name validated via `git check-ref-format`)
- [x] `git_remote_url`
- [x] `git_stage_all` removed, not deprecated
- [x] All registered in `lib.rs`

### Module 3 — Panel UI
- [x] Branch switcher replaces the inert list; **New branch** via the existing `NameDialog`
- [x] Changed-file list with staged / unstaged / conflicted sections and per-row actions
- [x] Numeric summary strip retained so existing count testids survive — with correct values
- [x] Diff pane reusing the read-only CodeMirror viewer and `--cm-*` tokens
- [x] Revert confirmation naming each affected file (DEC-G6)
- [x] Remote compare link via `inferCompareUrl`, opened through the shell plugin
- [x] Feedback routed through the app notification system

### Module 4 — Bridge layer
- [x] Wrappers for every new command
- [x] `GitUnavailableError` / `GitCommandError` typing
- [x] React Query hooks with explicit invalidation after every mutation

### Module 5 — Documentation
- [x] `api-client.md` Git bullets rewritten to describe the React implementation
- [x] Linked `.swebkit-api` roots recorded as a deferral (DEC-G4); `pull` removed from deferrals
- [x] Git Action Path rewritten around the Tauri commands
- [x] State Persistence rows updated for repo path/subpath and granted roots
- [x] Security notes state the `AllowedRoots` gate and API-subpath scoping

### Testing
- [x] `#[cfg(test)]` module added — in the new `src-tauri/src/git.rs`, first tests in the crate
- [x] All `parse_porcelain_v2` cases from [test-plan.md](test-plan.md), including the D2 regressions
- [x] Integration tests over temporary repositories — in `git::repo_tests` (in-crate rather than
      `tests/git_commands.rs`, so the `*_impl` functions are reachable without a Tauri app)
- [x] `AllowedRoots` rejection asserted for **every** git command individually
- [x] Vitest: `git-remote.test.ts`, `git-status-format.test.ts`
- [x] Existing 2 git-panel e2e tests pass; Bruno-button-absence guard added
- [ ] Full manual desktop verification, steps 1–9 — **not done** (requires a Tauri build and a real repository)

## Definition of Done

1. The panel's staged / modified / untracked / conflicted counts match `git status --short` exactly,
   verified manually against a repository with all four states present.
2. No git operation can stage or commit a file outside the configured API subpath, verified by
   `git show --stat` after a scoped commit with unrelated work present.
3. No UI element reports success for an operation that did not happen — the Bruno buttons are gone and
   every failure surfaces its real cause.
4. No git command runs against a path the user did not choose through an OS dialog, in this session or
   restored from a previously granted root.
5. A user can list changed files, stage and unstage individually, view a diff, revert with a named
   confirmation, switch and create branches, commit, and push — all from the panel.
6. `revert` never runs without a confirmation naming the affected files.
7. `cargo test` green in `src-tauri`, including every parser case in [test-plan.md](test-plan.md).
8. Vitest green in `web/`; Playwright suite green including the pre-existing git-panel tests.
9. `docs/architecture/functionalities/api-client.md` no longer claims capabilities the React app lacks,
   and records linked `.swebkit-api` roots as an explicit deferral.
10. `ship-readiness` run clean before merge.

## Deviations from the plan

| Planned | Shipped | Why |
| --- | --- | --- |
| Git commands stay in `native.rs` | New `src-tauri/src/git.rs` module | The file would have grown past 700 lines, and the parser plus its 26 tests deserve their own home. `hidden_command`, `AllowedRoots::allow/is_allowed` and the new `validate_dir_within_roots` are now `pub(crate)`. |
| `git_stage_paths(repo, paths)` etc. | Same, plus `validate_reported_paths` on stage/unstage/revert | Validating against live status on *all three* mutations, not just revert, costs one extra status read and closes the stale-frontend-list gap generally. |
| Binary detection via `git diff --numstat` | NUL-byte check on the file bytes | `--numstat` reports nothing for untracked files, which is exactly the case the diff pane needs to handle. |
| Multi-repo picker described as optional | Implemented (repo list + selector in Git settings) | It cost almost nothing once the config was a list, and avoids a storage migration later. |
| `#[serde(rename_all = "camelCase")]` not mentioned | Added to `GitFileChange` and `GitFileDiff` | Tauri does not camelCase returned struct fields, so `index_state` would have arrived as `undefined` in TypeScript. Recorded in `docs/pitfalls/react-frontend.md`. |
| — | `conflicted` added to `GitStatus` and surfaced as a counter | Unmerged entries were previously folded into `modified`, which is wrong and hides conflicts. |

## Verification status

| Check | Result |
| --- | --- |
| `cargo test --lib` in `src-tauri` | 50 passed — the crate's first tests (26 parser + 24 repository) |
| `npm --prefix web run test:unit` | 116 passed across 9 files (both features) |
| Playwright git-panel specs | 6 passed (2 pre-existing + 4 new) |
| Rust integration tests over real temp repositories | 24 passed, in `git::repo_tests` |
| Manual desktop verification (steps 1–9) | **Not performed — see below** |

The integration tests cover what the parser tests cannot: per-file staging leaving unnamed files
alone, paths containing spaces and paths starting with `-` (proving the `--` separator), revert
restoring committed content and refusing untracked files, all three mutations rejecting an unreported
path, diff on modified/new/binary files, branch create/checkout round-trip and invalid-name rejection,
the out-of-subpath commit guard both refusing and allowing, and **every command rejecting a path
outside `AllowedRoots`** — asserted per command, not once.

To make this possible each `#[tauri::command]` is now a one-liner over a plain `*_impl` function
taking `&AllowedRoots`, so the behaviour is reachable without standing up a Tauri app.

## Outstanding before this can be called done

**Manual desktop verification has not been performed.** Steps 1–9 in [test-plan.md](test-plan.md)
need a real repository under `npm --prefix web run tauri dev`. What automated tests cannot cover:

- the Tauri IPC serialization boundary end to end (the `camelCase` fix is asserted only by the TS
  types compiling, not by a round trip)
- `restore_allowed_root` surviving an actual app restart
- push/pull against a real remote with the user's credential helper
- the drawer, diff pane and file list rendering under the desktop shell rather than a browser

Definition of Done items 1–6 are verified by the Rust tests at the command layer; the same items are
**not** verified through the UI. Steps 2 (counter correctness) and 3 (scoped staging) matter most —
both previously failed silently.

## Blockers and sequencing

- **Blocked on [api-client-ux-overhaul](../api-client-ux-overhaul/index.md)** for two reasons: both
  features edit `ApiClientPage.tsx`'s render tree, and Module 3.3's diff pane reuses that feature's
  read-only CodeMirror viewer and `--cm-*` highlight tokens. Start Modules 1.1–1.4 (Rust and panel
  internals) in parallel if useful — they do not touch `ApiClientPage.tsx`.
- **Requires a Rust build.** None of the behaviour is verifiable in the browser dev server; plan for
  desktop verification time.
- Steps 1–3 of the implementation order are independently shippable and worth landing on their own if
  the feature has to be cut short: they turn a panel that misreports state into one that is merely
  limited.

## Notes

- `src-tauri` has no tests today. Adding `#[cfg(test)]` here sets the precedent for the crate — call it
  out in review rather than letting it land unremarked inside a feature change.
- The porcelain-v2 parse has been wrong since it was written and nobody noticed, because wrong counts
  still look like counts. The pure-function extraction plus the table of captured-output cases in
  [test-plan.md](test-plan.md) is the actual fix; correcting the arithmetic is the easy part.
