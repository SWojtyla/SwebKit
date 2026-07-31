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

    let current_bytes = std::fs::read(repo.join(file)).unwrap_or_default();
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

#[cfg(test)]
mod tests {
    use super::*;

    const H: &str = "0000000000000000000000000000000000000000";

    fn ordinary(xy: &str, path: &str) -> String {
        format!("1 {xy} N... 100644 100644 100644 {H} {H} {path}")
    }

    #[test]
    fn parses_branch_head() {
        let parsed = parse_porcelain_v2("# branch.head main\n");
        assert_eq!(parsed.status.branch, "main");
    }

    #[test]
    fn parses_detached_head() {
        let parsed = parse_porcelain_v2("# branch.head (detached)\n");
        assert_eq!(parsed.status.branch, "(detached)");
    }

    #[test]
    fn parses_ahead_behind() {
        let parsed = parse_porcelain_v2("# branch.ab +3 -2\n");
        assert_eq!(parsed.status.ahead, 3);
        assert_eq!(parsed.status.behind, 2);
    }

    #[test]
    fn zero_ahead_behind() {
        let parsed = parse_porcelain_v2("# branch.ab +0 -0\n");
        assert_eq!(parsed.status.ahead, 0);
        assert_eq!(parsed.status.behind, 0);
    }

    #[test]
    fn missing_upstream_leaves_counts_zero() {
        let parsed = parse_porcelain_v2("# branch.oid abc\n# branch.head main\n");
        assert_eq!(parsed.status.ahead, 0);
        assert_eq!(parsed.status.behind, 0);
    }

    #[test]
    fn empty_input_is_all_zero() {
        let parsed = parse_porcelain_v2("");
        assert_eq!(parsed.status, GitStatus::default());
        assert!(parsed.files.is_empty());
    }

    #[test]
    fn staged_only_counts_as_staged() {
        let parsed = parse_porcelain_v2(&ordinary("M.", "src/a.json"));
        assert_eq!(parsed.status.staged, 1);
        assert_eq!(parsed.status.modified, 0);
    }

    /// The regression the original parser got wrong: it compared the two-char XY
    /// field against "." and so counted every change as staged.
    #[test]
    fn unstaged_only_counts_as_modified() {
        let parsed = parse_porcelain_v2(&ordinary(".M", "src/a.json"));
        assert_eq!(parsed.status.staged, 0);
        assert_eq!(parsed.status.modified, 1);
    }

    #[test]
    fn staged_and_modified_counts_in_both() {
        let parsed = parse_porcelain_v2(&ordinary("MM", "src/a.json"));
        assert_eq!(parsed.status.staged, 1);
        assert_eq!(parsed.status.modified, 1);
        assert!(parsed.files[0].staged);
        assert!(parsed.files[0].unstaged);
    }

    #[test]
    fn added_file_is_staged() {
        let parsed = parse_porcelain_v2(&ordinary("A.", "src/new.json"));
        assert_eq!(parsed.status.staged, 1);
        assert_eq!(parsed.status.modified, 0);
    }

    #[test]
    fn worktree_deletion_is_modified() {
        let parsed = parse_porcelain_v2(&ordinary(".D", "src/gone.json"));
        assert_eq!(parsed.status.staged, 0);
        assert_eq!(parsed.status.modified, 1);
    }

    #[test]
    fn staged_deletion_is_staged() {
        let parsed = parse_porcelain_v2(&ordinary("D.", "src/gone.json"));
        assert_eq!(parsed.status.staged, 1);
    }

    #[test]
    fn parses_rename_with_orig_path() {
        let line = format!("2 R. N... 100644 100644 100644 {H} {H} R100 new.json\told.json");
        let parsed = parse_porcelain_v2(&line);
        assert_eq!(parsed.status.staged, 1);
        assert_eq!(parsed.files[0].path, "new.json");
        assert_eq!(parsed.files[0].orig_path.as_deref(), Some("old.json"));
    }

    #[test]
    fn parses_unstaged_rename() {
        let line = format!("2 .R N... 100644 100644 100644 {H} {H} R100 new.json\told.json");
        let parsed = parse_porcelain_v2(&line);
        assert_eq!(parsed.status.staged, 0);
        assert_eq!(parsed.status.modified, 1);
    }

