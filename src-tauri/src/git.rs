//! Git operations for the API Client panel.
//!
//! Every command shells out to the `git` binary with a fixed argument array. That
//! is deliberate: it inherits the user's credential helpers, SSH agent, gitconfig
//! and proxy settings, all of which matter for push/pull against GitHub and Azure
//! DevOps and none of which libgit2 would provide for free. See
//! `docs/features/active/api-client-git-completion/decisions.md` DEC-G1.
//!
//! Paths are always passed after `--` so a filename can never be parsed as a flag,
//! and every command validates its repository directory against `AllowedRoots`
//! before running.

use std::path::{Path, PathBuf};
use tauri::State;

use crate::native::{hidden_command, validate_dir_within_roots, AllowedRoots};

// ── Types ────────────────────────────────────────────────────────────────────

#[derive(serde::Serialize, Debug, Default, PartialEq)]
pub struct GitStatus {
    pub branch: String,
    pub ahead: u32,
    pub behind: u32,
    pub staged: u32,
    pub modified: u32,
    pub untracked: u32,
    pub conflicted: u32,
}

#[derive(serde::Serialize, Debug)]
pub struct GitBranch {
    pub name: String,
    pub current: bool,
}

// Tauri does not camelCase struct fields automatically, so the wire shape is
// declared explicitly to match the TypeScript interfaces in `tauri-bridge.ts`.
#[derive(serde::Serialize, Debug, Clone, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct GitFileChange {
    /// Repository-relative path, always `/`-separated as git reports it.
    pub path: String,
    /// Index (staged) state letter; `.` means unchanged in the index.
    pub index_state: String,
    /// Worktree state letter; `.` means unchanged in the worktree.
    pub worktree_state: String,
    pub staged: bool,
    pub unstaged: bool,
    pub untracked: bool,
    pub conflicted: bool,
    /// Rename/copy source, when git reported one.
    pub orig_path: Option<String>,
}

#[derive(serde::Serialize, Debug)]
#[serde(rename_all = "camelCase")]
pub struct GitFileDiff {
    /// Committed content, or `None` when the file is new.
    pub original: Option<String>,
    pub current: String,
    pub is_binary: bool,
}

// ── Porcelain v2 parsing ─────────────────────────────────────────────────────

/// Result of parsing `git status --porcelain=v2 --branch`.
#[derive(Debug, Default, PartialEq)]
pub struct ParsedStatus {
    pub status: GitStatus,
    pub files: Vec<GitFileChange>,
}

fn unchanged(state: &str) -> bool {
    state == "."
}

fn split_state(xy: &str) -> (String, String) {
    // XY is always exactly two characters: X is the index state, Y the worktree
    // state. Comparing the whole field against "." (as the original code did) can
    // never match, which is why every changed file was counted as staged and
    // `modified` was always zero.
    let mut chars = xy.chars();
    let x = chars.next().unwrap_or('.');
    let y = chars.next().unwrap_or('.');
    (x.to_string(), y.to_string())
}

fn change_from_states(path: String, xy: &str, orig_path: Option<String>) -> GitFileChange {
    let (index_state, worktree_state) = split_state(xy);
    GitFileChange {
        path,
        staged: !unchanged(&index_state),
        unstaged: !unchanged(&worktree_state),
        untracked: false,
        conflicted: false,
        index_state,
        worktree_state,
        orig_path,
    }
}

