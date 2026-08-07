import { useEffect, useRef, useState } from "react";
import { Plus, Trash2, Globe, Folder, X, Check } from "lucide-react";
import { loadViewPreference, saveViewPreference } from "@/lib/stores/panel-preferences";
import { ResizablePanels } from "@/components/ui/ResizablePanels";
import { VariableList, type VariableListItem } from "./VariableList";
import {
  environmentVariableToListItem,
  listItemToEnvironmentVariable,
} from "@/lib/variable-utils";
import type { ApiEnvironment, ApiCollection } from "@/lib/types";
import { useProfile } from "@/lib/hooks";

interface EnvironmentManagerProps {
  environments: ApiEnvironment[];
  collections: ApiCollection[];
  activeEnvironmentId: string | null;
  onSave: (environments: ApiEnvironment[], activeEnvironmentId: string | null) => void;
  onClose: () => void;
}

const DEFAULT_SIZE = { width: 800, height: 600 };
const MIN_SIZE = { width: 640, height: 420 };

export function EnvironmentManager({
  environments,
  collections,
  activeEnvironmentId,
  onSave,
  onClose,
}: EnvironmentManagerProps) {
  const [editingEnv, setEditingEnv] = useState<ApiEnvironment | null>(null);
  const [envList, setEnvList] = useState<ApiEnvironment[]>(environments);
  const [activeId, setActiveId] = useState<string | null>(activeEnvironmentId);
  const [size, setSize] = useState(() => loadViewPreference("env-manager-size", DEFAULT_SIZE));

  const dialogRef = useRef<HTMLDivElement>(null);
  const isResizingRef = useRef(false);
  const startSizeRef = useRef({ width: 0, height: 0 });
  const startMouseRef = useRef({ x: 0, y: 0 });

  useEffect(() => {
    const el = dialogRef.current;
    if (!el) return;
    const obs = new ResizeObserver((entries) => {
      const { width, height } = entries[0].contentRect;
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
          width: dialogRef.current.clientWidth,
          height: dialogRef.current.clientHeight,
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

  const deleteEnvironment = (id: string) => {
    setEnvList(envList.filter((e) => e.id !== id));
    if (activeId === id) setActiveId(null);
    if (editingEnv?.id === id) setEditingEnv(null);
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
              activeId={activeId}
              editingEnv={editingEnv}
              onAdd={addEnvironment}
              onSelect={setEditingEnv}
              onDelete={deleteEnvironment}
            />
            <div className="flex h-full w-full flex-col overflow-auto p-4">
              {editingEnv ? (
                <EnvironmentEditor
                  key={editingEnv.id}
                  environment={editingEnv}
                  collections={collections}
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
  );
}

interface EnvironmentListProps {
  environments: ApiEnvironment[];
  activeId: string | null;
  editingEnv: ApiEnvironment | null;
  onAdd: () => void;
  onSelect: (env: ApiEnvironment) => void;
  onDelete: (id: string) => void;
}

function EnvironmentList({ environments, activeId, editingEnv, onAdd, onSelect, onDelete }: EnvironmentListProps) {
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
        {environments.map((env) => (
          <div
            key={env.id}
            className={`group flex cursor-pointer items-center gap-2 px-3 py-1.5 text-sm ${
              editingEnv?.id === env.id ? "bg-accent" : "hover:bg-accent/50"
            }`}
            onClick={() => onSelect(env)}
            data-testid={`env-item-${env.id}`}
          >
            {env.collectionId ? (
              <Folder className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
            ) : (
              <Globe className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
            )}
            <span className="flex-1 truncate">{env.name}</span>
            {activeId === env.id && (
              <Check className="h-3 w-3" style={{ color: "var(--success)" }} data-testid={`env-active-${env.id}`} />
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
    </div>
  );
}

interface EnvironmentEditorProps {
  environment: ApiEnvironment;
  collections: ApiCollection[];
  isActive: boolean;
  onChange: (env: ApiEnvironment) => void;
  onSetActive: () => void;
}

function EnvironmentEditor({ environment, collections, isActive, onChange, onSetActive }: EnvironmentEditorProps) {
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
  const setScope = (collectionId: string | null) => onChange({ ...environment, collectionId });

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
