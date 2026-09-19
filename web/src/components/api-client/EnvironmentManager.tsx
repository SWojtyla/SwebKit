import { useEffect, useRef, useState } from "react";
import { Plus, Trash2, Globe, Folder, X, Check, FolderGit2, HardDrive } from "lucide-react";
import { loadViewPreference, saveViewPreference } from "@/lib/stores/panel-preferences";
import { ResizablePanels } from "@/components/ui/ResizablePanels";
import { VariableList, type VariableListItem } from "./VariableList";
import {
  environmentVariableToListItem,
  listItemToEnvironmentVariable,
} from "@/lib/variable-utils";
import type { ApiEnvironment, ApiCollection, LinkedRootSummary } from "@/lib/types";
import { useProfile, useLinkedRoots } from "@/lib/hooks";
import { ConfirmDialog } from "./Dialogs";

interface EnvironmentManagerProps {
  environments: ApiEnvironment[];
  collections: ApiCollection[];
  activeEnvironmentId: string | null;
  /** Read-only, for showing which project environment is active in each group. */
  activeEnvironmentIdByCollection?: Record<string, string>;
  onSave: (environments: ApiEnvironment[], activeEnvironmentId: string | null) => void;
  onClose: () => void;
}

const DEFAULT_SIZE = { width: 1040, height: 720 };
const MIN_SIZE = { width: 640, height: 420 };

/// A remembered size outlives the screen it was chosen on. Without this, a size
/// saved on a large monitor reopens off the edge of a laptop display with the
/// resize grip — the only way to shrink it again — out of reach. Never goes below
/// `MIN_SIZE`, which the inner panel minimums are sized against.
function fitToViewport(size: { width: number; height: number }) {
  if (typeof window === "undefined") return size;
  return {
    width: Math.max(MIN_SIZE.width, Math.min(size.width, window.innerWidth - 48)),
    height: Math.max(MIN_SIZE.height, Math.min(size.height, window.innerHeight - 48)),
  };
}