/// Parses porcelain v2 output into counters and a per-file list.
///
/// Pure by design so every status shape can be covered by unit tests without a
/// repository — this parse has been silently wrong once already.
pub fn parse_porcelain_v2(text: &str) -> ParsedStatus {
    let mut parsed = ParsedStatus::default();

    for line in text.lines() {
        if line.is_empty() {
            continue;
        }

        if let Some(rest) = line.strip_prefix("# branch.head ") {
            parsed.status.branch = rest.to_string();
            continue;
        }

        if let Some(rest) = line.strip_prefix("# branch.ab ") {
            let mut parts = rest.split_whitespace();
            if let Some(a) = parts.next() {
                parsed.status.ahead = a.trim_start_matches('+').parse().unwrap_or(0);
            }
            if let Some(b) = parts.next() {
                parsed.status.behind = b.trim_start_matches('-').parse().unwrap_or(0);
            }
            continue;
        }

        // Other `# branch.*` headers (oid, upstream) carry nothing we surface.
        if line.starts_with('#') {
            continue;
        }

        // Ordinary change: 1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>
        // `splitn` rather than `split_whitespace` because the path may contain spaces.
        if let Some(rest) = line.strip_prefix("1 ") {
            let parts: Vec<&str> = rest.splitn(8, ' ').collect();
            if parts.len() < 8 {
                continue;
            }
            parsed
                .files
                .push(change_from_states(parts[7].to_string(), parts[0], None));
            continue;
        }

        // Rename/copy: 2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <Xscore> <path>\t<origPath>
        if let Some(rest) = line.strip_prefix("2 ") {
            let parts: Vec<&str> = rest.splitn(9, ' ').collect();
            if parts.len() < 9 {
                continue;
            }
            // The new and original paths are tab-separated, not space-separated.
            let (path, orig) = match parts[8].split_once('\t') {
                Some((p, o)) => (p.to_string(), Some(o.to_string())),
                None => (parts[8].to_string(), None),
            };
            parsed.files.push(change_from_states(path, parts[0], orig));
            continue;
        }

        // Unmerged: u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path>
        if let Some(rest) = line.strip_prefix("u ") {
            let parts: Vec<&str> = rest.splitn(10, ' ').collect();
            if parts.len() < 10 {
                continue;
            }
            let (index_state, worktree_state) = split_state(parts[0]);
            parsed.files.push(GitFileChange {
                path: parts[9].to_string(),
                index_state,
                worktree_state,
                staged: false,
                unstaged: false,
                untracked: false,
                conflicted: true,
                orig_path: None,
            });
            continue;
        }

        if let Some(rest) = line.strip_prefix("? ") {
            parsed.files.push(GitFileChange {
                path: rest.to_string(),
                index_state: ".".to_string(),
                worktree_state: "?".to_string(),
                staged: false,
                unstaged: true,
                untracked: true,
                conflicted: false,
                orig_path: None,
            });
            continue;
        }

        // `! <path>` — ignored files. Deliberately not counted anywhere; listed
        // here so the omission is explicit rather than accidental.
        if line.starts_with("! ") {
            continue;
        }
    }

    for file in &parsed.files {
        if file.conflicted {
            parsed.status.conflicted += 1;
        } else if file.untracked {
            parsed.status.untracked += 1;
        } else {
            // A file changed in both the index and the worktree counts once in
            // each — that is what git means by the two state letters.
            if file.staged {
                parsed.status.staged += 1;
            }
            if file.unstaged {
                parsed.status.modified += 1;
            }
        }
    }

    parsed
}

/// Normalizes a configured subpath for comparison against git's `/`-separated paths.
fn normalize_subpath(subpath: &str) -> String {
    subpath
        .replace('\\', "/")
        .trim_matches('/')
        .to_string()
}

/// True when `path` is inside `subpath`, matching on a segment boundary so
/// `apixyz/a.json` is not treated as being under `api`.
pub fn is_within_subpath(path: &str, subpath: &Option<String>) -> bool {
    let Some(subpath) = subpath else {
        return true;
    };
    let prefix = normalize_subpath(subpath);
    if prefix.is_empty() {
        return true;
    }
    let normalized = path.replace('\\', "/");
    normalized == prefix || normalized.starts_with(&format!("{prefix}/"))
}

// ── Command helpers ──────────────────────────────────────────────────────────

fn run_git(repo: &Path, args: &[&str]) -> Result<String, String> {
    let output = hidden_command("git")
        .args(args)
        .current_dir(repo)
        .output()
        .map_err(|e| {
            if e.kind() == std::io::ErrorKind::NotFound {
                "git was not found on this system".to_string()
            } else {
                format!("Failed to run git: {e}")
            }
        })?;

    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr).trim().to_string();
        return Err(if stderr.is_empty() {
            format!("git {} failed", args.join(" "))
        } else {
            stderr
        });
    }

    Ok(String::from_utf8_lossy(&output.stdout).to_string())
}

fn repo_dir(path: &str, roots: &AllowedRoots) -> Result<PathBuf, String> {
    validate_dir_within_roots(path, roots)
}

