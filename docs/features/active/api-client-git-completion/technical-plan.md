# Technical Plan — API Client Git Completion

Touches `src-tauri` (Rust) and `web/src` (React). **No sidecar (C#) changes.**

## Existing surface

Six commands, registered in [src-tauri/src/lib.rs:46-51](../../../../src-tauri/src/lib.rs) and
implemented in [src-tauri/src/native.rs:274-424](../../../../src-tauri/src/native.rs):
`git_status`, `git_branches`, `git_commit`, `git_push`, `git_pull`, `git_stage_all`. Each shells out
through `hidden_command("git")` with `current_dir(&path)` and fixed arguments — the right pattern,
which this feature keeps.

Frontend wrappers are in
[web/src/lib/tauri-bridge.ts:114-172](../../../../web/src/lib/tauri-bridge.ts); the panel is
[web/src/components/api-client/GitPanel.tsx](../../../../web/src/components/api-client/GitPanel.tsx),
mounted as a fixed overlay from
[ApiClientPage.tsx:833](../../../../web/src/components/api-client/ApiClientPage.tsx).

`AllowedRoots` ([native.rs:23-64](../../../../src-tauri/src/native.rs)) is the existing security
model: roots are added **only** when the user picks a path through the native dialog
(`pick_file` / `pick_directory`), and `read_file` / `write_file` / `list_dir` validate against it.
The git commands do not.

---

## Module 1 — Fix the defects

### 1.1 (D2) Correct the porcelain-v2 parse

`native.rs:322-333` today:

```rust
} else if line.starts_with('1') || line.starts_with('2') {
    let fields: Vec<&str> = line.split_whitespace().collect();
    if fields.len() >= 2 {
        let staged_field = fields[1];
        if staged_field != "." { staged += 1; }   // XY is always 2 chars — never "."
        else { modified += 1; }
    }
}
```

Porcelain v2 ordinary entries are `1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>` and renamed/copied
entries are `2 <XY> … <path><sep><origPath>`. `XY` is always exactly two characters — `X` is the
index (staged) state, `Y` the worktree state — so `fields[1] != "."` is unconditionally true. Every
changed file is counted as staged and `modified` is never incremented.

Replace with a dedicated parser that splits the two characters:

```rust
struct FileStatus {
    path: String,
    index_state: char,     // X — '.' means unchanged in the index
    worktree_state: char,  // Y — '.' means unchanged in the worktree
}

// counting:
if index_state != '.' { staged += 1; }
if worktree_state != '.' { modified += 1; }
```

A file modified both in the index and the worktree correctly counts once in each — that is what Git
means, and what the UI must show. Unmerged (`u`) entries get their own `conflicted` counter rather
than being folded into `modified` as they are today; add `conflicted: u32` to `GitStatus` and surface
it. Also handle `!` (ignored) lines, currently unhandled and silently ignored — which is correct
behaviour, but should be explicit rather than accidental.

Extract the parser as a pure `fn parse_porcelain_v2(text: &str) -> (GitStatus, Vec<FileStatus>)` so it
is unit-testable without a repository. This is the single most important testability change in the
feature — see [test-plan.md](test-plan.md).

### 1.2 (D1) Real repo selection, persisted

- Remove the `useState("."` default in `ApiClientPage.tsx:237`. With no repository selected the panel
  shows a first-run state: an explanation and a **Choose repository** button. No implicit `"."`.
- Persist the chosen path following the `sb-preferences.ts` localStorage pattern
  (`api-client-git-repo`), and validate it on load with a new `git_is_repo(path)` command; if it no
  longer resolves, clear it and return to the first-run state with a plain message.
- Because `AllowedRoots` is **in-memory only**, a persisted path is not authorized after a restart.
  Add a `restore_allowed_root(path)` command that re-admits a path the user previously picked, backed
  by a Rust-side persisted list of granted roots so provenance survives the restart. See
  [decisions.md](decisions.md) DEC-G3 — this is a security-relevant design point, not plumbing.
- Keep the door open for several repositories: store a list and render a `<select>` above the panel.
  A single entry renders as a static label, so the multi-repo case costs almost nothing now and avoids
  another migration later.

### 1.3 (D3) Delete the Bruno buttons

Remove both handlers and buttons at `GitPanel.tsx:207-239`. Re-import copies `collections.json` to
`collections.bru.json` and imports nothing; export writes a two-field object with no collection data.
Both then report success. Real Bruno import/export already lives in
[CollectionExportDialog.tsx](../../../../web/src/components/api-client/CollectionExportDialog.tsx),
which is where it belongs — a Git panel is not an import/export surface.

Also drop the now-unused `readFile` / `writeFile` imports from `GitPanel.tsx:3`.

### 1.4 (D4) Honest error states

`GitPanel.tsx:90` collapses every failure into "Git integration requires the Tauri desktop app".
Replace with explicit states:

| Condition | UI |
|---|---|
| Not running under Tauri (`isTauri()` false) | "Git actions need the SwebKit desktop app" |
| No repository selected | First-run state with **Choose repository** |
| Selected path is not a Git repository | "…is not a Git repository" + change-repository action |
| `git` not on `PATH` | "Git was not found on this system" + install hint |
| Any other failure | The actual stderr from the command, in the existing `git-error` region |

`tauri-bridge.ts:130` already swallows the failure by returning `null` from `gitStatus`; change it to
throw a typed error so the panel can distinguish these instead of inferring from a `null`.

### 1.5 (D5) Scope staging and committing to the API path

`git_stage_all` runs `git add --all`, so "Stage All & Commit" can sweep in unrelated work in
progress. The documented Blazor behaviour scoped every operation to the linked API root.

- Add an optional `apiSubpath` to the stored repository config (default: repository root, with a
  clear warning in the UI when it is the root).
- Replace `git_stage_all(path)` with `git_stage_paths(repo, paths: Vec<String>)`, invoked as
  `git add -- <paths…>`. "Stage all changed API files" then passes the filtered file list explicitly
  rather than delegating the scope decision to Git.
- Before committing, re-read status and **refuse** to commit when staged files exist outside
  `apiSubpath`, listing them — matching the documented Blazor guard. The commit form shows the exact
  file list it is about to commit.

### 1.6 (D6) Proper drawer

The panel is `fixed right-0 top-0 bottom-0 z-40 w-96` (`ApiClientPage.tsx:834`), covering the app
titlebar and status bar, with the close button overlapping its own header. Convert to a drawer:

- Positioned within the page content area, not the whole viewport.
- Dimmed backdrop that closes on click.
- `Escape` closes; focus moves into the drawer on open and returns to the toggle on close;
  `role="dialog"` + `aria-modal` + `aria-label`.
- Close button inside the drawer header, in flow, not absolutely positioned over it.
- Width resizable and persisted, reusing the `ResizablePanels` persistence from
  [api-client-ux-overhaul](../api-client-ux-overhaul/technical-plan.md) Module 1.

### 1.7 (D7) Gate git commands on `AllowedRoots`

Every git command takes `roots: State<'_, AllowedRoots>` and validates the repository directory before
running. `validate_within_roots` canonicalizes the *parent* directory because it is written for files;
add a sibling `validate_dir_within_roots(path, roots)` that canonicalizes the directory itself, and use
it in the git commands. Without this, any script in the webview can run `git push` in an arbitrary
directory — inconsistent with the explicit reasoning in the comment at `native.rs:426-431`.

---

## Module 2 — New Tauri commands

All in `native.rs`, registered in `lib.rs`, each validated with `validate_dir_within_roots` and built
from fixed arguments. Paths are always passed after `--` so a filename can never be read as a flag.

```rust
#[derive(serde::Serialize)]
pub struct GitFileChange {
    pub path: String,
    pub index_state: String,     // "M", "A", "D", "R", "." …
    pub worktree_state: String,
    pub staged: bool,            // index_state != "."
    pub unstaged: bool,          // worktree_state != "."
    pub untracked: bool,
    pub conflicted: bool,
    pub orig_path: Option<String>,  // rename/copy source
}

git_is_repo(path)                        -> bool
git_changed_files(path, subpath: Option<String>) -> Vec<GitFileChange>
git_stage_paths(path, paths: Vec<String>)        -> ()   // git add -- <paths>
git_unstage_paths(path, paths: Vec<String>)      -> ()   // git restore --staged -- <paths>
git_revert_paths(path, paths: Vec<String>)       -> ()   // git restore --worktree -- <paths>
git_diff_file(path, file: String)        -> GitFileDiff  // { original: Option<String>, current: String, is_binary: bool }
git_checkout_branch(path, branch: String)        -> ()   // git checkout <branch>
git_create_branch(path, branch: String, checkout: bool)  -> ()
git_remote_url(path)                     -> Option<String>
restore_allowed_root(path)               -> ()           // see 1.2 / DEC-G3
```

Notes:

- `git_changed_files` reuses `parse_porcelain_v2` from 1.1 — one parser, one set of tests.
- `git_diff_file` reads the committed version with `git show HEAD:<path>` and the working copy from
  disk, matching the "original/current" model the Blazor version used. `original: None` means the file
  is new. Detect binary via `git diff --numstat` reporting `-` and set `is_binary` rather than
  returning garbage text.
- `git_revert_paths` is the only destructive command; the confirmation lives in the UI (Module 3) and
  the command validates every path against the current changed-file list before acting.
- `git_create_branch` validates the branch name against `git check-ref-format --branch` rather than a
  hand-rolled regex, so the rules are Git's, not ours.
- `git_remote_url` returns the raw remote; provider inference and compare-URL construction happen in
  TypeScript (Module 3.4) where they are cheap to unit-test.
- `git_stage_all` is **removed**, not kept as a deprecated alias. It is unscoped by nature and there
  is exactly one caller.

---

## Module 3 — Panel UI

**File:** `GitPanel.tsx`, likely split into `GitPanel.tsx` + `GitFileList.tsx` + `GitDiffPane.tsx`
once it exceeds ~250 lines.

### 3.1 Header and branch switcher

Replace the display-only list (`GitPanel.tsx:244-254`) with:
- A branch `<select>` (or searchable combobox when the repo has many branches) that calls
  `git_checkout_branch`. Block switching while a checkout would fail and surface Git's own message.
- **New branch** action prompting for a name, calling `git_create_branch(checkout: true)`, reusing
  the existing `NameDialog` from
  [Dialogs.tsx](../../../../web/src/components/api-client/Dialogs.tsx) rather than a new prompt.
- Ahead/behind indicators kept, plus a compare link when `git_remote_url` resolves (3.4).

### 3.2 Changed-file list

Replace the three counters with staged and unstaged sections, each row showing a status letter, the
path relative to `apiSubpath`, and per-row actions:

| Section | Row actions |
|---|---|
| Staged | Unstage, Diff |
| Unstaged / Untracked | Stage, Revert (confirm), Diff |
| Conflicted | Diff only, with an explanatory note — conflict resolution is out of scope |

Section headers carry a count and a "stage all in section" action that passes the explicit file list.
Keep the numeric summary as a compact strip so `git-staged-count` / `git-modified-count` /
`git-untracked-count` testids survive — with values that are finally correct.

### 3.3 Diff pane

Selecting Diff opens a pane inside the drawer showing original vs current. Reuse the read-only
CodeMirror viewer and `--cm-*` highlight tokens from
[api-client-ux-overhaul](../api-client-ux-overhaul/technical-plan.md) Module 3, with a
unified/side-by-side toggle. Binary files show a "binary file" placeholder. Very large diffs follow
the same size-threshold approach as the response viewer rather than inventing a second policy.

### 3.4 Remote compare link

Pure TypeScript in `web/src/lib/git-remote.ts`:

```ts
export function inferCompareUrl(remoteUrl: string, branch: string): string | null
```

Handles GitHub (`https://github.com/o/r/compare/<branch>`) and Azure DevOps
(`https://dev.azure.com/org/project/_git/repo/branchCompare?baseVersion=GB…`) for both HTTPS and SSH
remote forms, returning `null` for anything unrecognized so no broken link is ever rendered. Opened
via the existing `@tauri-apps/plugin-shell` opener, never `window.open`.

### 3.5 Feedback

Route success and failure through the app's existing notification system rather than the panel-local
`successMsg` / `error` strings, matching the convention
[react-polish-aug-01](../react-polish-aug-01/index.md) establishes for mutation feedback. Keep the
inline `git-error` region for errors that need to stay pinned next to the action that caused them.

---

## Module 4 — Bridge layer

`web/src/lib/tauri-bridge.ts`: add wrappers for the Module 2 commands following the existing shape,
with two changes to current behaviour:

- `gitStatus` throws a typed `GitUnavailableError` / `GitCommandError` instead of returning `null`
  (needed by 1.4).
- `gitStageAll` is removed along with its command.

Consider moving git calls to React Query hooks in `web/src/lib/hooks.ts` for cache invalidation, so a
stage/unstage/commit refreshes status and the file list without the manual `refresh()` chain the panel
uses today. Status is cheap and repo state changes outside the app, so keep `staleTime` at 0 and
invalidate explicitly after every mutation.

---

## Module 5 — Documentation

[docs/architecture/functionalities/api-client.md](../../../architecture/functionalities/api-client.md)
describes the Blazor implementation throughout. This feature owns correcting the **Git and linked-root
sections**:

- **What Is Supported** — rewrite the "Git-linked API repositories", "Safe linked Git actions", and
  "Workflow trust UI" bullets to describe what React actually does after this feature.
- **Current Deferrals** — add linked `.swebkit-api` collection roots, linked-file conflict detection,
  rebase/stash/merge, and conflict resolution. Remove `pull`, which is implemented.
- **Git Action Path** — rewrite around the Tauri commands, replacing the `LinkedGitService` steps.
- **State Persistence** — replace the linked-root rows with the persisted repository path/subpath and
  the Rust-side granted-roots list.
- **Security and Safety Notes** — state that git commands are `AllowedRoots`-gated and that staging
  and committing are scoped to the configured API subpath.

[api-client-ux-overhaul](../api-client-ux-overhaul/index.md) Module 6 rewrites the component-graph and
response-rendering sections of the same file. Land that feature first so this pass edits a base that
is already accurate elsewhere.

---

## Implementation order

1. **Module 1.1** (parser fix) with its Rust unit tests — smallest change, largest correctness win,
   and everything else depends on trustworthy status.
2. **Module 1.7 + 1.2** (`AllowedRoots` gate, repo selection and persistence) — the security and
   provenance foundation before new commands are added.
3. **Module 1.3 + 1.4** (delete the fakes, honest errors) — pure subtraction, immediately reduces
   misleading UI.
4. **Module 2** commands, one group at a time: changed files → stage/unstage/revert → diff → branch.
5. **Module 3** UI, following Module 2's groups.
6. **Module 1.5** (API-subpath scoping) once the file list exists to filter against.
7. **Module 1.6** (drawer) — independent, can land any time after the UX feature's persistence helper.
8. **Module 5** (docs) last.

Steps 1–3 are worth shipping on their own even if the rest is deferred: they turn a panel that lies
into a panel that is merely limited.
