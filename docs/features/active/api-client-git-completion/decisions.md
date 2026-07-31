# Decisions — API Client Git Completion

## DEC-G1 — Git stays shell-out with fixed argument builders; no libgit2

**Decision.** Keep invoking the `git` binary through `hidden_command` with fixed argument arrays, as
the six existing commands do. Do not adopt `git2`/libgit2.

**Why.** The existing pattern is already the safe one and is called out as deliberate in the
`native.rs` comments. It inherits the user's credential helpers, SSH agent, `.gitconfig`, hooks, and
corporate proxy settings for free — all of which matter for `push`/`pull` against Azure DevOps and
GitHub, and all of which libgit2 would require reimplementing. libgit2 would also add a substantial
native dependency and a second authentication story.

**Consequence.** Requires `git` on `PATH`; the panel must detect and report its absence explicitly
(D4). Output parsing is our responsibility, which is why `parse_porcelain_v2` is extracted as a pure,
unit-tested function rather than being inlined a second time.

**Rejected.** libgit2 — better structured output, materially worse authentication and configuration
compatibility.

---

## DEC-G2 — `git_stage_all` is deleted, not scoped

**Decision.** Remove `git_stage_all` entirely and replace it with
`git_stage_paths(repo, paths: Vec<String>)`, where the caller passes the explicit file list.

**Why.** `git add --all` is unscoped by construction. Scoping it by passing a pathspec would work but
leaves the scope decision inside the command, where it is invisible to the user. Passing an explicit
list means the UI can show exactly which files are about to be staged — which is also what makes the
commit preview honest, and what the documented Blazor behaviour provided.

**Consequence.** The UI must always compute the file list first, so `git_changed_files` becomes a
prerequisite for staging rather than an optional nicety. That ordering is intentional.

**Rejected.** Keeping `git_stage_all` with a pathspec argument — one fewer round trip, but preserves
the "I don't know what I just staged" failure mode that motivates this feature.

---

## DEC-G3 — Granted repository roots are persisted on the Rust side, not the frontend

**Decision.** The list of user-granted repository roots is persisted by the Rust layer and re-admitted
into `AllowedRoots` at startup via `restore_allowed_root`. The frontend persists only *which* of those
roots is currently selected.

**Why.** `AllowedRoots` is in-memory and is populated **only** by the native `pick_file`/
`pick_directory` dialogs, so the frontend can never grant itself access to a path. Persisting the
repository path in `localStorage` and passing it back after a restart would break that property: any
script in the webview could write an arbitrary path into `localStorage` and have it accepted as a
granted root. Keeping the authority list on the Rust side preserves "the user picked this, in an OS
dialog" as the only way a path is ever admitted.

**Consequence.** One new command and a small persisted file on the Rust side. The frontend stores an
identifier or path that is only ever *matched against* the Rust-side list, never trusted as
authorization on its own.

**Rejected.** Persisting the path in `localStorage` and re-adding it to `AllowedRoots` on request —
simpler, and a straightforward sandbox escape. Re-prompting for the folder on every app start —
secure but a poor experience for the feature's main use case.

---

## DEC-G4 — Linked `.swebkit-api` collection roots stay out of scope

**Decision.** This feature treats the repository as a folder that happens to contain API files. It
does not port the Blazor linked-root model — linked collections and environments loaded beside local
ones, `LinkedCollectionFileService` content-stamp save conflict detection,
`LinkedCollectionRootRepository`.

**Why.** That model spans `src/SwebKit.Core/Configuration/LinkedCollectionRootRepository.cs`,
`LinkedCollectionFileService.cs`, `LinkedCollectionModels.cs` and their React equivalents — a
collections-storage feature, not a Git-panel feature. Bundling it would make this change several times
larger and would block the defect fixes behind a storage redesign.

**Consequence.** The panel operates on paths, not on collection identity, so it cannot say "this
change belongs to collection X". The `apiSubpath` setting (technical plan 1.5) is the pragmatic
substitute. The canonical feature doc must record linked roots as a deferral so the gap is documented
rather than implied (Module 5).

---

## DEC-G5 — Provider inference lives in TypeScript, not Rust

**Decision.** `git_remote_url` returns the raw remote string; GitHub/Azure DevOps detection and
compare-URL construction happen in `web/src/lib/git-remote.ts`.

**Why.** It is pure string manipulation with many input shapes (HTTPS, SSH, `git@`, Azure's two URL
generations, `.git` suffixes, embedded credentials). Testing that in TypeScript is fast and needs no
repository or Rust test harness. Nothing about it needs native access.

**Consequence.** Adding a provider is a frontend change with no Rust rebuild. Unrecognized remotes
return `null` and no link is rendered — never a guessed URL.

---

## DEC-G6 — `revert` is the only destructive operation, and it confirms by name

**Decision.** `git_revert_paths` requires an explicit confirmation dialog naming each affected file,
and the command re-validates every path against the current changed-file list before invoking Git.

**Why.** It is the one operation in this feature that destroys uncommitted work with no Git-side
recovery. Double validation — UI confirm plus command-side membership check — means a stale frontend
list cannot cause a revert of a file the user never saw.

**Consequence.** `git_revert_paths` needs a status read before acting, making it slower than the other
commands. That is an acceptable trade for the only irreversible action in the panel.

**Rejected.** A generic "are you sure?" without file names — the pattern that makes users click
through confirmations without reading them.