/// Rejects paths that are not among the repository's currently reported changes.
///
/// Double validation: the UI confirms by name, and the command re-checks against
/// live status, so a stale frontend list cannot act on a file the user never saw.
fn validate_reported_paths(
    repo: &Path,
    paths: &[String],
    allow_untracked: bool,
) -> Result<(), String> {
    if paths.is_empty() {
        return Err("No files specified".to_string());
    }

    let text = run_git(
        repo,
        &["-c", "core.quotePath=false", "status", "--porcelain=v2", "--branch"],
    )?;
    let parsed = parse_porcelain_v2(&text);

    for path in paths {
        let normalized = path.replace('\\', "/");
        let found = parsed.files.iter().find(|f| f.path == normalized);
        match found {
            None => return Err(format!("{path} is not a reported change in this repository")),
            Some(file) if file.untracked && !allow_untracked => {
                return Err(format!(
                    "{path} is untracked — there is no committed version to restore"
                ))
            }
            Some(_) => {}
        }
    }

    Ok(())
}

// -- Command implementations -------------------------------------------------
//
// The `#[tauri::command]` wrappers below are one-liners over these plain
// functions. Taking `&AllowedRoots` instead of `State<'_, AllowedRoots>` is what
// makes the behaviour -- including the security gate -- reachable from unit tests
// without standing up a Tauri app.

pub fn is_repo_impl(path: &str, roots: &AllowedRoots) -> Result<bool, String> {
    let Ok(repo) = repo_dir(path, roots) else {
        return Ok(false);
    };
    match run_git(&repo, &["rev-parse", "--is-inside-work-tree"]) {
        Ok(out) => Ok(out.trim() == "true"),
        Err(_) => Ok(false),
    }
}

fn read_status(repo: &Path) -> Result<ParsedStatus, String> {
    let text = run_git(
        repo,
        &["-c", "core.quotePath=false", "status", "--porcelain=v2", "--branch"],
    )?;
    Ok(parse_porcelain_v2(&text))
}

pub fn status_impl(path: &str, roots: &AllowedRoots) -> Result<GitStatus, String> {
    let repo = repo_dir(path, roots)?;
    Ok(read_status(&repo)?.status)
}

pub fn changed_files_impl(
    path: &str,
    subpath: Option<String>,
    roots: &AllowedRoots,
) -> Result<Vec<GitFileChange>, String> {
    let repo = repo_dir(path, roots)?;
    Ok(read_status(&repo)?
        .files
        .into_iter()
        .filter(|f| is_within_subpath(&f.path, &subpath))
        .collect())
}

pub fn branches_impl(path: &str, roots: &AllowedRoots) -> Result<Vec<GitBranch>, String> {
    let repo = repo_dir(path, roots)?;
    let text = run_git(
        &repo,
        &["branch", "--list", "--format=%(HEAD) %(refname:short)"],
    )?;

    Ok(text
        .lines()
        .filter(|l| !l.trim().is_empty())
        .map(|l| {
            let current = l.starts_with('*');
            let name = l.trim_start_matches('*').trim().to_string();
            GitBranch { name, current }
        })
        .collect())
}

/// Builds `[<subcommand>..., "--", <paths>]` so a filename starting with `-` can
/// never be parsed as a flag.
fn run_git_with_paths(repo: &Path, leading: &[&str], paths: &[String]) -> Result<(), String> {
    let mut args: Vec<&str> = leading.to_vec();
    args.push("--");
    args.extend(paths.iter().map(|p| p.as_str()));
    run_git(repo, &args)?;
    Ok(())
}

pub fn stage_paths_impl(path: &str, paths: &[String], roots: &AllowedRoots) -> Result<(), String> {
    let repo = repo_dir(path, roots)?;
    validate_reported_paths(&repo, paths, true)?;
    run_git_with_paths(&repo, &["add"], paths)
}

pub fn unstage_paths_impl(path: &str, paths: &[String], roots: &AllowedRoots) -> Result<(), String> {
    let repo = repo_dir(path, roots)?;
    validate_reported_paths(&repo, paths, true)?;
    run_git_with_paths(&repo, &["restore", "--staged"], paths)
}

