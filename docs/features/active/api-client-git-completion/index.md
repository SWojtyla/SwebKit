# API Client Git Completion — fix the defects, finish the panel

## Summary

The API Client's Git panel is a thin shell over six Tauri commands. It presents a branch list you
cannot click, three counters computed from a broken parse, a "Stage All & Commit" that stages the
**entire** repository, two Bruno import/export buttons that do nothing real, and a repo path
hardcoded to `"."`. Meanwhile
[docs/architecture/functionalities/api-client.md](../../../architecture/functionalities/api-client.md)
documents a far richer feature — per-file staging, diff review, branch switching, API-root scoping —
which exists only in the deleted Blazor implementation.

This feature makes the panel honest and then makes it useful: fix the three defects, remove the two
fakes, and add the missing Tauri commands and UI for a changed-file list, per-file
stage/unstage/revert, a diff preview, and branch switch/create.

**Jira:** not linked

## Goal

A developer with API collections in a Git repository can review exactly what changed, stage
selectively, see a diff before committing, switch branches, and push — without leaving SwebKit and
without ever accidentally committing unrelated work.

## Verified defects

| # | Defect | Location |
|---|---|---|
| D1 | Repo path hardcoded to `"."`; "Link Repo" result is never persisted, so it resets to `"."` on every remount. In dev this reports SwebKit's own repository. | [ApiClientPage.tsx:237](../../../../web/src/components/api-client/ApiClientPage.tsx) |
| D2 | Staged/Modified counters are always wrong. The porcelain-v2 parse compares the two-character `XY` field against `"."`, which can never match — so every changed file counts as *staged* and `modified` stays 0. | [src-tauri/src/native.rs:325](../../../../src-tauri/src/native.rs) |
| D3 | "Re-import Bruno" copies `collections.json` to `collections.bru.json` and imports nothing. "Export Bruno" writes `{"exported": true, timestamp}`. Both report success. | [GitPanel.tsx:207-239](../../../../web/src/components/api-client/GitPanel.tsx) |
| D4 | `if (!status)` renders "Git integration requires the Tauri desktop app" for *any* failure — not a repo, git missing, permission denied — while the real message sits unused in `error` state. | [GitPanel.tsx:90](../../../../web/src/components/api-client/GitPanel.tsx) |
| D5 | `git_stage_all` runs `git add --all` across the whole repository, so "Stage All & Commit" can commit unrelated work in progress. The documented Blazor behaviour scoped every operation to the API root. | [native.rs:413](../../../../src-tauri/src/native.rs) |
| D6 | The panel is a `fixed inset-y-0 right-0` overlay covering the app titlebar and status bar, with no backdrop, no Escape-to-close, and a close button overlapping its own header. | [ApiClientPage.tsx:834](../../../../web/src/components/api-client/ApiClientPage.tsx) |
| D7 | Git commands accept an arbitrary `path` and skip the `AllowedRoots` check that the filesystem commands in the same module enforce. | [src-tauri/src/native.rs:274-424](../../../../src-tauri/src/native.rs) |

## Verified missing capabilities

Documented as supported, absent from React/Tauri:

| Capability | Rust command | UI |
|---|---|---|
| Changed-file list with per-file status | missing | missing — only three counters |
| Per-file stage / unstage / revert | missing | missing — only Stage All |
| Original vs current diff preview | missing | missing |
| Branch switch | missing | branch list has no click handler |
| Branch create | missing | missing (the `Plus` icon is the Commit button) |
| Remote compare link (GitHub / Azure DevOps inference) | missing | missing |

## Scope

### Fixes
- D1 — persist the selected repository path; offer a repo picker; no silent `"."` default.
- D2 — correct porcelain-v2 `XY` parsing so staged, unstaged, and both-modified are distinguished.
- D3 — delete both Bruno buttons. They belong nowhere, least of all in a Git panel.
- D4 — distinguish "not running under Tauri" from "git failed", and surface the real error.
- D5 — scope staging and committing to a configured API-collections subpath within the repo.
- D6 — convert the overlay into a proper drawer: backdrop, Escape-to-close, focus trap, respecting
  the app chrome.