export function EnvironmentManager({
  environments,
  collections,
  activeEnvironmentId,
  activeEnvironmentIdByCollection = {},
  onSave,
  onClose,
}: EnvironmentManagerProps) {
  const [editingEnv, setEditingEnv] = useState<ApiEnvironment | null>(null);
  const [envList, setEnvList] = useState<ApiEnvironment[]>(environments);
  const { data: linkedRoots = [] } = useLinkedRoots();
  const [activeId, setActiveId] = useState<string | null>(activeEnvironmentId);
  const [size, setSize] = useState(() =>
    fitToViewport(loadViewPreference("env-manager-size", DEFAULT_SIZE)),
  );
  // Every other destructive flow in this feature confirms first (unit 4.3) —
  // environment delete previously had no confirmation at all.
  const [deleteConfirm, setDeleteConfirm] = useState<{ id: string; name: string } | null>(null);

  const dialogRef = useRef<HTMLDivElement>(null);
  const isResizingRef = useRef(false);
  const startSizeRef = useRef({ width: 0, height: 0 });
  const startMouseRef = useRef({ x: 0, y: 0 });

  useEffect(() => {
    const el = dialogRef.current;
    if (!el) return;
    const obs = new ResizeObserver(() => {
      const width = el.offsetWidth;
      const height = el.offsetHeight;
      if (width >= MIN_SIZE.width && height >= MIN_SIZE.height) {
        saveViewPreference("env-manager-size", { width: Math.round(width), height: Math.round(height) });
      }
    });
    obs.observe(el);
    return () => obs.disconnect();
  }, []);

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  useEffect(() => {
    const onMove = (e: PointerEvent) => {
      if (!isResizingRef.current) return;
      const deltaX = e.clientX - startMouseRef.current.x;
      const deltaY = e.clientY - startMouseRef.current.y;
      const next = {
        width: Math.max(MIN_SIZE.width, Math.round(startSizeRef.current.width + deltaX)),
        height: Math.max(MIN_SIZE.height, Math.round(startSizeRef.current.height + deltaY)),
      };
      setSize(next);
    };
    const onUp = () => {
      if (!isResizingRef.current) return;
      isResizingRef.current = false;
      document.body.style.userSelect = "";
      document.body.style.cursor = "";
      if (dialogRef.current) {
        saveViewPreference("env-manager-size", {
          width: dialogRef.current.offsetWidth,
          height: dialogRef.current.offsetHeight,
        });
      }
    };
    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp);
    return () => {
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
    };
  }, []);

  const startResize = (e: React.PointerEvent) => {
    e.preventDefault();
    isResizingRef.current = true;
    startMouseRef.current = { x: e.clientX, y: e.clientY };
    startSizeRef.current = { width: size.width, height: size.height };
    document.body.style.userSelect = "none";
    document.body.style.cursor = "se-resize";
  };

  const addEnvironment = () => {
    const env: ApiEnvironment = {
      id: crypto.randomUUID().replace(/-/g, "").slice(0, 32),
      name: "New Environment",
      collectionId: null,
      variables: [],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };
    setEnvList([...envList, env]);
    setEditingEnv(env);
  };

  const requestDeleteEnvironment = (id: string) => {
    const env = envList.find((e) => e.id === id);
    setDeleteConfirm({ id, name: env?.name ?? "this environment" });
  };

  const confirmDeleteEnvironment = () => {
    if (!deleteConfirm) return;
    const { id } = deleteConfirm;
    setEnvList((prev) => prev.filter((e) => e.id !== id));
    if (activeId === id) setActiveId(null);
    if (editingEnv?.id === id) setEditingEnv(null);
    setDeleteConfirm(null);
  };

  const updateEnvironment = (updated: ApiEnvironment) => {
    setEnvList(envList.map((e) => (e.id === updated.id ? updated : e)));
    setEditingEnv(updated);
  };

  const handleSave = () => {
    onSave(envList, activeId);
    onClose();
  };

  return (
    <>
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
      data-testid="env-manager-overlay"
    >
      <div
        ref={dialogRef}
        className="relative flex flex-col overflow-hidden rounded-lg border bg-card shadow-lg"
        style={{ width: size.width, height: size.height, minWidth: MIN_SIZE.width, minHeight: MIN_SIZE.height }}
        data-testid="env-manager"
        role="dialog"
        aria-modal="true"
        aria-label="Environment Manager"
        tabIndex={-1}
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b px-4 py-3">
          <h2 className="text-sm font-semibold">Environment Manager</h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground" data-testid="env-manager-close">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="flex flex-1 overflow-hidden">
          <ResizablePanels
            initialWidths={[220, "1fr"]}
            minWidths={[180, 320]}
            storageKey="env-manager-panels"
            panelLabels={["environments", "editor"]}
            className="flex-1"
          >
            <EnvironmentList
              environments={envList}
              collections={collections}
              activeId={activeId}
              activeByCollection={activeEnvironmentIdByCollection}
              editingEnv={editingEnv}
              onAdd={addEnvironment}
              onSelect={setEditingEnv}
              onDelete={requestDeleteEnvironment}
            />
            <div className="flex h-full w-full flex-col overflow-auto p-4">
              {editingEnv ? (
                <EnvironmentEditor
                  key={editingEnv.id}
                  environment={editingEnv}
                  collections={collections}
                  linkedRoots={linkedRoots}
                  isActive={activeId === editingEnv.id}
                  onChange={updateEnvironment}
                  onSetActive={() => setActiveId(activeId === editingEnv.id ? null : editingEnv.id)}
                />
              ) : (
                <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
                  Select or create an environment to edit.
                </div>
              )}
            </div>
          </ResizablePanels>
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-2 border-t px-4 py-3">
          <button
            onClick={onClose}
            className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
          >
            Cancel
          </button>
          <button
            onClick={handleSave}
            className="rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90"
            data-testid="env-save-all"
          >
            Save All
          </button>
        </div>

        {/* Resize handle */}
        <div
          className="absolute bottom-0 right-0 h-4 w-4 cursor-se-resize"
          aria-label="Resize dialog"
          data-testid="env-manager-resize-handle"
          onPointerDown={startResize}
        />
      </div>
    </div>
    {deleteConfirm && (
      <ConfirmDialog
        message={`Delete environment "${deleteConfirm.name}"? This cannot be undone.`}
        confirmText="Delete"
        onConfirm={confirmDeleteEnvironment}
        onCancel={() => setDeleteConfirm(null)}
      />
    )}
    </>
  );
}

interface EnvironmentListProps {
  environments: ApiEnvironment[];
  activeId: string | null;
  editingEnv: ApiEnvironment | null;
  collections: ApiCollection[];
  /** Active collection-scoped environment per collection id. */
  activeByCollection: Record<string, string>;
  onAdd: () => void;
  onSelect: (env: ApiEnvironment) => void;
  onDelete: (id: string) => void;
}