/// Discards worktree changes. The only irreversible operation here, so it
/// re-validates against live status and refuses untracked files, which have no
/// committed version to restore (DEC-G6).
pub fn revert_paths_impl(path: &str, paths: &[String], roots: &AllowedRoots) -> Result<(), String> {
    let repo = repo_dir(path, roots)?;
    validate_reported_paths(&repo, paths, false)?;
    run_git_with_paths(&repo, &["restore", "--worktree"], paths)
}

pub fn diff_file_impl(path: &str, file: &str, roots: &AllowedRoots) -> Result<GitFileDiff, String> {
    let repo = repo_dir(path, roots)?;

    // A missing HEAD version means the file is new, not that anything failed.
    let original = run_git(&repo, &["show", &format!("HEAD:{file}")]).ok();

    // A file with staged changes has an index version that can differ from the
    // working tree — further edits made *after* staging, still unstaged. Always
    // diffing against the raw working-tree file (as this used to) folds those
    // extra edits into what looks like "the staged diff", silently showing more
    // than a commit made right now would actually contain. Prefer the index blob
    // whenever the file has any staged change; a purely unstaged or untracked
    // file has nothing staged to differ from, so it still compares HEAD against
    // the raw working-tree bytes exactly as before.
    let normalized_file = file.replace('\\', "/");
    let staged = read_status(&repo)
        .map(|s| s.files.iter().any(|f| f.path == normalized_file && f.staged))
        .unwrap_or(false);

    let current_bytes = if staged {
        // ":<path>" is git's index (stage 0) blob syntax — the staged content,
        // independent of any further unstaged edit sitting on top of it in the
        // working tree. Falls back to the raw file if the index read fails for
        // any reason, so a diff is still shown rather than none at all.
        run_git(&repo, &["show", &format!(":{file}")])
            .map(String::into_bytes)
            .unwrap_or_else(|_| std::fs::read(repo.join(file)).unwrap_or_default())
    } else {
        std::fs::read(repo.join(file)).unwrap_or_default()
    };
    // A NUL byte is the same heuristic git itself uses, and unlike `--numstat` it
    // also works for untracked files.
    let is_binary =
        current_bytes.contains(&0) || original.as_ref().is_some_and(|o| o.contains('\0'));

    let current = if is_binary {
        String::new()
    } else {
        String::from_utf8_lossy(&current_bytes).to_string()
    };

    Ok(GitFileDiff {
        original: if is_binary { None } else { original },
        current,
        is_binary,
    })
}

pub fn commit_impl(
    path: &str,
    message: &str,
    subpath: Option<String>,
    roots: &AllowedRoots,
) -> Result<(), String> {
    let repo = repo_dir(path, roots)?;

    if message.trim().is_empty() {
        return Err("Commit message is empty".to_string());
    }

    // Refuse to commit when files outside the configured API subpath are staged.
    // Committing unrelated work in progress is the failure mode `git add --all`
    // used to cause; the guard is what makes the commit preview trustworthy.
    if subpath.is_some() {
        let outside: Vec<String> = read_status(&repo)?
            .files
            .into_iter()
            .filter(|f| f.staged && !is_within_subpath(&f.path, &subpath))
            .map(|f| f.path)
            .collect();

        if !outside.is_empty() {
            return Err(format!(
                "Refusing to commit: {} staged file(s) are outside the API path - {}",
                outside.len(),
                outside.join(", ")
            ));
        }
    }

    run_git(&repo, &["commit", "-m", message])?;
    Ok(())
}

pub fn checkout_branch_impl(path: &str, branch: &str, roots: &AllowedRoots) -> Result<(), String> {
    let repo = repo_dir(path, roots)?;
    run_git(&repo, &["checkout", branch])?;
    Ok(())
}

pub fn create_branch_impl(
    path: &str,
    branch: &str,
    checkout: bool,
    roots: &AllowedRoots,
) -> Result<(), String> {
    let repo = repo_dir(path, roots)?;

    // Delegate name validation to git so the rules are git's, not a hand-rolled
    // regex that will drift from them.
    run_git(&repo, &["check-ref-format", "--branch", branch])
        .map_err(|_| format!("\"{branch}\" is not a valid branch name"))?;

    if checkout {
        run_git(&repo, &["checkout", "-b", branch])?;
    } else {
        run_git(&repo, &["branch", branch])?;
    }
    Ok(())
}

