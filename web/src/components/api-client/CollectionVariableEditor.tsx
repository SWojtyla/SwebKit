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

export function CollectionVariableEditor({ collection, onSave, onClose }: CollectionVariableEditorProps) {
  const [variables, setVariables] = useState<VariableListItem[]>(
    collection.variables?.map((v, i) => collectionVariableToListItem(v, `${collection.id}-${i}`)) ?? []
  );

  const handleSave = () => {
    onSave(variables.map(listItemToCollectionVariable).filter((v) => v.key.trim()));
    onClose();
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" data-testid="col-var-editor-overlay">
      <div className="w-[500px] rounded-lg border bg-card shadow-lg" data-testid="col-var-editor">
        <div className="flex items-center justify-between border-b px-4 py-3">
          <h2 className="text-sm font-semibold">Collection Variables — {collection.name}</h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="p-4 space-y-2">
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
