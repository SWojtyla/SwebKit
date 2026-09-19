import { useState } from "react";
import { X, FolderGit2, HardDrive } from "lucide-react";
import type { LinkedRootSummary } from "@/lib/types";

interface NewCollectionDialogProps {
  roots: LinkedRootSummary[];
  onConfirm: (name: string, linkedRootId: string | null) => void;
  onCancel: () => void;
  /** Opens the project-folder manager so the user can link a folder first. */
  onManageProjects: () => void;
}

/**
 * "New Collection" with a storage choice: app storage (private collections.json)
 * or one of the linked project folders. The select only appears when linked roots
 * exist — otherwise the dialog collapses to name-only.
 */
export function NewCollectionDialog({ roots, onConfirm, onCancel, onManageProjects }: NewCollectionDialogProps) {
  const [name, setName] = useState("");
  const [linkedRootId, setLinkedRootId] = useState<string | null>(null);
  const enabledRoots = roots.filter((r) => r.isEnabled);

  const submit = () => {
    const trimmed = name.trim();
    if (trimmed) onConfirm(trimmed, linkedRootId);
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
      data-testid="new-collection-overlay"
      onKeyDown={(e) => { if (e.key === "Escape") { e.stopPropagation(); onCancel(); } }}
    >
      <div
        className="w-[420px] rounded-lg border bg-card shadow-lg"
        role="dialog"
        aria-modal="true"
        aria-label="New collection"
        data-testid="new-collection-dialog"
      >
        <div className="flex items-center justify-between border-b px-4 py-3">
          <h2 className="text-sm font-semibold">New Collection</h2>
          <button onClick={onCancel} className="text-muted-foreground hover:text-foreground" data-testid="new-collection-close" aria-label="Close">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="space-y-3 p-4">
          <div>
            <label htmlFor="new-collection-name" className="mb-1 block text-xs text-muted-foreground">
              Collection name
            </label>
            <input
              id="new-collection-name"
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") submit(); }}
              autoFocus
              className="w-full rounded border bg-background px-2 py-1.5 text-sm"
              data-testid="new-collection-name"
            />
          </div>

          <div>
            <label htmlFor="new-collection-storage" className="mb-1 block text-xs text-muted-foreground">
              Store in
            </label>
            <select
              id="new-collection-storage"
              value={linkedRootId ?? ""}
              onChange={(e) => setLinkedRootId(e.target.value || null)}
              className="w-full rounded border bg-background px-2 py-1.5 text-sm"
              data-testid="new-collection-storage"
            >
              <option value="">App storage (private to SwebKit)</option>
              {enabledRoots.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.displayName} — {r.path}
                </option>
              ))}
            </select>
            <p className="mt-1 flex items-start gap-1 text-[11px] text-muted-foreground">
              {linkedRootId ? (
                <>
                  <FolderGit2 className="mt-0.5 h-3 w-3 shrink-0" />
                  Saved as files under <code>.swebkit-api/</code> in the linked folder — one file per request, ready for Git.
                </>
              ) : (
                <>
                  <HardDrive className="mt-0.5 h-3 w-3 shrink-0" />
                  Kept inside SwebKit's own storage — not visible to Git or other tools.
                </>
              )}
            </p>
          </div>

          {enabledRoots.length === 0 && (
            <button
              onClick={onManageProjects}
              className="flex w-full items-center justify-center gap-1 rounded border border-dashed px-2 py-1.5 text-[11px] text-muted-foreground hover:bg-accent hover:text-foreground"
              data-testid="new-collection-manage-folders"
            >
              <FolderGit2 className="h-3 w-3" />
              Link a folder to store collections as files (Git-friendly)…
            </button>
          )}
        </div>

        <div className="flex justify-end gap-2 border-t px-4 py-3">
          <button
            onClick={onCancel}
            className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
            data-testid="new-collection-cancel"
          >
            Cancel
          </button>
          <button
            onClick={submit}
            disabled={!name.trim()}
            className="rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
            data-testid="new-collection-create"
          >
            Create
          </button>
        </div>
      </div>
    </div>
  );
}
