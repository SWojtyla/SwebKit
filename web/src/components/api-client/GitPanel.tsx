import { useState, useEffect, useCallback, useMemo } from "react";
import {
  GitBranch as GitBranchIcon, RefreshCw, ArrowUp, ArrowDown, Check, Plus,
  FolderOpen, ExternalLink, Settings2, Trash2,
} from "lucide-react";
import {
  gitStatus, gitBranches, gitChangedFiles, gitCommit, gitPush, gitPull,
  gitStagePaths, gitUnstagePaths, gitRevertPaths, gitCheckoutBranch, gitCreateBranch,
  gitRemoteUrl, gitIsRepo, pickDirectory, isGitAvailable, restoreAllowedRoot,
  type GitStatus as GitStatusType, type GitBranch as GitBranchType, type GitFileChange,
} from "@/lib/tauri-bridge";
import { inferCompareUrl, remoteProviderName } from "@/lib/git-remote";
import type { GitFileAction } from "@/lib/git-status-format";
import {
  loadGitRepoState, saveGitRepoState, addRepo, removeRepo, setApiSubpath, selectedRepo,
  type GitRepoState,
} from "@/lib/stores/git-repo-preferences";
import { useNotification } from "@/components/layout/NotificationSystem";
import { NameDialog, ConfirmDialog } from "./Dialogs";
import { GitFileList } from "./GitFileList";
import { GitDiffPane } from "./GitDiffPane";

/** Why the panel cannot show a repository, so each case gets its own message. */
type Unavailable =
  | { kind: "no-tauri" }
  | { kind: "no-repo" }
  | { kind: "not-a-repo"; path: string }
  | { kind: "git-missing" }
  | { kind: "error"; message: string };