    #[test]
    fn unmerged_is_conflicted_not_modified() {
        let line = format!("u UU N... 100644 100644 100644 100644 {H} {H} {H} conflict.json");
        let parsed = parse_porcelain_v2(&line);
        assert_eq!(parsed.status.conflicted, 1);
        assert_eq!(parsed.status.modified, 0);
        assert_eq!(parsed.status.staged, 0);
        assert!(parsed.files[0].conflicted);
        assert_eq!(parsed.files[0].path, "conflict.json");
    }

    #[test]
    fn untracked_is_counted_separately() {
        let parsed = parse_porcelain_v2("? untracked.json\n");
        assert_eq!(parsed.status.untracked, 1);
        assert_eq!(parsed.status.staged, 0);
        assert_eq!(parsed.status.modified, 0);
        assert!(parsed.files[0].untracked);
    }

    #[test]
    fn ignored_files_are_counted_nowhere() {
        let parsed = parse_porcelain_v2("! ignored.json\n");
        assert_eq!(parsed.status, GitStatus::default());
        assert!(parsed.files.is_empty());
    }

    #[test]
    fn preserves_paths_containing_spaces() {
        let parsed = parse_porcelain_v2(&ordinary(".M", "src/my file.json"));
        assert_eq!(parsed.files[0].path, "src/my file.json");
    }

    #[test]
    fn mixed_realistic_output() {
        let text = format!(
            "# branch.oid abc123\n\
             # branch.head feature/x\n\
             # branch.ab +1 -0\n\
             {}\n{}\n{}\n\
             2 R. N... 100644 100644 100644 {H} {H} R100 renamed.json\torig.json\n\
             u UU N... 100644 100644 100644 100644 {H} {H} {H} conflict.json\n\
             ? untracked.json\n\
             ! ignored.json\n",
            ordinary("M.", "staged.json"),
            ordinary(".M", "unstaged.json"),
            ordinary("MM", "both.json"),
        );
        let parsed = parse_porcelain_v2(&text);

        assert_eq!(parsed.status.branch, "feature/x");
        assert_eq!(parsed.status.ahead, 1);
        // staged.json, both.json, renamed.json
        assert_eq!(parsed.status.staged, 3);
        // unstaged.json, both.json
        assert_eq!(parsed.status.modified, 2);
        assert_eq!(parsed.status.untracked, 1);
        assert_eq!(parsed.status.conflicted, 1);
        // Ignored files are excluded from the list.
        assert_eq!(parsed.files.len(), 6);
    }

    #[test]
    fn classification_flags_match_states() {
        let parsed = parse_porcelain_v2(&ordinary("M.", "a.json"));
        assert!(parsed.files[0].staged);
        assert!(!parsed.files[0].unstaged);

        let parsed = parse_porcelain_v2(&ordinary(".M", "a.json"));
        assert!(!parsed.files[0].staged);
        assert!(parsed.files[0].unstaged);
    }

    #[test]
    fn subpath_includes_nested_paths() {
        let sub = Some("api".to_string());
        assert!(is_within_subpath("api/a.json", &sub));
        assert!(is_within_subpath("api/nested/deep/c.json", &sub));
    }

    /// A raw string prefix would wrongly match `apixyz`; matching must be on a
    /// path-segment boundary.
    #[test]
    fn subpath_excludes_similar_prefix() {
        let sub = Some("api".to_string());
        assert!(!is_within_subpath("apixyz/d.json", &sub));
        assert!(!is_within_subpath("src/b.cs", &sub));
    }

    #[test]
    fn no_subpath_includes_everything() {
        assert!(is_within_subpath("anything/at/all.json", &None));
    }

    #[test]
    fn empty_subpath_includes_everything() {
        assert!(is_within_subpath("anything.json", &Some(String::new())));
        assert!(is_within_subpath("anything.json", &Some("/".to_string())));
    }

    #[test]
    fn subpath_normalizes_windows_separators() {
        let sub = Some("api\\collections".to_string());
        assert!(is_within_subpath("api/collections/a.json", &sub));
    }

    #[test]
    fn subpath_matches_the_directory_itself() {
        assert!(is_within_subpath("api", &Some("api".to_string())));
    }
}

// -- Integration tests over real temporary repositories ----------------------
//
// These exercise the `*_impl` functions against an actual `git` binary, covering
// what the pure parser cannot: argument passing, the `AllowedRoots` gate, and the
// destructive-operation guards. Skipped with a clear message when git is absent.

