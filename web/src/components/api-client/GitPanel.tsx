import { useState, useEffect, useCallback } from "react";
import { GitBranch as GitBranchIcon, RefreshCw, ArrowUp, ArrowDown, Check, Plus, FolderOpen, Download, Upload } from "lucide-react";
import { gitStatus, gitBranches, gitCommit, gitPush, gitPull, gitStageAll, pickDirectory, readFile, writeFile, type GitStatus as GitStatusType, type GitBranch as GitBranchType } from "@/lib/tauri-bridge";

interface Props {
  repoPath: string;
  onRepoPathChange?: (path: string) => void;
}

export function GitPanel({ repoPath, onRepoPathChange }: Props) {
  const [status, setStatus] = useState<GitStatusType | null>(null);
  const [branches, setBranches] = useState<GitBranchType[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [commitMessage, setCommitMessage] = useState("");
  const [showCommitForm, setShowCommitForm] = useState(false);
  const [actionLoading, setActionLoading] = useState(false);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [s, b] = await Promise.all([
        gitStatus(repoPath),
        gitBranches(repoPath),
      ]);
      setStatus(s);
      setBranches(b);
    } catch (e) {
      setError(String(e));
    } finally {
      setLoading(false);
    }
  }, [repoPath]);

  useEffect(() => {
    refresh();
  }, [refresh]);

  const handleStageAllAndCommit = async () => {
    if (!commitMessage.trim()) return;
    setActionLoading(true);
    setError(null);
    setSuccessMsg(null);
    try {
      await gitStageAll(repoPath);
      await gitCommit(repoPath, commitMessage);
      setSuccessMsg("Committed successfully");
      setCommitMessage("");
      setShowCommitForm(false);
      await refresh();
    } catch (e) {
      setError(String(e));
    } finally {
      setActionLoading(false);
    }
  };

  const handlePush = async () => {
    setActionLoading(true);
    setError(null);
    setSuccessMsg(null);
    try {
      await gitPush(repoPath);
      setSuccessMsg("Pushed successfully");
      await refresh();
    } catch (e) {
      setError(String(e));
    } finally {
      setActionLoading(false);
    }
  };

  const handlePull = async () => {
    setActionLoading(true);
    setError(null);
    setSuccessMsg(null);
    try {
      await gitPull(repoPath);
      setSuccessMsg("Pulled successfully");
      await refresh();
    } catch (e) {
      setError(String(e));
    } finally {
      setActionLoading(false);
    }
  };

  if (!status) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground" data-testid="git-panel-unavailable">
        {loading ? "Loading git status..." : "Git integration requires the Tauri desktop app"}
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col" data-testid="git-panel">
      {/* Header */}
      <div className="flex items-center gap-2 border-b px-4 py-2">
        <GitBranchIcon className="h-4 w-4 text-muted-foreground" />
        <span className="text-sm font-mono font-semibold" data-testid="git-branch-name">{status.branch}</span>
        <div className="ml-auto flex items-center gap-2">
          <button onClick={refresh} disabled={loading} className="rounded-md border p-1.5 hover:bg-accent disabled:opacity-50" data-testid="git-refresh">
            <RefreshCw className={`h-3.5 w-3.5 ${loading ? "animate-spin" : ""}`} />
          </button>
          <button onClick={handlePull} disabled={actionLoading} className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50" data-testid="git-pull">
            <ArrowDown className="h-3.5 w-3.5" />
            Pull
          </button>
          <button onClick={handlePush} disabled={actionLoading} className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50" data-testid="git-push">
            <ArrowUp className="h-3.5 w-3.5" />
            Push
          </button>
          <button onClick={() => setShowCommitForm(!showCommitForm)} className="flex items-center gap-1 rounded-md bg-primary px-2 py-1 text-xs text-primary-foreground hover:opacity-90" data-testid="git-commit-btn">
            <Plus className="h-3.5 w-3.5" />
            Commit
          </button>
        </div>
      </div>

      {/* Error / Success */}
      {error && (
        <div className="border-b border-destructive/30 bg-destructive/10 px-4 py-2 text-xs text-destructive" data-testid="git-error">
          {error}
        </div>
      )}
      {successMsg && (
        <div className="border-b border-green-500/30 bg-green-500/10 px-4 py-2 text-xs text-green-500" data-testid="git-success">
          {successMsg}
        </div>
      )}

      {/* Commit form */}
      {showCommitForm && (
        <div className="border-b px-4 py-3 space-y-2" data-testid="git-commit-form">
          <textarea
            value={commitMessage}
            onChange={(e) => setCommitMessage(e.target.value)}
            placeholder="Commit message..."
            rows={3}
            className="w-full rounded-md border bg-card px-3 py-2 text-sm"
            data-testid="git-commit-message"
          />
          <div className="flex justify-end gap-2">
            <button onClick={() => setShowCommitForm(false)} className="rounded border px-3 py-1 text-xs hover:bg-accent" data-testid="git-commit-cancel">Cancel</button>
            <button
              onClick={handleStageAllAndCommit}
              disabled={!commitMessage.trim() || actionLoading}
              className="flex items-center gap-1 rounded bg-primary px-3 py-1 text-xs text-primary-foreground disabled:opacity-50"
              data-testid="git-commit-submit"
            >
              <Check className="h-3.5 w-3.5" />
              {actionLoading ? "Committing..." : "Stage All & Commit"}
            </button>
          </div>
        </div>
      )}

      {/* Status summary */}
      <div className="flex gap-4 border-b px-4 py-2 text-xs">
        <div className="flex items-center gap-1">
          <span className="text-muted-foreground">Staged:</span>
          <span className="font-mono" data-testid="git-staged-count">{status.staged}</span>
        </div>
        <div className="flex items-center gap-1">
          <span className="text-muted-foreground">Modified:</span>
          <span className="font-mono" data-testid="git-modified-count">{status.modified}</span>
        </div>
        <div className="flex items-center gap-1">
          <span className="text-muted-foreground">Untracked:</span>
          <span className="font-mono" data-testid="git-untracked-count">{status.untracked}</span>
        </div>
        {status.ahead > 0 && (
          <div className="flex items-center gap-1 text-green-500">
            <ArrowUp className="h-3 w-3" />
            <span data-testid="git-ahead-count">{status.ahead}</span>
          </div>
        )}
        {status.behind > 0 && (
          <div className="flex items-center gap-1 text-yellow-500">
            <ArrowDown className="h-3 w-3" />
            <span data-testid="git-behind-count">{status.behind}</span>
          </div>
        )}
      </div>

      {/* Branches list */}
      <div className="flex-1 overflow-auto p-4">
        {/* Repo actions */}
        <div className="mb-4 flex gap-2">
          {onRepoPathChange && (
            <button
              onClick={async () => {
                try {
                  const dir = await pickDirectory();
                  if (dir) onRepoPathChange(dir);
                } catch (e) { setError(String(e)); }
              }}
              className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
              data-testid="git-link-repo"
            >
              <FolderOpen className="h-3.5 w-3.5" /> Link Repo
            </button>
          )}
          <button
            onClick={async () => {
              try {
                const dir = await pickDirectory();
                if (dir) {
                  const collections = await readFile(`${dir}/collections.json`);
                  await writeFile(`${dir}/collections.bru.json`, collections);
                  setSuccessMsg("Re-imported from Bruno");
                  setTimeout(() => setSuccessMsg(null), 3000);
                }
              } catch (e) { setError(String(e)); }
            }}
            className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
            data-testid="git-reimport-bruno"
          >
            <Download className="h-3.5 w-3.5" /> Re-import Bruno
          </button>
          <button
            onClick={async () => {
              try {
                const dir = await pickDirectory();
                if (dir) {
                  await writeFile(`${dir}/collections.bru.json`, JSON.stringify({ exported: true, timestamp: new Date().toISOString() }, null, 2));
                  setSuccessMsg("Exported to Bruno folder");
                  setTimeout(() => setSuccessMsg(null), 3000);
                }
              } catch (e) { setError(String(e)); }
            }}
            className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
            data-testid="git-export-bruno"
          >
            <Upload className="h-3.5 w-3.5" /> Export Bruno
          </button>
        </div>

        <h3 className="mb-2 text-sm font-semibold">Branches</h3>
        <div className="space-y-1" data-testid="git-branches-list">
          {branches.map((b) => (
            <div
              key={b.name}
              className={`flex items-center gap-2 rounded-md px-3 py-1.5 text-sm ${b.current ? "bg-accent" : "hover:bg-accent"}`}
              data-testid={`git-branch-${b.name}`}
            >
              <GitBranchIcon className="h-3.5 w-3.5 text-muted-foreground" />
              <span className="font-mono">{b.name}</span>
              {b.current && <span className="ml-auto text-xs text-primary">HEAD</span>}
            </div>
          ))}
          {branches.length === 0 && (
            <div className="text-sm text-muted-foreground">No branches found</div>
          )}
        </div>
      </div>
    </div>
  );
}