export function GitPanel() {
  const { notify } = useNotification();

  const [repoState, setRepoState] = useState<GitRepoState>(() => loadGitRepoState());
  const [status, setStatus] = useState<GitStatusType | null>(null);
  const [branches, setBranches] = useState<GitBranchType[]>([]);
  const [files, setFiles] = useState<GitFileChange[]>([]);
  const [remote, setRemote] = useState<string | null>(null);
  const [unavailable, setUnavailable] = useState<Unavailable | null>(
    isGitAvailable() ? null : { kind: "no-tauri" },
  );
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [inlineError, setInlineError] = useState<string | null>(null);
  const [commitMessage, setCommitMessage] = useState("");
  const [showCommitForm, setShowCommitForm] = useState(false);
  const [diffFile, setDiffFile] = useState<string | null>(null);
  const [showSettings, setShowSettings] = useState(false);
  const [branchDialog, setBranchDialog] = useState(false);
  const [confirm, setConfirm] = useState<{ message: string; onConfirm: () => void } | null>(null);

  const repo = selectedRepo(repoState);
  const repoPath = repo?.path ?? null;
  const subpath = repo?.apiSubpath ?? null;

  const persist = useCallback((next: GitRepoState) => {
    setRepoState(next);
    saveGitRepoState(next);
  }, []);

  // A path persisted from a previous session is not in the in-memory AllowedRoots
  // yet; ask Rust to re-admit it from its own grant list (DEC-G3).
  useEffect(() => {
    if (!repoPath || !isGitAvailable()) return;
    void restoreAllowedRoot(repoPath);
  }, [repoPath]);

  const refresh = useCallback(async () => {
    if (!isGitAvailable()) {
      setUnavailable({ kind: "no-tauri" });
      return;
    }
    if (!repoPath) {
      setUnavailable({ kind: "no-repo" });
      return;
    }

    setLoading(true);
    setInlineError(null);
    try {
      await restoreAllowedRoot(repoPath);

      if (!(await gitIsRepo(repoPath))) {
        setUnavailable({ kind: "not-a-repo", path: repoPath });
        setStatus(null);
        return;
      }

      const [s, b, f, r] = await Promise.all([
        gitStatus(repoPath),
        gitBranches(repoPath),
        gitChangedFiles(repoPath, subpath),
        gitRemoteUrl(repoPath).catch(() => null),
      ]);
      setStatus(s);
      setBranches(b);
      setFiles(f);
      setRemote(r);
      setUnavailable(null);
    } catch (e) {
      const message = e instanceof Error ? e.message : String(e);
      // git's own "not found" message is the one case worth a dedicated state.
      setUnavailable(
        message.toLowerCase().includes("git was not found")
          ? { kind: "git-missing" }
          : { kind: "error", message },
      );
      setStatus(null);
    } finally {
      setLoading(false);
    }
  }, [repoPath, subpath]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const chooseRepo = async () => {
    try {
      const dir = await pickDirectory("Select a repository containing API collections");
      if (!dir) return;
      persist(addRepo(repoState, dir));
    } catch (e) {
      setInlineError(e instanceof Error ? e.message : String(e));
    }
  };

  /** Runs a git mutation with consistent busy state, feedback and refresh. */
  const runAction = async (label: string, action: () => Promise<unknown>) => {
    setBusy(true);
    setInlineError(null);
    try {
      await action();
      notify("success", label);
      await refresh();
      return true;
    } catch (e) {
      const message = e instanceof Error ? e.message : String(e);
      setInlineError(message);
      notify("error", `${label} failed`, message);
      return false;
    } finally {
      setBusy(false);
    }
  };

  const handleFileAction = (action: GitFileAction, targets: GitFileChange[]) => {
    if (!repoPath) return;
    const paths = targets.map((f) => f.path);

    if (action === "diff") {
      setDiffFile(paths[0] ?? null);
      return;
    }
    if (action === "stage") {
      void runAction(`Staged ${describe(paths)}`, () => gitStagePaths(repoPath, paths));
      return;
    }
    if (action === "unstage") {
      void runAction(`Unstaged ${describe(paths)}`, () => gitUnstagePaths(repoPath, paths));
      return;
    }

    // Revert is the only irreversible action here, so it confirms by name (DEC-G6).
    setConfirm({
      message:
        `Discard uncommitted changes to ${paths.length === 1 ? "this file" : `these ${paths.length} files`}?\n\n` +
        `${paths.join("\n")}\n\nThis cannot be undone.`,
      onConfirm: () => {
        setConfirm(null);
        void runAction(`Reverted ${describe(paths)}`, () => gitRevertPaths(repoPath, paths));
      },
    });
  };

  const stagedFiles = useMemo(() => files.filter((f) => f.staged), [files]);

  const handleCommit = async () => {
    if (!repoPath || !commitMessage.trim()) return;
    const ok = await runAction("Committed", () => gitCommit(repoPath, commitMessage, subpath));
    if (ok) {
      setCommitMessage("");
      setShowCommitForm(false);
    }
  };

  const compareUrl = useMemo(
    () => (remote && status?.branch ? inferCompareUrl(remote, status.branch) : null),
    [remote, status?.branch],
  );

  const openCompare = async () => {
    if (!compareUrl) return;
    try {
      const { open } = await import("@tauri-apps/plugin-shell");
      await open(compareUrl);
    } catch (e) {
      setInlineError(e instanceof Error ? e.message : String(e));
    }
  };

  // ── Unavailable / first-run states ─────────────────────────────────────────

  if (unavailable) {
    return (
      <div
        className="flex h-full flex-col items-center justify-center gap-3 p-6 text-center"
        data-testid="git-panel-unavailable"
      >
        <GitBranchIcon className="h-8 w-8 text-muted-foreground" />
        <p className="text-sm text-muted-foreground">{unavailableMessage(unavailable)}</p>
        {unavailable.kind !== "no-tauri" && unavailable.kind !== "git-missing" && (
          <button
            onClick={chooseRepo}
            className="flex items-center gap-1 rounded border px-3 py-1.5 text-xs hover:bg-accent"
            data-testid="git-choose-repo"
          >
            <FolderOpen className="h-3.5 w-3.5" />
            {unavailable.kind === "no-repo" ? "Choose repository" : "Choose a different repository"}
          </button>
        )}
        {unavailable.kind === "error" && (
          <button
            onClick={() => void refresh()}
            className="rounded border px-3 py-1.5 text-xs hover:bg-accent"
            data-testid="git-retry"
          >
            Retry
          </button>
        )}
      </div>
    );
  }

  if (!status) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
        Loading git status…
      </div>
    );
  }

  return (
    <div className="flex h-full min-h-0 flex-col" data-testid="git-panel">
      {/* Header: branch switcher and remote actions */}
      <div className="flex flex-wrap items-center gap-2 border-b px-3 py-2">
        <GitBranchIcon className="h-4 w-4 shrink-0 text-muted-foreground" />
        <select
          value={status.branch}
          onChange={(e) => {
            const branch = e.target.value;
            if (!repoPath || branch === status.branch) return;
            void runAction(`Switched to ${branch}`, () => gitCheckoutBranch(repoPath, branch));
          }}
          disabled={busy}
          className="min-w-0 max-w-[10rem] rounded border bg-background px-1.5 py-1 font-mono text-xs"
          data-testid="git-branch-select"
          aria-label="Current branch"
        >
          {/* A detached HEAD is not in the branch list, so it is added explicitly. */}
          {!branches.some((b) => b.name === status.branch) && (
            <option value={status.branch}>{status.branch}</option>
          )}
          {branches.map((b) => (
            <option key={b.name} value={b.name}>{b.name}</option>
          ))}
        </select>
        <span className="font-mono text-xs font-semibold" data-testid="git-branch-name">
          {status.branch}
        </span>

        <button
          onClick={() => setBranchDialog(true)}
          disabled={busy}
          className="rounded border p-1 hover:bg-accent disabled:opacity-50"
          title="New branch"
          data-testid="git-new-branch"
        >
          <Plus className="h-3.5 w-3.5" />
        </button>

        <div className="ml-auto flex items-center gap-1">
          <button
            onClick={() => void refresh()}
            disabled={loading}
            className="rounded border p-1.5 hover:bg-accent disabled:opacity-50"
            title="Refresh"
            data-testid="git-refresh"
          >
            <RefreshCw className={`h-3.5 w-3.5 ${loading ? "animate-spin" : ""}`} />
          </button>
          <button
            onClick={() => repoPath && void runAction("Pulled", () => gitPull(repoPath))}
            disabled={busy}
            className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
            data-testid="git-pull"
          >
            <ArrowDown className="h-3.5 w-3.5" /> Pull
          </button>
          <button
            onClick={() => repoPath && void runAction("Pushed", () => gitPush(repoPath))}
            disabled={busy}
            className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
            data-testid="git-push"
          >
            <ArrowUp className="h-3.5 w-3.5" /> Push
          </button>
          <button
            onClick={() => setShowCommitForm(!showCommitForm)}
            className="flex items-center gap-1 rounded bg-primary px-2 py-1 text-xs text-primary-foreground hover:opacity-90"
            data-testid="git-commit-btn"
          >
            <Check className="h-3.5 w-3.5" /> Commit
          </button>
          <button
            onClick={() => setShowSettings(!showSettings)}
            className={`rounded border p-1.5 hover:bg-accent ${showSettings ? "border-primary text-primary" : ""}`}
            title="Repository settings"
            data-testid="git-settings-toggle"
          >
            <Settings2 className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      {/* Repository settings */}
      {showSettings && (
        <div className="space-y-2 border-b bg-muted/30 px-3 py-2 text-xs" data-testid="git-settings">
          <div className="flex items-center gap-2">
            <span className="w-20 shrink-0 text-muted-foreground">Repository</span>
            <select
              value={repoState.selectedPath ?? ""}
              onChange={(e) => persist({ ...repoState, selectedPath: e.target.value })}
              className="min-w-0 flex-1 rounded border bg-background px-1.5 py-1 font-mono"
              data-testid="git-repo-select"
            >
              {repoState.repos.map((r) => (
                <option key={r.path} value={r.path}>{r.path}</option>
              ))}
            </select>
            <button
              onClick={chooseRepo}
              className="flex items-center gap-1 rounded border px-1.5 py-1 hover:bg-accent"
              data-testid="git-add-repo"
            >
              <FolderOpen className="h-3 w-3" /> Add
            </button>
            {repoPath && repoState.repos.length > 1 && (
              <button
                onClick={() => persist(removeRepo(repoState, repoPath))}
                className="rounded border p-1 hover:bg-accent"
                title="Forget this repository"
                data-testid="git-remove-repo"
              >
                <Trash2 className="h-3 w-3" />
              </button>
            )}
          </div>

          <div className="flex items-center gap-2">
            <span className="w-20 shrink-0 text-muted-foreground">API path</span>
            <SubpathInput
              value={subpath}
              onCommit={(next) => repoPath && persist(setApiSubpath(repoState, repoPath, next))}
            />
          </div>
          {!subpath && (
            <p style={{ color: "var(--warning)" }} data-testid="git-subpath-warning">
              Without an API path, staging and committing are not scoped — unrelated changes in this
              repository can be committed.
            </p>
          )}
        </div>
      )}

      {inlineError && (
        <div
          className="border-b px-3 py-2 text-xs"
          style={{
            color: "var(--destructive)",
            backgroundColor: "color-mix(in oklch, var(--destructive) 10%, transparent)",
          }}
          data-testid="git-error"
        >
          {inlineError}
        </div>
      )}

      {/* Commit form, with the exact file list it will commit */}
      {showCommitForm && (
        <div className="space-y-2 border-b px-3 py-2" data-testid="git-commit-form">
          <textarea
            value={commitMessage}
            onChange={(e) => setCommitMessage(e.target.value)}
            placeholder="Commit message..."
            rows={3}
            className="w-full rounded-md border bg-card px-2 py-1.5 text-sm"
            data-testid="git-commit-message"
          />
          <div className="rounded border bg-muted/30 px-2 py-1.5 text-xs" data-testid="git-commit-preview">
            {stagedFiles.length === 0 ? (
              <span className="text-muted-foreground">
                Nothing staged. Stage files before committing.
              </span>
            ) : (
              <>
                <span className="text-muted-foreground">
                  Will commit {stagedFiles.length} staged file{stagedFiles.length === 1 ? "" : "s"}:
                </span>
                <ul className="mt-1 space-y-0.5 font-mono">
                  {stagedFiles.map((f) => (
                    <li key={f.path} className="truncate">{f.path}</li>
                  ))}
                </ul>
              </>
            )}
          </div>
          <div className="flex justify-end gap-2">
            <button
              onClick={() => setShowCommitForm(false)}
              className="rounded border px-3 py-1 text-xs hover:bg-accent"
              data-testid="git-commit-cancel"
            >
              Cancel
            </button>
            <button
              onClick={() => void handleCommit()}
              disabled={!commitMessage.trim() || busy || stagedFiles.length === 0}
              className="flex items-center gap-1 rounded bg-primary px-3 py-1 text-xs text-primary-foreground disabled:opacity-50"
              data-testid="git-commit-submit"
            >
              <Check className="h-3.5 w-3.5" />
              {busy ? "Committing..." : "Commit"}
            </button>
          </div>
        </div>
      )}

      {/* Status summary */}
      <div className="flex flex-wrap gap-4 border-b px-3 py-2 text-xs">
        <SummaryItem label="Staged" value={status.staged} testId="git-staged-count" />
        <SummaryItem label="Modified" value={status.modified} testId="git-modified-count" />
        <SummaryItem label="Untracked" value={status.untracked} testId="git-untracked-count" />
        {status.conflicted > 0 && (
          <SummaryItem
            label="Conflicted"
            value={status.conflicted}
            testId="git-conflicted-count"
            color="var(--destructive)"
          />
        )}
        {status.ahead > 0 && (
          <div className="flex items-center gap-1" style={{ color: "var(--success)" }}>
            <ArrowUp className="h-3 w-3" />
            <span data-testid="git-ahead-count">{status.ahead}</span>
          </div>
        )}
        {status.behind > 0 && (
          <div className="flex items-center gap-1" style={{ color: "var(--warning)" }}>
            <ArrowDown className="h-3 w-3" />
            <span data-testid="git-behind-count">{status.behind}</span>
          </div>
        )}
        {compareUrl && (
          <button
            onClick={() => void openCompare()}
            className="ml-auto flex items-center gap-1 text-primary hover:underline"
            data-testid="git-compare-link"
          >
            <ExternalLink className="h-3 w-3" />
            Compare on {remoteProviderName(remote ?? "") ?? "remote"}
          </button>
        )}
      </div>

      {/* Changed files */}
      <div className="min-h-0 flex-1 overflow-auto px-3 py-2">
        <GitFileList files={files} subpath={subpath} busy={busy} onAction={handleFileAction} />
      </div>

      {diffFile && repoPath && (
        <div className="h-1/2 min-h-0 shrink-0">
          <GitDiffPane repoPath={repoPath} file={diffFile} onClose={() => setDiffFile(null)} />
        </div>
      )}

      {branchDialog && (
        <NameDialog
          title="New Branch"
          label="Branch name"
          defaultValue=""
          confirmText="Create & switch"
          onConfirm={(name) => {
            setBranchDialog(false);
            if (!repoPath) return;
            void runAction(`Created ${name}`, () => gitCreateBranch(repoPath, name, true));
          }}
          onCancel={() => setBranchDialog(false)}
        />
      )}

      {confirm && (
        <ConfirmDialog
          message={confirm.message}
          onConfirm={confirm.onConfirm}
          onCancel={() => setConfirm(null)}
        />
      )}
    </div>
  );
}

