
// -- Integration tests over real temporary repositories ----------------------
//
// These exercise the `*_impl` functions against an actual `git` binary, covering
// what the pure parser cannot: argument passing, the `AllowedRoots` gate, and the
// destructive-operation guards. Skipped with a clear message when git is absent.

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

    /// The regression this guards: `diff_file_impl` used to always read the raw
    /// working-tree file, so a file with staged changes plus a *further* unstaged
    /// edit on top showed the unstaged content as if it were staged.
    #[test]
    fn diff_file_prefers_staged_content_over_a_further_unstaged_edit() {
        require_git!();
        let repo = TestRepo::new("diff-staged");
        repo.write("api/baseline.json", "{\"a\":2}\n");
        stage_paths_impl(&repo.path(), &["api/baseline.json".to_string()], &repo.roots).unwrap();
        // An additional edit on top of what was staged, deliberately left unstaged.
        repo.write("api/baseline.json", "{\"a\":3}\n");

        let diff = diff_file_impl(&repo.path(), "api/baseline.json", &repo.roots).unwrap();
        assert_eq!(diff.original.as_deref(), Some("{\"a\":1}\n"));
        // Must show what a commit right now would actually contain (the staged
        // content), not the further unstaged edit sitting on top of it.
        assert_eq!(diff.current, "{\"a\":2}\n");
    }

    #[test]
    fn diff_file_still_shows_working_tree_when_nothing_is_staged() {
        require_git!();
        let repo = TestRepo::new("diff-unstaged-only");
        repo.write("api/baseline.json", "{\"a\":2}\n");

        let diff = diff_file_impl(&repo.path(), "api/baseline.json", &repo.roots).unwrap();
        assert_eq!(diff.original.as_deref(), Some("{\"a\":1}\n"));
        assert_eq!(diff.current, "{\"a\":2}\n");
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
