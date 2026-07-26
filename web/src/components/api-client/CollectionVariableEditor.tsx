import { useState } from "react";
import { Trash2, X } from "lucide-react";
import type { ApiCollection, CollectionVariable } from "@/lib/types";

interface CollectionVariableEditorProps {
  collection: ApiCollection;
  onSave: (variables: CollectionVariable[]) => void;
  onClose: () => void;
}

export function CollectionVariableEditor({ collection, onSave, onClose }: CollectionVariableEditorProps) {
  const [variables, setVariables] = useState<CollectionVariable[]>(collection.variables ?? []);

  const addVariable = () =>
    setVariables([...variables, { key: "", value: "", isEnabled: true }]);

  const updateVariable = (index: number, patch: Partial<CollectionVariable>) =>
    setVariables(variables.map((v, i) => (i === index ? { ...v, ...patch } : v)));

  const removeVariable = (index: number) =>
    setVariables(variables.filter((_, i) => i !== index));

  const handleSave = () => {
    onSave(variables.filter((v) => v.key.trim()));
    onClose();
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" data-testid="col-var-editor-overlay">
      <div className="w-[500px] rounded-lg border bg-card shadow-lg" data-testid="col-var-editor">
        <div className="flex items-center justify-between border-b px-4 py-3">
          <h2 className="text-sm font-semibold">
            Collection Variables — {collection.name}
          </h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="p-4 space-y-2">
          {variables.length === 0 && (
            <div className="text-xs text-muted-foreground py-2">
              No collection variables. These are available to all requests in this collection.
            </div>
          )}
          {variables.map((v, i) => (
            <div key={i} className="flex items-center gap-2" data-testid={`col-var-row-${i}`}>
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
                data-testid={`col-var-key-${i}`}
              />
              <span className="text-xs text-muted-foreground">=</span>
              <input
                type="text"
                value={v.value ?? ""}
                onChange={(e) => updateVariable(i, { value: e.target.value })}
                placeholder="Value"
                className="flex-1 rounded border bg-background px-2 py-1 text-sm font-mono"
                data-testid={`col-var-value-${i}`}
              />
              <button
                className="p-1 text-destructive"
                onClick={() => removeVariable(i)}
                data-testid={`col-var-remove-${i}`}
              >
                <Trash2 className="h-3 w-3" />
              </button>
            </div>
          ))}
          <button
            onClick={addVariable}
            className="text-xs text-primary hover:underline"
            data-testid="col-var-add"
          >
            + Add variable
          </button>
        </div>

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
            data-testid="col-var-save"
          >
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
