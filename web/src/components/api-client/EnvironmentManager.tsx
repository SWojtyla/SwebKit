import { useState } from "react";
import { Plus, Trash2, Globe, Folder, X, Check } from "lucide-react";
import type { ApiEnvironment, EnvironmentVariable, ApiCollection } from "@/lib/types";

interface EnvironmentManagerProps {
  environments: ApiEnvironment[];
  collections: ApiCollection[];
  activeEnvironmentId: string | null;
  onSave: (environments: ApiEnvironment[], activeEnvironmentId: string | null) => void;
  onClose: () => void;
}

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
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" data-testid="env-manager-overlay">
      <div className="flex h-[600px] w-[800px] flex-col rounded-lg border bg-card shadow-lg" data-testid="env-manager">
        {/* Header */}
        <div className="flex items-center justify-between border-b px-4 py-3">
          <h2 className="text-sm font-semibold">Environment Manager</h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="flex flex-1 overflow-hidden">
          {/* Environment list */}
          <div className="w-56 border-r overflow-auto" data-testid="env-list">
            <div className="flex items-center justify-between px-3 py-2 border-b">
              <span className="text-xs font-medium text-muted-foreground">Environments</span>
              <button
                onClick={addEnvironment}
                className="rounded p-1 hover:bg-accent"
                title="New environment"
                data-testid="env-add-button"
              >
                <Plus className="h-3.5 w-3.5" />
              </button>
            </div>
            {envList.length === 0 && (
              <div className="p-3 text-xs text-muted-foreground">
                No environments. Click + to create one.
              </div>
            )}
            {envList.map((env) => (
              <div
                key={env.id}
                className={`group flex cursor-pointer items-center gap-2 px-3 py-1.5 text-sm ${
                  editingEnv?.id === env.id ? "bg-accent" : "hover:bg-accent/50"
                }`}
                onClick={() => setEditingEnv(env)}
                data-testid={`env-item-${env.id}`}
              >
                {env.collectionId ? (
                  <Folder className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                ) : (
                  <Globe className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                )}
                <span className="flex-1 truncate">{env.name}</span>
                {activeId === env.id && (
                  <Check
                    className="h-3 w-3"
                    style={{ color: "var(--success)" }}
                    data-testid={`env-active-${env.id}`}
                  />
                )}
                <button
                  className="p-0.5 opacity-0 group-hover:opacity-100 hover:text-destructive"
                  onClick={(e) => { e.stopPropagation(); deleteEnvironment(env.id); }}
                  data-testid={`env-delete-${env.id}`}
                >
                  <Trash2 className="h-3 w-3" />
                </button>
              </div>
            ))}
          </div>

          {/* Editor */}
          <div className="flex-1 overflow-auto p-4">
            {editingEnv ? (
              <EnvironmentEditor
                key={editingEnv.id}
                environment={editingEnv}
                collections={collections}
                isActive={activeId === editingEnv.id}
                onChange={updateEnvironment}
                onSetActive={() => setActiveId(
                  activeId === editingEnv.id ? null : editingEnv.id
                )}
              />
            ) : (
              <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
                Select or create an environment to edit.
              </div>
            )}
          </div>
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
      </div>
    </div>
  );
}

// ── Inline environment editor ───────────────────────────────────────────────

interface EnvironmentEditorProps {
  environment: ApiEnvironment;
  collections: ApiCollection[];
  isActive: boolean;
  onChange: (env: ApiEnvironment) => void;
  onSetActive: () => void;
}

function EnvironmentEditor({ environment, collections, isActive, onChange, onSetActive }: EnvironmentEditorProps) {
  const setName = (name: string) => onChange({ ...environment, name });
  const setScope = (collectionId: string | null) => onChange({ ...environment, collectionId });

  const addVariable = () =>
    onChange({
      ...environment,
      variables: [...environment.variables, { key: "", value: "", secretSource: "Plain", credentialKey: null, keyVaultName: null, isEnabled: true }],
    });

  const updateVariable = (index: number, patch: Partial<EnvironmentVariable>) =>
    onChange({
      ...environment,
      variables: environment.variables.map((v, i) => (i === index ? { ...v, ...patch } : v)),
    });

  const removeVariable = (index: number) =>
    onChange({
      ...environment,
      variables: environment.variables.filter((_, i) => i !== index),
    });

  return (
    <div data-testid="env-editor" className="space-y-4">
      {/* Name */}
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

      {/* Scope */}
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

      {/* Set active */}
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

      {/* Variables */}
      <div>
        <div className="mb-2 flex items-center justify-between">
          <span className="text-xs font-medium text-muted-foreground">Variables</span>
          <button
            onClick={addVariable}
            className="text-xs text-primary hover:underline"
            data-testid="env-add-variable"
          >
            + Add variable
          </button>
        </div>
        {environment.variables.length === 0 && (
          <div className="text-xs text-muted-foreground py-2">No variables defined.</div>
        )}
        {environment.variables.map((v, i) => (
          <div key={i} className="mb-1 flex items-center gap-2" data-testid={`env-var-row-${i}`}>
            <input
              type="checkbox"
              checked={v.isEnabled}
              onChange={(e) => updateVariable(i, { isEnabled: e.target.checked })}
            />
            <input
              type="text"
              value={v.key}
              onChange={(e) => updateVariable(i, { key: e.target.value })}
              placeholder="Key"
              className="w-32 rounded border bg-background px-2 py-1 text-sm font-mono"
              data-testid={`env-var-key-${i}`}
            />
            <span className="text-xs text-muted-foreground">=</span>
            <input
              type="text"
              value={v.value ?? ""}
              onChange={(e) => updateVariable(i, { value: e.target.value })}
              placeholder="Value"
              className="flex-1 rounded border bg-background px-2 py-1 text-sm font-mono"
              data-testid={`env-var-value-${i}`}
            />
            <button
              className="p-1 text-destructive"
              onClick={() => removeVariable(i)}
              data-testid={`env-var-remove-${i}`}
            >
              <Trash2 className="h-3 w-3" />
            </button>
          </div>
        ))}
      </div>
    </div>
  );
}