#[cfg(test)]
mod repo_tests {
    use super::*;
    use std::path::PathBuf;
    use std::sync::atomic::{AtomicU32, Ordering};

    static COUNTER: AtomicU32 = AtomicU32::new(0);

    fn git_available() -> bool {
        hidden_command("git")
            .arg("--version")
            .output()
            .map(|o| o.status.success())
            .unwrap_or(false)
    }

    /// A throwaway repository with a committed baseline, plus an `AllowedRoots`
    /// that has been granted it — mirroring what a user's directory pick does.
    struct TestRepo {
        dir: PathBuf,
        roots: AllowedRoots,
    }

    impl TestRepo {
        fn new(label: &str) -> Self {
            let id = COUNTER.fetch_add(1, Ordering::SeqCst);
            let dir = std::env::temp_dir().join(format!("swebkit-git-{label}-{id}"));
            let _ = std::fs::remove_dir_all(&dir);
            std::fs::create_dir_all(&dir).expect("create temp repo");

            let repo = Self {
                dir: std::fs::canonicalize(&dir).expect("canonicalize temp repo"),
                roots: AllowedRoots::new(),
            };
            repo.roots.allow(repo.dir.clone());

            repo.git(&["init", "--initial-branch=main"]);
            repo.git(&["config", "user.email", "test@example.com"]);
            repo.git(&["config", "user.name", "Test"]);
            repo.git(&["config", "commit.gpgsign", "false"]);
            // On Windows git rewrites LF to CRLF on checkout, so a reverted
            // file would not byte-match what the test wrote.
            repo.git(&["config", "core.autocrlf", "false"]);

            repo.write("api/baseline.json", "{\"a\":1}\n");
            repo.write("src/unrelated.txt", "untouched\n");
            repo.git(&["add", "--all"]);
            repo.git(&["commit", "-m", "baseline"]);
            repo
        }

        fn path(&self) -> String {
            self.dir.to_string_lossy().to_string()
        }

        fn git(&self, args: &[&str]) -> String {
            run_git(&self.dir, args).unwrap_or_else(|e| panic!("git {args:?} failed: {e}"))
        }

        fn write(&self, rel: &str, contents: &str) {
            let full = self.dir.join(rel);
            std::fs::create_dir_all(full.parent().unwrap()).unwrap();
            std::fs::write(full, contents).unwrap();
        }

        fn write_bytes(&self, rel: &str, contents: &[u8]) {
            let full = self.dir.join(rel);
            std::fs::create_dir_all(full.parent().unwrap()).unwrap();
            std::fs::write(full, contents).unwrap();
        }

        fn read(&self, rel: &str) -> String {
            std::fs::read_to_string(self.dir.join(rel)).unwrap()
        }

        fn short_status(&self) -> String {
            self.git(&["status", "--short"])
        }
    }

