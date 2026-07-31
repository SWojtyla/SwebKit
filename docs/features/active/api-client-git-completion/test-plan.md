# Test Plan — API Client Git Completion

## Testing infrastructure — a gap this feature has to close

`src-tauri` currently has **no tests**: no `#[cfg(test)]` module anywhere in
`src-tauri/src`, and no `src-tauri/tests/` directory. The crate already has a lib target
(`src-tauri/src/lib.rs`), so `cargo test` works without new tooling — only test modules are missing.

The porcelain-v2 parser is exactly the kind of code that must not ship untested: it is pure, it has
many input shapes, and it is already wrong once in a way nobody noticed because the counters looked
plausible. Introducing `#[cfg(test)]` in `native.rs` is a required part of this feature.

Frontend units (`inferCompareUrl`) go to Vitest, which
[api-client-ux-overhaul](../api-client-ux-overhaul/test-plan.md) introduces to `web/`. If this feature
somehow lands first, it carries the Vitest setup instead.

---

## Rust unit tests — `parse_porcelain_v2`

`src-tauri/src/native.rs`, `#[cfg(test)] mod tests`. Inputs are literal porcelain-v2 strings captured
from real `git status --porcelain=v2 --branch` output — no repository fixtures, no process spawning.

### Branch header parsing

| Input line | Expectation |
|---|---|
| `# branch.head main` | `branch == "main"` |
| `# branch.head (detached)` | `branch == "(detached)"`, no panic |
| `# branch.ab +3 -2` | `ahead == 3`, `behind == 2` |
| `# branch.ab +0 -0` | both `0` |
| No `branch.ab` line (no upstream) | both `0`, no panic |
| Missing `branch.head` (empty repo) | `branch` empty, no panic |

### File state parsing — the D2 regression tests

| Input | Expectation |
|---|---|
| `1 M. N... 100644 100644 100644 <h> <h> src/a.json` | staged 1, modified 0 — **staged only** |
| `1 .M N... 100644 100644 100644 <h> <h> src/a.json` | staged 0, modified 1 — **this is the case today's code gets wrong** |
| `1 MM N... …` | staged 1 **and** modified 1 — counted once in each, which is what Git means |
| `1 A. N... …` | staged 1, modified 0 (newly added) |
| `1 .D N... …` | staged 0, modified 1 (deleted in worktree) |
| `1 D. N... …` | staged 1 (deletion staged) |
| `2 R. N... 100644 100644 100644 <h> <h> R100 new.json\told.json` | staged 1; `path == "new.json"`, `orig_path == Some("old.json")` |
| `2 .R N... …` | unstaged rename |
| `u UU N... …` | `conflicted == 1`; **not** counted as modified, unlike today |
| `? untracked.json` | untracked 1 |
| `! ignored.json` | ignored — counted in none of staged/modified/untracked |
| Empty string | All zeros, empty file list, no panic |
| Mixed realistic output (all of the above together) | Each counter exactly right; file list length matches |
| A path containing a space | Path preserved intact — regression guard against `split_whitespace` on the path field |
| Renamed entry with a tab-separated path pair | Split on tab, not whitespace |

### `GitFileChange` classification

| Input | Expectation |
|---|---|
| `1 M. …` | `staged: true`, `unstaged: false` |
| `1 .M …` | `staged: false`, `unstaged: true` |
| `1 MM …` | both `true` |
| `? f.json` | `untracked: true`, `staged: false` |
| `u UU …` | `conflicted: true` |

### Subpath filtering

| Repo files | `subpath` | Expectation |
|---|---|---|
| `api/a.json`, `src/b.cs` | `Some("api")` | Only `api/a.json` returned |
| Same | `None` | Both returned |
| `api/nested/deep/c.json` | `Some("api")` | Included — filtering is prefix-based on the repo-relative path |
| `apixyz/d.json` | `Some("api")` | **Excluded** — prefix match must be on a path segment boundary, not a raw string prefix |
| Paths using `\` on Windows | `Some("api")` | Normalized before comparison; Git reports `/` but the config may hold `\` |

### Branch name validation

`git_create_branch` delegates to `git check-ref-format --branch`, so tests assert the delegation and
error propagation rather than reimplementing the rules: a valid name succeeds, a name with a space or
`..` surfaces Git's own error message rather than a generic failure.

---

## Rust integration tests — command behaviour

`src-tauri/tests/git_commands.rs`, each test creating a temporary repository with `git init`, a
committed baseline, and known dirty state. Skipped with a clear message when `git` is absent rather
than failing.

| Scenario | Assertion |
|---|---|
| `git_is_repo` on a repo / on a plain directory | `true` / `false` |
| `git_changed_files` on a mixed dirty repo | Matches the parser's expectations end to end |
| `git_stage_paths` with one of three modified files | Only that file becomes staged |
| `git_stage_paths` with a path containing a space | Staged correctly — argument passing, not shell quoting |
| `git_stage_paths` with a path starting `-` | Treated as a path, not a flag — proves the `--` separator |
| `git_unstage_paths` on a staged file | Returns to unstaged, worktree content untouched |
| `git_revert_paths` on a modified file | Worktree content back to `HEAD` |
| `git_revert_paths` on an untracked file | Rejected with a clear error — `git restore` cannot revert an untracked file |
| `git_diff_file` on a modified tracked file | `original` is the `HEAD` content, `current` the worktree content |
| `git_diff_file` on a new untracked file | `original: None`, `current` populated |
| `git_diff_file` on a binary file | `is_binary: true`, no garbage text |
| `git_checkout_branch` to an existing branch | `git_status().branch` reflects it |
| `git_checkout_branch` that would lose changes | Fails, and Git's message is surfaced |
| `git_create_branch(checkout: true)` | Branch exists and is `HEAD` |
| `git_create_branch` with an existing name | Fails cleanly |
| `git_remote_url` with no remote | `None`, no error |
| **Every command with a path outside `AllowedRoots`** | Rejected with "outside the allowed workspace" — the D7 gate, asserted per command, not once |
| Commit with staged files outside `apiSubpath` | Refused, offending files listed (1.5's guard) |

---

## Frontend unit tests (Vitest)

### `web/src/lib/git-remote.test.ts`

| Remote | Branch | Expectation |
|---|---|---|
| `https://github.com/org/repo.git` | `feature/x` | `https://github.com/org/repo/compare/feature/x` |
| `git@github.com:org/repo.git` | `main` | Same host/org/repo as the HTTPS form |
| `https://github.com/org/repo` (no `.git`) | `main` | Correct — no doubled or missing segment |
| `https://dev.azure.com/org/project/_git/repo` | `main` | Azure `branchCompare` URL with `GBmain` |
| `https://org@dev.azure.com/org/project/_git/repo` | `main` | Embedded credential stripped from the output |
| `git@ssh.dev.azure.com:v3/org/project/repo` | `main` | Correct Azure URL |
| `https://org.visualstudio.com/project/_git/repo` | `main` | Legacy Azure form handled |
| `https://gitlab.com/org/repo.git` | `main` | `null` — unrecognized, no guessed link |
| `""` / malformed | any | `null`, no throw |
| Branch name needing encoding (`feature/a b`) | | Percent-encoded in the URL |

