import { useState } from "react";
import {
  X, FolderGit2, FolderOpen, RefreshCw, Pencil, Trash2,
  CheckCircle, AlertCircle, GitBranch, Link2,
} from "lucide-react";
import {
  useLinkedRoots,
  useAddLinkedRoot,
  useUpdateLinkedRoot,
  useRemoveLinkedRoot,
  useReloadLinkedRoots,
  useDemoMode,
} from "@/lib/hooks";
import { useNotification } from "@/components/layout/NotificationSystem";
import { pickDirectory } from "@/lib/tauri-bridge";
import { ConfirmDialog } from "./Dialogs";
import type { LinkedRootSummary } from "@/lib/types";

interface LinkedProjectsDialogProps {
  onClose: () => void;
}

/**
 * Management surface for linked API project folders — the "store this project in
 * folder X, that one in folder Y" model. Each root owns a `.swebkit-api/` directory
 * of plain files (one `.swebreq.json` per request, `.swebenv.json` per environment)
 * that can live inside any git repo.
 */
export function LinkedProjectsDialog({ onClose }: LinkedProjectsDialogProps) {
  const { notify } = useNotification();
  const { data: roots = [], isLoading, error } = useLinkedRoots();
  const addRoot = useAddLinkedRoot();
  const updateRoot = useUpdateLinkedRoot();
  const removeRoot = useRemoveLinkedRoot();
  const reloadRoots = useReloadLinkedRoots();
  const { data: demoMode } = useDemoMode();
  const isDemo = demoMode?.isDemoMode ?? false;

  const [showAddForm, setShowAddForm] = useState(false);
  const [newPath, setNewPath] = useState("");
  const [newName, setNewName] = useState("");
  const [brunoPath, setBrunoPath] = useState("");
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState("");
  const [unlinkTarget, setUnlinkTarget] = useState<LinkedRootSummary | null>(null);

  const busy = addRoot.isPending || updateRoot.isPending || removeRoot.isPending || reloadRoots.isPending;

  const handleAdd = () => {
    const path = newPath.trim();
    if (!path) return;
    addRoot.mutate(
      {
        path,
        name: newName.trim() || null,
        brunoFolderPath: brunoPath.trim() || null,
      },
      {
        onSuccess: (updated) => {
          const added = updated.find((r) => r.path === path);
          notify("success", "Folder linked", added ? `"${added.displayName}" is now a live project folder.` : undefined);
          setShowAddForm(false);
          setNewPath("");
          setNewName("");
          setBrunoPath("");
        },
      },
    );
  };

  const handleUnlink = (root: LinkedRootSummary) => {
    setUnlinkTarget(null);
    removeRoot.mutate(root.id, {
      onSuccess: () => notify("success", "Folder unlinked", `"${root.displayName}" was removed from SwebKit. Its files were left on disk.`),
    });
  };

  const commitRename = (root: LinkedRootSummary) => {
    const name = renameValue.trim();
    setRenamingId(null);
    if (!name || name === root.name) return;
    updateRoot.mutate(
      { id: root.id, name },
      { onSuccess: () => notify("success", "Renamed", `Folder is now called "${name}".`) },
    );
  };

  const browse = async (setter: (path: string) => void, title: string) => {
    const picked = await pickDirectory(title);
    if (picked) setter(picked);
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
      data-testid="linked-projects-overlay"
    >
      <div
        className="flex max-h-[80vh] w-[560px] flex-col rounded-lg border bg-card shadow-lg"
        role="dialog"
        aria-modal="true"
        aria-label="Linked project folders"
        data-testid="linked-projects-dialog"
      >
        <div className="flex items-center justify-between border-b px-4 py-3">
          <h2 className="flex items-center gap-2 text-sm font-semibold">
            <FolderGit2 className="h-4 w-4" /> Project folders
          </h2>
          <div className="flex items-center gap-1">
            <button
              onClick={() => reloadRoots.mutate()}
              disabled={busy}
              className="rounded p-1 text-muted-foreground hover:bg-accent disabled:opacity-50"
              title="Re-read all linked folders from disk"
              data-testid="linked-roots-reload"
            >
              <RefreshCw className={`h-4 w-4 ${reloadRoots.isPending ? "animate-spin" : ""}`} />
            </button>
            <button onClick={onClose} className="rounded p-1 text-muted-foreground hover:bg-accent" data-testid="linked-projects-close" aria-label="Close">
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>

        <div className="min-h-0 flex-1 space-y-3 overflow-auto p-4">
          <p className="text-xs text-muted-foreground">
            Linked folders store collections and environments as plain files under{" "}
            <code>.swebkit-api/</code> — one file per request — so each project can live in its own
            git repository. Collections in <strong>app storage</strong> stay private to SwebKit.
          </p>

          {isLoading && (
            <div className="py-6 text-center text-xs text-muted-foreground" data-testid="linked-roots-loading">
              Loading linked folders…
            </div>
          )}

          {error && (
            <div className="flex items-start gap-2 rounded border border-destructive bg-destructive/10 px-3 py-2 text-xs text-destructive" data-testid="linked-roots-error">
              <AlertCircle className="h-4 w-4 shrink-0" />
              <span>{error.message}</span>
            </div>
          )}

          {!isLoading && !error && roots.length === 0 && (
            <div className="rounded border border-dashed px-4 py-6 text-center text-xs text-muted-foreground" data-testid="linked-roots-empty">
              <FolderGit2 className="mx-auto mb-2 h-6 w-6 opacity-50" />
              <p>No project folders linked yet.</p>
              <p className="mt-1">Link a folder to keep a project's requests in files Git can track — or link a Bruno collection folder to keep its <code>.bru</code> files in sync.</p>
            </div>
          )}

          {roots.map((root) => (
            <LinkedRootRow
              key={root.id}
              root={root}
              busy={busy}
              renaming={renamingId === root.id}
              renameValue={renameValue}
              onRenameChange={setRenameValue}
              onStartRename={() => { setRenamingId(root.id); setRenameValue(root.name); }}
              onCommitRename={() => commitRename(root)}
              onCancelRename={() => setRenamingId(null)}
              onToggleEnabled={() =>
                updateRoot.mutate(
                  { id: root.id, isEnabled: !root.isEnabled },
                  {
                    onSuccess: () =>
                      notify("success", root.isEnabled ? "Folder disabled" : "Folder enabled",
                        `"${root.displayName}" ${root.isEnabled ? "collections are hidden until re-enabled." : "collections are visible again."}`),
                  },
                )
              }
              onUnlink={() => setUnlinkTarget(root)}
            />
          ))}

          {!showAddForm ? (
            <button
              onClick={() => setShowAddForm(true)}
              disabled={isDemo}
              title={isDemo ? "Linked folders are not available in demo mode" : undefined}
              className="flex w-full items-center justify-center gap-2 rounded border border-dashed px-3 py-2 text-xs hover:bg-accent disabled:opacity-50"
              data-testid="linked-root-add-open"
            >
              <FolderOpen className="h-4 w-4" /> Link a folder…
            </button>
          ) : (
            <div className="space-y-2 rounded border p-3" data-testid="linked-root-add-form">
              <p className="text-xs font-medium">Link a folder</p>
              <p className="text-xs text-muted-foreground">
                SwebKit creates <code>.swebkit-api/</code> inside it. If the folder already holds a
                linked project (or you pick the <code>.swebkit-api</code> folder itself), it is
                attached as-is. To also mirror a Bruno collection, set the Bruno folder below —
                <code> .bru</code> files then update on every save.
              </p>
              <div className="flex items-center gap-2">
                <input
                  type="text"
                  value={newPath}
                  onChange={(e) => setNewPath(e.target.value)}
                  placeholder="D:\projects\orders-api"
                  className="min-w-0 flex-1 rounded border bg-background px-2 py-1 font-mono text-xs"
                  data-testid="linked-root-path-input"
                  aria-label="Folder path"
                />
                <button
                  onClick={() => void browse(setNewPath, "Select the project folder")}
                  className="flex shrink-0 items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
                  data-testid="linked-root-browse"
                >
                  <FolderOpen className="h-3 w-3" /> Browse…
                </button>
              </div>
              <input
                type="text"
                value={newName}
                onChange={(e) => setNewName(e.target.value)}
                placeholder="Display name (optional — defaults to the folder name)"
                className="w-full rounded border bg-background px-2 py-1 text-xs"
                data-testid="linked-root-name-input"
                aria-label="Display name"
              />
              <div className="flex items-center gap-2">
                <input
                  type="text"
                  value={brunoPath}
                  onChange={(e) => setBrunoPath(e.target.value)}
                  placeholder="Bruno collection folder (optional — enables .bru sync)"
                  className="min-w-0 flex-1 rounded border bg-background px-2 py-1 font-mono text-xs"
                  data-testid="linked-root-bruno-input"
                  aria-label="Bruno collection folder"
                />
                <button
                  onClick={() => void browse(setBrunoPath, "Select the Bruno collection folder")}
                  className="flex shrink-0 items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
                  data-testid="linked-root-bruno-browse"
                >
                  <FolderOpen className="h-3 w-3" /> Browse…
                </button>
              </div>
              <div className="flex justify-end gap-2 pt-1">
                <button
                  onClick={() => { setShowAddForm(false); setNewPath(""); setNewName(""); setBrunoPath(""); }}
                  className="rounded border px-3 py-1 text-xs hover:bg-accent"
                  data-testid="linked-root-add-cancel"
                >
                  Cancel
                </button>
                <button
                  onClick={handleAdd}
                  disabled={!newPath.trim() || busy}
                  className="rounded bg-primary px-3 py-1 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
                  data-testid="linked-root-add-submit"
                >
                  {addRoot.isPending ? "Linking…" : "Link folder"}
                </button>
              </div>
            </div>
          )}
        </div>

        <div className="flex justify-end gap-2 border-t px-4 py-3">
          <button
            onClick={onClose}
            className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
            data-testid="linked-projects-done"
          >
            Done
          </button>
        </div>
      </div>

      {unlinkTarget && (
        <ConfirmDialog
          message={
            `Unlink "${unlinkTarget.displayName}"?\n\n` +
            `Its files stay on disk in ${unlinkTarget.path} — only SwebKit's link is removed. ` +
            `The collections disappear from the tree until you link the folder again.`
          }
          confirmText="Unlink"
          onConfirm={() => handleUnlink(unlinkTarget)}
          onCancel={() => setUnlinkTarget(null)}
        />
      )}
    </div>
  );
}