    impl Drop for TestRepo {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.dir);
        }
    }

    /// Skips the body when git is missing rather than failing the suite.
    macro_rules! require_git {
        () => {
            if !git_available() {
                eprintln!("skipping: git is not on PATH");
                return;
            }
        };
    }

    #[test]
    fn is_repo_distinguishes_a_repository_from_a_plain_directory() {
        require_git!();
        let repo = TestRepo::new("isrepo");
        assert!(is_repo_impl(&repo.path(), &repo.roots).unwrap());

        let plain = std::env::temp_dir().join("swebkit-git-plain-dir");
        std::fs::create_dir_all(&plain).unwrap();
        let canonical = std::fs::canonicalize(&plain).unwrap();
        let roots = AllowedRoots::new();
        roots.allow(canonical.clone());
        assert!(!is_repo_impl(&canonical.to_string_lossy(), &roots).unwrap());
        let _ = std::fs::remove_dir_all(&plain);
    }

    #[test]
    fn changed_files_reports_a_mixed_dirty_repository() {
        require_git!();
        let repo = TestRepo::new("mixed");

        // Commit a second tracked file first, so the three kinds of change below
        // are all still pending when status is read. Staging and then committing
        // in one go would sweep the staged change into the commit.
        repo.write("api/tracked.json", "{}\n");
        repo.git(&["add", "--", "api/tracked.json"]);
        repo.git(&["commit", "-m", "add tracked.json"]);

        repo.write("api/baseline.json", "{\"a\":2}\n");
        repo.git(&["add", "--", "api/baseline.json"]);            // staged only
        repo.write("api/tracked.json", "{\"changed\":true}\n");    // unstaged only
        repo.write("api/brand-new.json", "{}\n");                 // untracked

        let files = changed_files_impl(&repo.path(), None, &repo.roots).unwrap();
        let by_path = |p: &str| files.iter().find(|f| f.path == p).cloned();

        assert!(by_path("api/baseline.json").unwrap().staged);
        assert!(by_path("api/tracked.json").unwrap().unstaged);
        assert!(by_path("api/brand-new.json").unwrap().untracked);

        let status = status_impl(&repo.path(), &repo.roots).unwrap();
        assert_eq!(status.staged, 1);
        assert_eq!(status.modified, 1);
        assert_eq!(status.untracked, 1);
        assert_eq!(status.branch, "main");
    }

    #[test]
    fn changed_files_filters_to_the_api_subpath() {
        require_git!();
        let repo = TestRepo::new("subpath");
        repo.write("api/baseline.json", "{\"a\":2}\n");
        repo.write("src/unrelated.txt", "changed\n");

        let all = changed_files_impl(&repo.path(), None, &repo.roots).unwrap();
        assert_eq!(all.len(), 2);

        let scoped =
            changed_files_impl(&repo.path(), Some("api".to_string()), &repo.roots).unwrap();
        assert_eq!(scoped.len(), 1);
        assert_eq!(scoped[0].path, "api/baseline.json");
    }

    #[test]
    fn stage_paths_stages_only_the_named_file() {
        require_git!();
        let repo = TestRepo::new("stage");
        repo.write("api/baseline.json", "{\"a\":2}\n");
        repo.write("src/unrelated.txt", "changed\n");

        stage_paths_impl(&repo.path(), &["api/baseline.json".to_string()], &repo.roots).unwrap();

        let files = changed_files_impl(&repo.path(), None, &repo.roots).unwrap();
        let baseline = files.iter().find(|f| f.path == "api/baseline.json").unwrap();
        let unrelated = files.iter().find(|f| f.path == "src/unrelated.txt").unwrap();
        assert!(baseline.staged, "named file should be staged");
        assert!(!unrelated.staged, "unnamed file must not be staged");
    }

    #[test]
    fn stage_paths_handles_a_path_containing_a_space() {
        require_git!();
        let repo = TestRepo::new("space");
        repo.write("api/my file.json", "{}\n");

        stage_paths_impl(&repo.path(), &["api/my file.json".to_string()], &repo.roots).unwrap();

        let files = changed_files_impl(&repo.path(), None, &repo.roots).unwrap();
        assert!(files.iter().find(|f| f.path == "api/my file.json").unwrap().staged);
    }

    /// Proves the `--` separator: without it git would read this as a flag.
    #[test]
    fn stage_paths_treats_a_leading_dash_as_a_path() {
        require_git!();
        let repo = TestRepo::new("dash");
        repo.write("api/-weird.json", "{}\n");

        stage_paths_impl(&repo.path(), &["api/-weird.json".to_string()], &repo.roots).unwrap();

        let files = changed_files_impl(&repo.path(), None, &repo.roots).unwrap();
        assert!(files.iter().find(|f| f.path == "api/-weird.json").unwrap().staged);
    }

    #[test]
    fn unstage_paths_leaves_the_worktree_untouched() {
        require_git!();
        let repo = TestRepo::new("unstage");
        repo.write("api/baseline.json", "{\"a\":2}\n");
        repo.git(&["add", "--", "api/baseline.json"]);

        unstage_paths_impl(&repo.path(), &["api/baseline.json".to_string()], &repo.roots).unwrap();

        let files = changed_files_impl(&repo.path(), None, &repo.roots).unwrap();
        let baseline = files.iter().find(|f| f.path == "api/baseline.json").unwrap();
        assert!(!baseline.staged);
        assert!(baseline.unstaged);
        assert_eq!(repo.read("api/baseline.json"), "{\"a\":2}\n");
    }

    #[test]
    fn revert_paths_restores_the_committed_content() {
        require_git!();
        let repo = TestRepo::new("revert");
        repo.write("api/baseline.json", "{\"a\":999}\n");

        revert_paths_impl(&repo.path(), &["api/baseline.json".to_string()], &repo.roots).unwrap();

        assert_eq!(repo.read("api/baseline.json"), "{\"a\":1}\n");
        assert!(repo.short_status().trim().is_empty());
    }

    /// DEC-G6: an untracked file has no committed version, so reverting it would
    /// mean deleting the user's new work.
    #[test]
    fn revert_paths_refuses_an_untracked_file() {
        require_git!();
        let repo = TestRepo::new("revert-untracked");
        repo.write("api/brand-new.json", "{}\n");

        let err = revert_paths_impl(&repo.path(), &["api/brand-new.json".to_string()], &repo.roots)
            .expect_err("reverting an untracked file must fail");
        assert!(err.contains("untracked"), "unhelpful error: {err}");
        // The file must survive.
        assert_eq!(repo.read("api/brand-new.json"), "{}\n");
    }

    #[test]
    fn mutations_reject_a_path_that_is_not_a_reported_change() {
        require_git!();
        let repo = TestRepo::new("unreported");

        for result in [
            stage_paths_impl(&repo.path(), &["api/baseline.json".to_string()], &repo.roots),
            unstage_paths_impl(&repo.path(), &["api/baseline.json".to_string()], &repo.roots),
            revert_paths_impl(&repo.path(), &["api/baseline.json".to_string()], &repo.roots),
        ] {
            let err = result.expect_err("a clean file is not a reported change");
            assert!(err.contains("not a reported change"), "unhelpful error: {err}");
        }
    }

    #[test]
    fn mutations_reject_an_empty_path_list() {
        require_git!();
        let repo = TestRepo::new("emptylist");
        assert!(stage_paths_impl(&repo.path(), &[], &repo.roots).is_err());
    }

    #[test]
    fn diff_file_returns_head_and_worktree_versions() {
        require_git!();
        let repo = TestRepo::new("diff");
        repo.write("api/baseline.json", "{\"a\":2}\n");

        let diff = diff_file_impl(&repo.path(), "api/baseline.json", &repo.roots).unwrap();
        assert_eq!(diff.original.as_deref(), Some("{\"a\":1}\n"));
        assert_eq!(diff.current, "{\"a\":2}\n");
        assert!(!diff.is_binary);
    }

    #[test]
    fn diff_file_reports_a_new_file_as_having_no_original() {
        require_git!();
        let repo = TestRepo::new("diff-new");
        repo.write("api/brand-new.json", "{\"new\":true}\n");

        let diff = diff_file_impl(&repo.path(), "api/brand-new.json", &repo.roots).unwrap();
        assert!(diff.original.is_none());
        assert_eq!(diff.current, "{\"new\":true}\n");
    }

    #[test]
    fn diff_file_detects_binary_content() {
        require_git!();
        let repo = TestRepo::new("diff-binary");
        repo.write_bytes("api/blob.bin", &[0x00, 0x01, 0x02, 0xff]);

        let diff = diff_file_impl(&repo.path(), "api/blob.bin", &repo.roots).unwrap();
        assert!(diff.is_binary);
        assert!(diff.current.is_empty(), "binary content must not be returned as text");
    }

    #[test]
    fn commit_refuses_staged_files_outside_the_api_subpath() {
        require_git!();
        let repo = TestRepo::new("commit-guard");
        repo.write("api/baseline.json", "{\"a\":2}\n");
        repo.write("src/unrelated.txt", "work in progress\n");
        repo.git(&["add", "--all"]);

        let err = commit_impl(&repo.path(), "scoped commit", Some("api".to_string()), &repo.roots)
            .expect_err("commit must refuse out-of-scope staged files");
        assert!(err.contains("src/unrelated.txt"), "error must name the offender: {err}");

        // Nothing was committed.
        assert!(repo.git(&["log", "--oneline"]).lines().count() == 1);
    }

    #[test]
    fn commit_succeeds_when_everything_staged_is_in_scope() {
        require_git!();
        let repo = TestRepo::new("commit-ok");
        repo.write("api/baseline.json", "{\"a\":2}\n");
        // Unrelated work exists but is deliberately left unstaged.
        repo.write("src/unrelated.txt", "work in progress\n");
        stage_paths_impl(&repo.path(), &["api/baseline.json".to_string()], &repo.roots).unwrap();

        commit_impl(&repo.path(), "scoped commit", Some("api".to_string()), &repo.roots).unwrap();

        let committed = repo.git(&["show", "--stat", "--name-only", "--format=", "HEAD"]);
        assert!(committed.contains("api/baseline.json"));
        assert!(
            !committed.contains("src/unrelated.txt"),
            "unrelated work must not be committed: {committed}"
        );
    }

    #[test]
    fn commit_rejects_an_empty_message() {
        require_git!();
        let repo = TestRepo::new("commit-empty-msg");
        assert!(commit_impl(&repo.path(), "   ", None, &repo.roots).is_err());
    }

    #[test]
    fn branches_and_checkout_round_trip() {
        require_git!();
        let repo = TestRepo::new("branches");

        create_branch_impl(&repo.path(), "feature/x", true, &repo.roots).unwrap();
        assert_eq!(status_impl(&repo.path(), &repo.roots).unwrap().branch, "feature/x");

        let names: Vec<String> = branches_impl(&repo.path(), &repo.roots)
            .unwrap()
            .into_iter()
            .map(|b| b.name)
            .collect();
        assert!(names.contains(&"feature/x".to_string()));
        assert!(names.contains(&"main".to_string()));

        checkout_branch_impl(&repo.path(), "main", &repo.roots).unwrap();
        assert_eq!(status_impl(&repo.path(), &repo.roots).unwrap().branch, "main");
    }

    #[test]
    fn create_branch_rejects_an_invalid_name() {
        require_git!();
        let repo = TestRepo::new("badbranch");
        for bad in ["has space", "bad..name", "-leading"] {
            assert!(
                create_branch_impl(&repo.path(), bad, false, &repo.roots).is_err(),
                "{bad} should have been rejected"
            );
        }
    }

    #[test]
    fn create_branch_rejects_a_duplicate_name() {
        require_git!();
        let repo = TestRepo::new("dupbranch");
        create_branch_impl(&repo.path(), "dup", false, &repo.roots).unwrap();
        assert!(create_branch_impl(&repo.path(), "dup", false, &repo.roots).is_err());
    }

    #[test]
    fn remote_url_is_none_without_a_remote() {
        require_git!();
        let repo = TestRepo::new("noremote");
        assert!(remote_url_impl(&repo.path(), &repo.roots).unwrap().is_none());
    }

    #[test]
    fn remote_url_returns_the_configured_origin() {
        require_git!();
        let repo = TestRepo::new("remote");
        repo.git(&["remote", "add", "origin", "https://github.com/org/repo.git"]);
        assert_eq!(
            remote_url_impl(&repo.path(), &repo.roots).unwrap().as_deref(),
            Some("https://github.com/org/repo.git")
        );
    }

    /// D7: every git command must be gated on `AllowedRoots`, not just one. A
    /// repository the user never picked must be unreachable from the webview.
    #[test]
    fn every_command_rejects_a_path_outside_allowed_roots() {
        require_git!();
        let repo = TestRepo::new("gate");
        // A fresh, empty allowlist: nothing has been granted.
        let denied = AllowedRoots::new();
        let path = repo.path();
        let files = vec!["api/baseline.json".to_string()];

        let outcomes: Vec<(&str, Result<(), String>)> = vec![
            ("status", status_impl(&path, &denied).map(|_| ())),
            ("changed_files", changed_files_impl(&path, None, &denied).map(|_| ())),
            ("branches", branches_impl(&path, &denied).map(|_| ())),
            ("stage_paths", stage_paths_impl(&path, &files, &denied)),
            ("unstage_paths", unstage_paths_impl(&path, &files, &denied)),
            ("revert_paths", revert_paths_impl(&path, &files, &denied)),
            ("diff_file", diff_file_impl(&path, "api/baseline.json", &denied).map(|_| ())),
            ("commit", commit_impl(&path, "msg", None, &denied)),
            ("checkout_branch", checkout_branch_impl(&path, "main", &denied)),
            ("create_branch", create_branch_impl(&path, "x", false, &denied)),
            ("remote_url", remote_url_impl(&path, &denied).map(|_| ())),
        ];

        for (name, result) in outcomes {
            assert!(
                result.is_err(),
                "{name} must reject a path outside AllowedRoots, but it succeeded"
            );
        }

        // `is_repo` reports false rather than erroring, but must not inspect it.
        assert!(!is_repo_impl(&path, &denied).unwrap());
    }

    #[test]
    fn commands_reject_a_file_path_where_a_directory_is_required() {
        require_git!();
        let repo = TestRepo::new("notdir");
        let file = repo.dir.join("api/baseline.json").to_string_lossy().to_string();
        let err = status_impl(&file, &repo.roots).expect_err("a file is not a repository directory");
        assert!(err.contains("not a directory"), "unhelpful error: {err}");
    }
}
