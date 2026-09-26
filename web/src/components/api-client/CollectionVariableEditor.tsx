import { useState } from "react";
import { X } from "lucide-react";
import { VariableList, type VariableListItem } from "./VariableList";
import { collectionVariableToListItem, listItemToCollectionVariable } from "@/lib/variable-utils";
import type { ApiCollection, CollectionVariable } from "@/lib/types";

interface CollectionVariableEditorProps {
  collection: ApiCollection;
  onSave: (variables: CollectionVariable[]) => void;
  onClose: () => void;
}

function toListItems(collection: ApiCollection): VariableListItem[] {
  return collection.variables?.map((v, i) => collectionVariableToListItem(v, `${collection.id}-${i}`)) ?? [];
}

export function CollectionVariableEditor({ collection, onSave, onClose }: CollectionVariableEditorProps) {
  const [variables, setVariables] = useState<VariableListItem[]>(() => toListItems(collection));

  // Saving closes the dialog while the store write is still in flight. Reopening
  // before it lands mounts this against the pre-save collection — and since the
  // initializer only runs once, the list stayed empty even after the fresh data
  // arrived, so the variables looked lost and saving again really would have lost
  // them. Re-sync when the stored variables change. Safe against clobbering an edit
  // in progress: the dialog closes on save, so while it is open this component is
  // the only writer and an identity change means the data genuinely moved.
  const [prevSource, setPrevSource] = useState({ id: collection.id, vars: collection.variables });
  if (prevSource.id !== collection.id || prevSource.vars !== collection.variables) {
    setPrevSource({ id: collection.id, vars: collection.variables });
    setVariables(toListItems(collection));
  }

  const handleSave = () => {
    onSave(variables.map(listItemToCollectionVariable).filter((v) => v.key.trim()));
    onClose();
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" data-testid="col-var-editor-overlay">
      {/* Wide enough for a real variable name beside its value, and capped so a
          collection with twenty of them scrolls instead of running off-screen. */}
      <div
        className="flex max-h-[80vh] w-[min(56rem,92vw)] flex-col rounded-lg border bg-card shadow-lg"
        data-testid="col-var-editor"
      >
        <div className="flex items-center justify-between border-b px-4 py-3">
          <h2 className="text-sm font-semibold">Collection Variables — {collection.name}</h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="min-h-0 flex-1 space-y-2 overflow-auto p-4">
          <VariableList
            variables={variables}
            keyVaults={[]}
            onChange={setVariables}
            emptyMessage="No collection variables. These are available to all requests in this collection."
            testIdPrefix="col-var"
          />
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