### `web/src/lib/git-status-format.test.ts`

| Case | Expectation |
|---|---|
| `index_state: "M", worktree_state: "."` | Label "Modified", staged section |
| `"." / "M"` | "Modified", unstaged section |
| `"M" / "M"` | Appears in **both** sections |
| `"R"` with `orig_path` | Renders `old → new` |
| `untracked` | "New", unstaged section, Revert action unavailable |
| `conflicted` | "Conflicted", only Diff offered |

---

## E2E tests (Playwright)

The panel's current coverage is two tests in
[web/e2e/git-notifications.spec.ts](../../../../web/e2e/git-notifications.spec.ts), and both only
assert the browser-mode "unavailable" message — no git behaviour is covered today.

**Constraint:** git commands are Tauri commands, unavailable in the Playwright browser context, so
end-to-end git *behaviour* cannot be covered by the existing harness. Playwright covers the states
reachable without Tauri; behaviour is covered by the Rust integration tests above plus manual desktop
verification. This limit is stated here rather than left implicit — no test in this section should be
read as proving a git operation works.

| Scenario | Assertion |
|---|---|
| Existing `git toggle button is visible` | Passes unchanged |
| Existing `git panel opens and closes` | Passes; `git-panel-unavailable` still shown in browser mode |
| Browser mode message | Reads "needs the SwebKit desktop app" — not the old blanket message shown for every failure |
| No Bruno buttons | `git-reimport-bruno` and `git-export-bruno` resolve to zero elements — the D3 removal guard |
| Drawer chrome | Backdrop present; `Escape` closes; focus returns to `api-client-git-toggle`; `role="dialog"` with an accessible name |
| Drawer does not cover the status bar | The app status bar remains visible and hit-testable with the drawer open |
| Drawer width | Draggable and persisted across reload |
| No repo selected | First-run state with a **Choose repository** action; no implicit `"."` anywhere in the UI |

---

## Manual verification (desktop, required)

Everything below needs `npm --prefix web run tauri dev` and a real repository containing API
collection files. Steps 3 and 4 are the acceptance checks for the two defects most likely to have
caused silent damage.

1. **Repo selection & persistence.** Pick a repository, restart the app, confirm it is still selected
   and status loads without re-prompting. Delete the folder, restart, confirm a clear message and a
   return to the picker — not a raw Git error.
2. **Counter correctness (D2).** Create one staged-only, one unstaged-only, and one staged-and-modified
   file plus an untracked file. Compare the panel's numbers against `git status --short`. Today's build
   reports every file as staged and zero modified; the new build must match Git exactly.
3. **Scoped staging (D5).** With unrelated uncommitted work outside the API subpath, use "Stage all
   changed API files" and commit. Verify with `git show --stat` that the unrelated work was **not**
   committed. This is the check that today's build fails.
4. **Revert safety (DEC-G6).** Modify a file, revert it, confirm the dialog names the file and the
   worktree returns to `HEAD`. Confirm cancelling changes nothing.
5. **Branch switch and create.** Switch branches, confirm the header and file list refresh. Create a
   branch and confirm checkout. Attempt a switch that would lose changes and confirm Git's own message
   is shown.
6. **Diff.** Open a diff on a modified JSON file, a new file, and a binary file. Confirm highlighting
   matches the response viewer and that the binary case shows a placeholder.
7. **Error paths (D4).** Point at a non-repo directory; temporarily remove `git` from `PATH`. Each must
   produce its own specific message, never the generic desktop-app text.
8. **Security gate (D7).** With no directory ever picked in the session, confirm no git command
   succeeds against an arbitrary path.
9. **Compare link.** On a GitHub remote and an Azure DevOps remote, confirm the link opens the right
   compare view in the system browser. On a GitLab remote, confirm no link is offered.