/**
 * Commits on blur or Enter rather than on every keystroke.
 *
 * The subpath is a dependency of `refresh`, so persisting per character would spawn
 * a `git status` process per keystroke.
 */
function SubpathInput({
  value,
  onCommit,
}: {
  value: string | null;
  onCommit: (next: string | null) => void;
}) {
  const [draft, setDraft] = useState(value ?? "");

  // Follow external changes (switching repository) without fighting local typing.
  useEffect(() => {
    setDraft(value ?? "");
  }, [value]);

  const commit = () => {
    const trimmed = draft.trim();
    const next = trimmed.length > 0 ? trimmed : null;
    if (next !== value) onCommit(next);
  };

  return (
    <input
      type="text"
      value={draft}
      onChange={(e) => setDraft(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => {
        if (e.key === "Enter") commit();
        if (e.key === "Escape") setDraft(value ?? "");
      }}
      placeholder="e.g. api/collections — blank = repository root"
      className="min-w-0 flex-1 rounded border bg-background px-1.5 py-1 font-mono"
      data-testid="git-api-subpath"
    />
  );
}

function SummaryItem({
  label,
  value,
  testId,
  color,
}: {
  label: string;
  value: number;
  testId: string;
  color?: string;
}) {
  return (
    <div className="flex items-center gap-1" style={color ? { color } : undefined}>
      <span className={color ? undefined : "text-muted-foreground"}>{label}:</span>
      <span className="font-mono" data-testid={testId}>{value}</span>
    </div>
  );
}

function describe(paths: string[]): string {
  return paths.length === 1 ? paths[0] : `${paths.length} files`;
}

function unavailableMessage(state: Unavailable): string {
  switch (state.kind) {
    case "no-tauri":
      return "Git actions need the SwebKit desktop app.";
    case "no-repo":
      return "Choose the repository that holds your API collections to review and commit changes.";
    case "not-a-repo":
      return `${state.path} is not a Git repository.`;
    case "git-missing":
      return "Git was not found on this system. Install Git and restart SwebKit.";
    case "error":
      return state.message;
  }
}
