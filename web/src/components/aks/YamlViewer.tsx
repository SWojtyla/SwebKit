import { useState } from "react";
import { X, FileText, Pencil, Save, Eye } from "lucide-react";
import { useAksResourceYaml } from "@/lib/hooks";
import { highlightYaml } from "@/lib/yamlHighlight";

interface YamlViewerProps {
  ns: string;
  kind: string;
  name: string;
  onClose: () => void;
}

export function YamlViewer({ ns, kind, name, onClose }: YamlViewerProps) {
  const { data: yaml, isLoading, error } = useAksResourceYaml(ns, kind, name);
  const [copied, setCopied] = useState(false);
  const [editMode, setEditMode] = useState(false);
  const [editedYaml, setEditedYaml] = useState("");

  const handleCopy = () => {
    navigator.clipboard.writeText(yaml ?? "");
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="flex h-full flex-col" data-testid="yaml-viewer">
      <div className="flex items-center gap-2 border-b px-4 py-2">
        <FileText className="h-4 w-4" />
        <span className="text-sm font-medium">{kind}/{name}</span>
        <span className="text-xs text-muted-foreground">YAML</span>
        <div className="ml-auto flex items-center gap-2">
          <button
            onClick={() => {
              if (editMode) { setEditedYaml(""); setEditMode(false); }
              else { setEditedYaml(yaml ?? ""); setEditMode(true); }
            }}
            className={`flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent ${editMode ? "border-primary text-primary" : ""}`}
            data-testid="yaml-edit-toggle"
          >
            {editMode ? <Eye className="h-3 w-3" /> : <Pencil className="h-3 w-3" />}
            {editMode ? "View" : "Edit"}
          </button>
          {editMode && (
            <button
              disabled
              title="Coming soon — applying edited YAML needs a sidecar PUT endpoint"
              className="flex items-center gap-1 rounded border px-2 py-1 text-xs opacity-50 cursor-not-allowed"
              data-testid="yaml-apply"
            >
              <Save className="h-3 w-3" /> Apply (coming soon)
            </button>
          )}
          <button
            onClick={handleCopy}
            className="rounded border px-2 py-1 text-xs hover:bg-accent"
            data-testid="yaml-copy"
          >
            {copied ? "Copied!" : "Copy"}
          </button>
          <button onClick={onClose} className="rounded p-1 hover:bg-accent">
            <X className="h-4 w-4" />
          </button>
        </div>
      </div>
      <div className="flex-1 overflow-auto bg-card p-3">
        {isLoading ? (
          <div className="text-primary text-xs font-mono">Loading YAML...</div>
        ) : error ? (
          <div className="text-destructive text-xs font-mono" data-testid="yaml-error">
            Error: {error.message}
          </div>
        ) : editMode ? (
          <textarea
            value={editedYaml}
            onChange={(e) => setEditedYaml(e.target.value)}
            className="h-full w-full bg-background text-foreground text-xs font-mono resize-none border-none outline-none"
            data-testid="yaml-editor"
            spellCheck={false}
          />
        ) : (
          <pre
            className="yml-viewer whitespace-pre-wrap break-all text-xs font-mono text-foreground"
            data-testid="yaml-content"
            dangerouslySetInnerHTML={{ __html: highlightYaml(yaml ?? "", true) }}
          />
        )}
      </div>
    </div>
  );
}