interface LinkedRootRowProps {
  root: LinkedRootSummary;
  busy: boolean;
  renaming: boolean;
  renameValue: string;
  onRenameChange: (v: string) => void;
  onStartRename: () => void;
  onCommitRename: () => void;
  onCancelRename: () => void;
  onToggleEnabled: () => void;
  onUnlink: () => void;
}

function LinkedRootRow({
  root, busy, renaming, renameValue, onRenameChange, onStartRename,
  onCommitRename, onCancelRename, onToggleEnabled, onUnlink,
}: LinkedRootRowProps) {
  const hasErrors = root.diagnostics.length > 0;

  return (
    <div
      className={`space-y-1.5 rounded border p-3 ${root.isEnabled ? "" : "opacity-60"}`}
      data-testid={`linked-root-${root.id}`}
    >
      <div className="flex items-center gap-2">
        {renaming ? (
          <input
            type="text"
            value={renameValue}
            onChange={(e) => onRenameChange(e.target.value)}
            onBlur={onCommitRename}
            onKeyDown={(e) => {
              if (e.key === "Enter") onCommitRename();
              if (e.key === "Escape") onCancelRename();
            }}
            autoFocus
            className="min-w-0 flex-1 rounded border bg-background px-1.5 py-0.5 text-sm"
            data-testid={`linked-root-rename-input-${root.id}`}
          />
        ) : (
          <>
            <span className="min-w-0 flex-1 truncate text-sm font-medium" title={root.path}>
              {root.displayName}
            </span>
            {root.brunoSyncEnabled && root.brunoSyncFolderPath && (
              <span
                className="flex shrink-0 items-center gap-1 rounded bg-accent px-1.5 py-0.5 text-[10px] text-muted-foreground"
                title={`Two-way .bru sync with ${root.brunoSyncFolderPath}`}
                data-testid={`linked-root-bruno-badge-${root.id}`}
              >
                <Link2 className="h-3 w-3" /> Bruno
              </span>
            )}
            {root.isGitRepository && (
              <span
                className="flex shrink-0 items-center gap-1 rounded bg-accent px-1.5 py-0.5 font-mono text-[10px] text-muted-foreground"
                title={root.changedFileCount > 0
                  ? `${root.changedFileCount} uncommitted change(s) on ${root.branch ?? "unknown branch"}`
                  : `Clean working tree on ${root.branch ?? "unknown branch"}`}
                data-testid={`linked-root-git-badge-${root.id}`}
              >
                <GitBranch className="h-3 w-3" />
                {root.branch ?? "git"}
                {root.changedFileCount > 0 && ` ·${root.changedFileCount}`}
              </span>
            )}
          </>
        )}
      </div>

      <p className="truncate font-mono text-[11px] text-muted-foreground" title={root.path} data-testid={`linked-root-path-${root.id}`}>
        {root.path}
      </p>

      <div className="flex items-center gap-3 text-[11px] text-muted-foreground">
        {hasErrors ? (
          <span className="flex items-center gap-1" style={{ color: "var(--destructive)" }} data-testid={`linked-root-error-${root.id}`}>
            <AlertCircle className="h-3 w-3" />
            {root.diagnostics[0]}
            {root.diagnostics.length > 1 && ` (+${root.diagnostics.length - 1} more)`}
          </span>
        ) : (
          <span className="flex items-center gap-1" data-testid={`linked-root-status-${root.id}`}>
            <CheckCircle className="h-3 w-3" style={{ color: "var(--success)" }} />
            {root.collectionCount} collection{root.collectionCount === 1 ? "" : "s"}
            {root.environmentCount > 0 && ` · ${root.environmentCount} environment${root.environmentCount === 1 ? "" : "s"}`}
          </span>
        )}

        <span className="ml-auto flex items-center gap-1">
          <button
            onClick={onStartRename}
            disabled={busy}
            className="rounded p-1 hover:bg-accent disabled:opacity-50"
            title="Rename"
            data-testid={`linked-root-rename-${root.id}`}
          >
            <Pencil className="h-3 w-3" />
          </button>
          <button
            onClick={onToggleEnabled}
            disabled={busy}
            className="rounded border px-1.5 py-0.5 text-[10px] hover:bg-accent disabled:opacity-50"
            title={root.isEnabled ? "Hide this folder's collections without unlinking" : "Show this folder's collections again"}
            data-testid={`linked-root-toggle-${root.id}`}
          >
            {root.isEnabled ? "Disable" : "Enable"}
          </button>
          <button
            onClick={onUnlink}
            disabled={busy}
            className="rounded p-1 text-muted-foreground hover:bg-accent hover:text-destructive disabled:opacity-50"
            title="Unlink (files stay on disk)"
            data-testid={`linked-root-unlink-${root.id}`}
          >
            <Trash2 className="h-3 w-3" />
          </button>
        </span>
      </div>
    </div>
  );
}