- D7 — apply the same `AllowedRoots` gate the filesystem commands use.

### New capability
- `git_changed_files` — path, index status, worktree status, staged/unstaged classification.
- `git_stage_file`, `git_unstage_file`, `git_revert_file` — single-path operations, validated against
  the reported file list before invoking Git.
- `git_diff_file` — original vs current text for a changed file.
- `git_checkout_branch`, `git_create_branch`.
- `git_remote_url` plus provider inference for a GitHub / Azure DevOps compare link.
- UI: a changed-file list with staged/unstaged sections, per-row actions, a diff pane, and a branch
  switcher replacing the inert list.

## Non-Goals

- **Linked `.swebkit-api` collection roots.** The Blazor concept — linked collections and
  environments loaded beside local ones, linked-file save with content-stamp conflict detection —
  exists only in `src/SwebKit.Core` and `src/SwebKit.App` and has no React equivalent. Porting it is a
  larger feature in its own right. This feature treats the repository as a folder containing API
  files, not as a source of linked collections.
- **Rebase, stash, merge, cherry-pick, conflict resolution.** Out of scope per the existing deferral
  list in the canonical feature doc. `pull` already exists and is kept.
- **Credential/auth handling for push.** Relies on the user's existing Git credential helper, as
  today.
- **Arbitrary Git command execution.** Every operation stays a fixed argument builder.
- **Layout, colour, and highlighting work** — tracked in
  [api-client-ux-overhaul](../api-client-ux-overhaul/index.md).

## Relationship to existing plans

Promotes finding **#10** from
[post-migration-ux-review](../post-migration-ux-review/status.md) ("Multi-repo Git management
collapsed to one hardcoded repo") into a real plan, and supersedes it with a wider scope: that
finding only noticed the hardcoded path, not the broken counters, the fake Bruno buttons, or the
unscoped `git add --all`.

Finding **#11** (conflict-resolution UI) is already closed — the banner exists at
[ApiClientPage.tsx:706](../../../../web/src/components/api-client/ApiClientPage.tsx).

## Dependencies

- **Land [api-client-ux-overhaul](../api-client-ux-overhaul/index.md) first.** Both features modify
  `ApiClientPage.tsx`'s render tree — that one for the panel layout, this one for the git drawer.
  Sequencing avoids a conflict in the same JSX.
- Requires a Rust build (`src-tauri`), so the change cannot be verified in the browser dev server
  alone. The panel's current e2e coverage only asserts the browser-mode "unavailable" message.
- No sidecar (C#) changes.

## Risks

| Risk | Mitigation |
|---|---|
| Git operations are destructive — `revert_file` discards uncommitted work | Confirmation dialog naming the exact file; validate the path against the current changed-file list before invoking Git; never accept a caller-supplied path unchecked |
| Widening the Tauri command surface widens the attack surface reachable from the webview | Apply the existing `AllowedRoots` gate (D7); keep every command a fixed argument builder; never pass user text as a Git flag; pass paths after `--` |
| API-root scoping (D5) could be wrong and stage too little, so a user believes work is committed when it isn't | Show the exact staged file list in the commit preview before committing, and refuse to commit when unrelated staged files are present — the behaviour the Blazor version had |
| Porcelain-v2 parsing is easy to get subtly wrong a second time | Unit-test the parser in Rust against captured real porcelain output for every state: staged-only, unstaged-only, both, renamed, copied, unmerged, untracked, ignored |
| Repo path persistence could point at a stale or deleted directory after a restart | Validate on load; fall back to the picker with a clear message rather than reporting a confusing git error |
| A large diff renders slowly | Cap the rendered diff and offer "open in external tool"; reuse the size-threshold approach from the UX feature |

## Related docs

- [technical-plan.md](technical-plan.md) — commands, signatures, UI structure
- [decisions.md](decisions.md) — scoping model, safety posture
- [test-plan.md](test-plan.md) — Rust unit tests, e2e, manual verification
- [status.md](status.md) — progress checklist and Definition of Done
- [docs/architecture/functionalities/api-client.md](../../../architecture/functionalities/api-client.md) —
  canonical doc; its Git sections currently describe Blazor and are corrected by Module 5
