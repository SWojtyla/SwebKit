// Unit tests for git.rs — extracted via #[path] so `super::*` still resolves here.
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