/// Grouped by scope rather than listed flat.
///
/// A flat list of twenty-odd names distinguished only by a small folder-or-globe icon gave
/// no way to answer the two questions that actually matter — which environments apply to
/// the collection I am working in, and which one of them is active. Grouping answers both,
/// and it surfaces environments scoped to a deleted collection, which were previously
/// invisible and unreachable.
function EnvironmentList({
  environments,
  collections,
  activeId,
  activeByCollection,
  editingEnv,
  onAdd,
  onSelect,
  onDelete,
}: EnvironmentListProps) {
  const groups: { key: string; label: string; icon: "globe" | "folder"; items: ApiEnvironment[]; activeId: string | null }[] = [];

  const globals = environments.filter((e) => e.collectionId === null);
  if (globals.length > 0) {
    groups.push({ key: "global", label: "Global — applies everywhere", icon: "globe", items: globals, activeId });
  }

  for (const collection of collections) {
    const items = environments.filter((e) => e.collectionId === collection.id);
    if (items.length > 0) {
      groups.push({
        key: collection.id,
        label: collection.name,
        icon: "folder",
        items,
        activeId: activeByCollection[collection.id] ?? null,
      });
    }
  }

  const knownCollectionIds = new Set(collections.map((c) => c.id));
  const orphans = environments.filter(
    (e) => e.collectionId !== null && !knownCollectionIds.has(e.collectionId),
  );
  if (orphans.length > 0) {
    groups.push({
      key: "orphans",
      label: "Scoped to a collection that no longer exists",
      icon: "folder",
      items: orphans,
      activeId: null,
    });
  }

  return (
    <div className="flex h-full w-full flex-col border-r bg-card" data-testid="env-list">
      <div className="flex items-center justify-between border-b px-3 py-2">
        <span className="text-xs font-medium text-muted-foreground">Environments</span>
        <button
          onClick={onAdd}
          className="rounded p-1 hover:bg-accent"
          title="New environment"
          data-testid="env-add-button"
        >
          <Plus className="h-3.5 w-3.5" />
        </button>
      </div>
      <div className="flex-1 overflow-auto">
        {environments.length === 0 && (
          <div className="p-3 text-xs text-muted-foreground">
            No environments. Click + to create one.
          </div>
        )}
        {groups.map((group) => (
          <div key={group.key}>
            <div className="flex items-center gap-1.5 bg-muted/40 px-3 py-1 text-[11px] font-medium uppercase tracking-wide text-muted-foreground">
              {group.icon === "globe" ? (
                <Globe className="h-3 w-3 shrink-0" />
              ) : (
                <Folder className="h-3 w-3 shrink-0" />
              )}
              <span className="truncate">{group.label}</span>
            </div>
            {group.items.map((env) => (
              <div
                key={env.id}
                className={`group flex cursor-pointer items-center gap-2 px-3 py-1.5 pl-6 text-sm ${
                  editingEnv?.id === env.id ? "bg-accent" : "hover:bg-accent/50"
                }`}
                onClick={() => onSelect(env)}
                data-testid={`env-item-${env.id}`}
              >
                <span className="flex-1 truncate">{env.name}</span>
                {group.activeId === env.id && (
                  <span
                    className="flex items-center gap-1 text-[11px]"
                    style={{ color: "var(--success)" }}
                    data-testid={`env-active-${env.id}`}
                  >
                    <Check className="h-3 w-3" /> Active
                  </span>
                )}
                <button
                  className="p-0.5 opacity-0 group-hover:opacity-100 hover:text-destructive"
                  onClick={(e) => { e.stopPropagation(); onDelete(env.id); }}
                  data-testid={`env-delete-${env.id}`}
                >
                  <Trash2 className="h-3 w-3" />
                </button>
              </div>
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}

interface EnvironmentEditorProps {
  environment: ApiEnvironment;
  collections: ApiCollection[];
  linkedRoots: LinkedRootSummary[];
  isActive: boolean;
  onChange: (env: ApiEnvironment) => void;
  onSetActive: () => void;
}

function EnvironmentEditor({ environment, collections, linkedRoots, isActive, onChange, onSetActive }: EnvironmentEditorProps) {
  const { data: profile } = useProfile();
  const keyVaults = profile?.config.keyVaults ?? [];

  const [variables, setVariables] = useState<VariableListItem[]>(() =>
    environment.variables.map((v, i) => environmentVariableToListItem(v, `${environment.id}-${i}`))
  );

  const updateVariables = (next: VariableListItem[]) => {
    setVariables(next);
    const updated: ApiEnvironment = {
      ...environment,
      variables: next.map(listItemToEnvironmentVariable),
      updatedAt: new Date().toISOString(),
    };
    onChange(updated);
  };

  const setName = (name: string) => onChange({ ...environment, name });
  // Scoping to a linked collection moves the environment's file into that
  // collection's folder — the server auto-links on `linkedRootId`.
  const setScope = (collectionId: string | null) => {
    const collection = collectionId ? collections.find((c) => c.id === collectionId) : null;
    onChange({
      ...environment,
      collectionId,
      linkedRootId: collection?.linkedRootId ?? environment.linkedRootId ?? null,
    });
  };
  const setStorage = (linkedRootId: string | null) => onChange({ ...environment, linkedRootId });

  const scopedCollection = environment.collectionId
    ? collections.find((c) => c.id === environment.collectionId)
    : null;
  const scopedRoot = scopedCollection?.linkedRootId
    ? linkedRoots.find((r) => r.id === scopedCollection.linkedRootId)
    : null;

  return (
    <div data-testid="env-editor" className="space-y-4">
      <div>
        <label className="mb-1 block text-xs font-medium text-muted-foreground">Name</label>
        <input
          type="text"
          data-testid="env-name-input"
          value={environment.name}
          onChange={(e) => setName(e.target.value)}
          className="w-full rounded border bg-background px-3 py-1.5 text-sm"
        />
      </div>

      <div>
        <label className="mb-1 block text-xs font-medium text-muted-foreground">Scope</label>
        <select
          data-testid="env-scope-select"
          value={environment.collectionId ?? ""}
          onChange={(e) => setScope(e.target.value || null)}
          className="w-full rounded border bg-background px-3 py-1.5 text-sm"
        >
          <option value="">Global (all collections)</option>
          {collections.map((c) => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </select>
      </div>

      {/* Where the environment file lives. An environment scoped to a linked
          collection always lives in that collection's folder — the scope decides.
          Only unscoped/global environments get a free choice. */}
      <div>
        <label className="mb-1 block text-xs font-medium text-muted-foreground">Store in</label>
        {scopedRoot ? (
          <p className="flex items-center gap-1.5 rounded border bg-muted/40 px-3 py-1.5 text-xs text-muted-foreground" data-testid="env-storage-linked">
            <FolderGit2 className="h-3.5 w-3.5 shrink-0" />
            Saved in <span className="font-medium text-foreground">{scopedRoot.displayName}</span>
            <span className="truncate font-mono" title={scopedRoot.path}>({scopedRoot.path})</span>
            — follows the collection it is scoped to.
          </p>
        ) : (
          <>
            <select
              data-testid="env-storage-select"
              value={environment.linkedRootId ?? ""}
              onChange={(e) => setStorage(e.target.value || null)}
              className="w-full rounded border bg-background px-3 py-1.5 text-sm"
            >
              <option value="">App storage (private to SwebKit)</option>
              {/* Disabled roots stay selectable only while already assigned — an
                  option must exist for the current value or the select lies. */}
              {linkedRoots.map((r) => (
                <option key={r.id} value={r.id} disabled={!r.isEnabled && environment.linkedRootId !== r.id}>
                  {r.displayName} — {r.path}{r.isEnabled ? "" : " (disabled)"}
                </option>
              ))}
            </select>
            <p className="mt-1 flex items-center gap-1 text-[11px] text-muted-foreground">
              {environment.linkedRootId ? (
                <>
                  <FolderGit2 className="h-3 w-3 shrink-0" />
                  Stored as a <code>.swebenv.json</code> file inside the linked folder.
                </>
              ) : (
                <>
                  <HardDrive className="h-3 w-3 shrink-0" />
                  Stored in SwebKit's own storage — not synced to any folder.
                </>
              )}
            </p>
          </>
        )}
      </div>

      <div>
        <button
          onClick={onSetActive}
          className={`rounded border px-3 py-1.5 text-xs ${isActive ? "" : "hover:bg-accent"}`}
          style={
            isActive
              ? {
                  color: "var(--success)",
                  backgroundColor: "color-mix(in oklch, var(--success) 10%, transparent)",
                  borderColor: "color-mix(in oklch, var(--success) 30%, transparent)",
                }
              : undefined
          }
          data-testid="env-toggle-active"
        >
          {isActive ? "✓ Active environment" : "Set as active"}
        </button>
      </div>

      <div>
        <label className="mb-1 block text-xs font-medium text-muted-foreground">Variables</label>
        <VariableList
          variables={variables}
          keyVaults={keyVaults}
          onChange={updateVariables}
          supportsKeyVault
          supportsCredentialStore
          emptyMessage="No variables defined."
          testIdPrefix="env-var"
          addButtonTestId="env-add-variable"
        />
      </div>
    </div>
  );
}
