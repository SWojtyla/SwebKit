import { useState } from "react";
import { X, FileText, Pencil, Save, Eye, Loader2, Check, AlertCircle } from "lucide-react";
import { useAksResourceYaml, useAksApplyYaml, useAksValidateYaml } from "@/lib/hooks";
import { useNotification } from "@/components/layout/NotificationSystem";
import { highlightYaml } from "@/lib/yamlHighlight";

interface YamlViewerProps {
  ns: string;
  kind: string;
  name: string;
  onClose: () => void;
}

export function YamlViewer({ ns, kind, name, onClose }: YamlViewerProps) {
  const { data: yaml, isLoading, error } = useAksResourceYaml(ns, kind, name);
  const applyMutation = useAksApplyYaml();
  const validateMutation = useAksValidateYaml();
  const { notify } = useNotification();
  const [copied, setCopied] = useState(false);
  const [editMode, setEditMode] = useState(false);
  const [editedYaml, setEditedYaml] = useState("");
  const [validationError, setValidationError] = useState<string | null>(null);

  const handleCopy = () => {
    navigator.clipboard.writeText(yaml ?? "");
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const toggleEdit = () => {
    if (editMode) {
      setEditedYaml("");
      setEditMode(false);
      setValidationError(null);
    } else {
      setEditedYaml(yaml ?? "");
      setEditMode(true);
      setValidationError(null);
    }
  };

  const handleValidate = () => {
    setValidationError(null);
    validateMutation.mutate(
      { ns, yaml: editedYaml },
      {
        onSuccess: (result) => {
          if (result.error) {
            setValidationError(result.error);
            notify("error", `YAML validation failed: ${result.error}`);
          } else {
            notify("success", "YAML is valid");
          }
        },
        onError: (err) => {
          const message = err instanceof Error ? err.message : String(err);
          setValidationError(message);
          notify("error", `YAML validation failed: ${message}`);
        },
      },
    );
  };

  const handleApply = () => {
    if (!window.confirm(`Apply ${kind}/${name} in ${ns}? This will update the cluster resource.`)) return;
    setValidationError(null);
    validateMutation.mutate(
      { ns, yaml: editedYaml },
      {
        onSuccess: (result) => {
          if (result.error) {
            setValidationError(result.error);
            notify("error", `YAML validation failed: ${result.error}`);
            return;
          }
          applyMutation.mutate(
            { ns, kind, name, yaml: editedYaml },
            {
              onSuccess: () => {
                notify("success", `${kind}/${name} applied successfully`);
                setEditMode(false);
                setEditedYaml("");
              },
              onError: (err) => {
                const message = err instanceof Error ? err.message : String(err);
                setValidationError(message);
                notify("error", `Failed to apply ${kind}/${name}: ${message}`);
              },
            },
          );
        },
        onError: (err) => {
          const message = err instanceof Error ? err.message : String(err);
          setValidationError(message);
          notify("error", `YAML validation failed: ${message}`);
        },
      },
    );
  };

  const isBusy = applyMutation.isPending || validateMutation.isPending;

  return (
    <div className="flex h-full flex-col" data-testid="yaml-viewer">
      <div className="flex items-center gap-2 border-b px-4 py-2">
        <FileText className="h-4 w-4" />
        <span className="text-sm font-medium">{kind}/{name}</span>
        <span className="text-xs text-muted-foreground">YAML</span>
        <div className="ml-auto flex items-center gap-2">
          {editMode && (
            <>
              <button
                onClick={handleValidate}
                disabled={isBusy || !editedYaml.trim()}
                className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                data-testid="yaml-validate"
              >
                {validateMutation.isPending ? (
                  <Loader2 className="h-3 w-3 animate-spin" />
                ) : (
                  <Check className="h-3 w-3" />
                )}
                Validate
              </button>
              <button
                onClick={handleApply}
                disabled={isBusy || !editedYaml.trim()}
                className="flex items-center gap-1 rounded border border-primary bg-primary/10 px-2 py-1 text-xs text-primary hover:bg-primary/20 disabled:opacity-50"
                data-testid="yaml-apply"
              >
                {applyMutation.isPending ? (
                  <Loader2 className="h-3 w-3 animate-spin" />
                ) : (
                  <Save className="h-3 w-3" />
                )}
                Apply
              </button>
            </>
          )}
          <button
            onClick={toggleEdit}
            className={`flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent ${editMode ? "border-primary text-primary" : ""}`}
            data-testid="yaml-edit-toggle"
          >
            {editMode ? <Eye className="h-3 w-3" /> : <Pencil className="h-3 w-3" />}
            {editMode ? "View" : "Edit"}
          </button>
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
          <div className="flex h-full flex-col gap-2">
            {validationError && (
              <div className="flex items-start gap-2 rounded border border-destructive bg-destructive/10 p-2 text-xs text-destructive" data-testid="yaml-validation-error">
                <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
                <pre className="whitespace-pre-wrap font-mono">{validationError}</pre>
              </div>
            )}
            <textarea
              value={editedYaml}
              onChange={(e) => setEditedYaml(e.target.value)}
              className="h-full w-full flex-1 bg-background text-foreground text-xs font-mono resize-none border-none outline-none"
              data-testid="yaml-editor"
              spellCheck={false}
            />
          </div>
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