pub fn remote_url_impl(path: &str, roots: &AllowedRoots) -> Result<Option<String>, String> {
    let repo = repo_dir(path, roots)?;
    // No remote is a normal state, not an error.
    Ok(run_git(&repo, &["remote", "get-url", "origin"])
        .ok()
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty()))
}

// -- Tauri commands ----------------------------------------------------------

#[tauri::command]
pub async fn git_is_repo(path: String, roots: State<'_, AllowedRoots>) -> Result<bool, String> {
    is_repo_impl(&path, &roots)
}

#[tauri::command]
pub async fn git_status(path: String, roots: State<'_, AllowedRoots>) -> Result<GitStatus, String> {
    status_impl(&path, &roots)
}

#[tauri::command]
pub async fn git_changed_files(
    path: String,
    subpath: Option<String>,
    roots: State<'_, AllowedRoots>,
) -> Result<Vec<GitFileChange>, String> {
    changed_files_impl(&path, subpath, &roots)
}

#[tauri::command]
pub async fn git_branches(
    path: String,
    roots: State<'_, AllowedRoots>,
) -> Result<Vec<GitBranch>, String> {
    branches_impl(&path, &roots)
}

#[tauri::command]
pub async fn git_stage_paths(
    path: String,
    paths: Vec<String>,
    roots: State<'_, AllowedRoots>,
) -> Result<(), String> {
    stage_paths_impl(&path, &paths, &roots)
}

#[tauri::command]
pub async fn git_unstage_paths(
    path: String,
    paths: Vec<String>,
    roots: State<'_, AllowedRoots>,
) -> Result<(), String> {
    unstage_paths_impl(&path, &paths, &roots)
}

#[tauri::command]
pub async fn git_revert_paths(
    path: String,
    paths: Vec<String>,
    roots: State<'_, AllowedRoots>,
) -> Result<(), String> {
    revert_paths_impl(&path, &paths, &roots)
}

#[tauri::command]
pub async fn git_diff_file(
    path: String,
    file: String,
    roots: State<'_, AllowedRoots>,
) -> Result<GitFileDiff, String> {
    diff_file_impl(&path, &file, &roots)
}

#[tauri::command]
pub async fn git_commit(
    path: String,
    message: String,
    subpath: Option<String>,
    roots: State<'_, AllowedRoots>,
) -> Result<(), String> {
    commit_impl(&path, &message, subpath, &roots)
}

#[tauri::command]
pub async fn git_push(path: String, roots: State<'_, AllowedRoots>) -> Result<String, String> {
    let repo = repo_dir(&path, &roots)?;
    run_git(&repo, &["push"])
}

#[tauri::command]
pub async fn git_pull(path: String, roots: State<'_, AllowedRoots>) -> Result<String, String> {
    let repo = repo_dir(&path, &roots)?;
    run_git(&repo, &["pull"])
}

#[tauri::command]
pub async fn git_checkout_branch(
    path: String,
    branch: String,
    roots: State<'_, AllowedRoots>,
) -> Result<(), String> {
    checkout_branch_impl(&path, &branch, &roots)
}

#[tauri::command]
pub async fn git_create_branch(
    path: String,
    branch: String,
    checkout: bool,
    roots: State<'_, AllowedRoots>,
) -> Result<(), String> {
    create_branch_impl(&path, &branch, checkout, &roots)
}

#[tauri::command]
pub async fn git_remote_url(
    path: String,
    roots: State<'_, AllowedRoots>,
) -> Result<Option<String>, String> {
    remote_url_impl(&path, &roots)
}
// ── Tests ────────────────────────────────────────────────────────────────────

// Both modules live in sibling files via #[path] — keeps this file to production code
// without changing module resolution (`super::*` inside the tests still sees git.rs).
#[cfg(test)]
#[path = "git_tests.rs"]
mod tests;


// -- Integration tests over real temporary repositories ----------------------
//
// These exercise the `*_impl` functions against an actual `git` binary, covering
// what the pure parser cannot: argument passing, the `AllowedRoots` gate, and the
// destructive-operation guards. Skipped with a clear message when git is absent.

#[cfg(test)]
#[path = "git_repo_tests.rs"]
mod repo_tests;
