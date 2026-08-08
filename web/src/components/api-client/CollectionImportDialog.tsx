import { useState } from "react";
import { Upload, FolderTree, FileJson, X, CheckCircle, AlertCircle } from "lucide-react";
import { useImportCollection, useDemoMode } from "@/lib/hooks";
import { useNotification } from "@/components/layout/NotificationSystem";
import { pickFileWithContent, pickDirectory, stringToBase64 } from "@/lib/tauri-bridge";
import type { CollectionImportResult } from "@/lib/types";

interface CollectionImportDialogProps {
  onClose: () => void;
}

export function CollectionImportDialog({ onClose }: CollectionImportDialogProps) {
  const { notify } = useNotification();
  const importMutation = useImportCollection();
  const { data: demoMode } = useDemoMode();
  const [tab, setTab] = useState<"file" | "bruno">("file");
  const [result, setResult] = useState<CollectionImportResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const isDemo = demoMode?.isDemoMode ?? false;

  const doImport = (payload: { folderPath?: string | null; payloadBase64?: string | null }) => {
    if (isDemo) {
      notify("info", "Demo mode", "Collection import is disabled in demo mode.");
      return;
    }
    setError(null);
    setResult(null);
    importMutation.mutate(payload, {
      onSuccess: (data) => {
        if (data.collections.length === 0 && data.environments.length === 0) {
          const message = data.warnings.length > 0
            ? data.warnings.join(" ")
            : "The file did not contain any collections or environments to import.";
          setError(message);
          setResult(null);
          notify("error", "Import failed", message);
          return;
        }
        setResult(data);
        notify(
          "success",
          "Import complete",
          `Imported ${data.collections.length} collection(s), ${data.environments.length} environment(s).`,
        );
      },
      onError: (err) => {
        const message = err instanceof Error ? err.message : "Import failed";
        setError(message);
        notify("error", "Import failed", message);
      },
    });
  };

  const handleFileImport = async () => {
    const picked = await pickFileWithContent("Import SwebKit JSON or Postman v2.1 collection");
    if (!picked) return;
    doImport({ payloadBase64: stringToBase64(picked.content) });
  };

  const handleBrunoImport = async () => {
    const folderPath = await pickDirectory("Select Bruno collection folder");
    if (!folderPath) return;
    doImport({ folderPath });
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
      data-testid="collection-import-overlay"
    >
      <div
        className="w-[480px] rounded-lg border bg-card shadow-lg"
        role="dialog"
        aria-modal="true"
        aria-label="Import collection"
        data-testid="collection-import-dialog"
      >
        <div className="flex items-center justify-between border-b px-4 py-3">
          <h2 className="text-sm font-semibold">Import Collection</h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground" data-testid="collection-import-close">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="p-4 space-y-4">
          <div className="flex rounded border p-1">
            <button
              className={`flex-1 rounded px-2 py-1 text-xs ${tab === "file" ? "bg-accent font-medium" : ""}`}
              onClick={() => setTab("file")}
              data-testid="collection-import-tab-file"
            >
              From file
            </button>
            <button
              className={`flex-1 rounded px-2 py-1 text-xs ${tab === "bruno" ? "bg-accent font-medium" : ""}`}
              onClick={() => setTab("bruno")}
              data-testid="collection-import-tab-bruno"
            >
              Bruno folder
            </button>
          </div>

          {isDemo && (
            <div className="rounded border border-warning bg-warning/10 px-3 py-2 text-xs text-warning">
              Import is disabled in demo mode.
            </div>
          )}

          {tab === "file" && (
            <div className="space-y-2 text-sm text-muted-foreground">
              <p>Import a SwebKit collection bundle or a Postman v2.1 JSON file.</p>
              <button
                onClick={handleFileImport}
                disabled={importMutation.isPending || isDemo}
                className="flex w-full items-center justify-center gap-2 rounded border px-3 py-2 hover:bg-accent disabled:opacity-50"
                data-testid="collection-import-file-btn"
              >
                <FileJson className="h-4 w-4" /> Choose file…
              </button>
            </div>
          )}

          {tab === "bruno" && (
            <div className="space-y-2 text-sm text-muted-foreground">
              <p>Select a Bruno collection folder (containing <code>bruno.json</code> and <code>.bru</code> files).</p>
              <button
                onClick={handleBrunoImport}
                disabled={importMutation.isPending || isDemo}
                className="flex w-full items-center justify-center gap-2 rounded border px-3 py-2 hover:bg-accent disabled:opacity-50"
                data-testid="collection-import-bruno-btn"
              >
                <FolderTree className="h-4 w-4" /> Choose folder…
              </button>
            </div>
          )}

          {importMutation.isPending && (
            <div className="text-center text-xs text-muted-foreground" data-testid="collection-import-loading">
              Importing…
            </div>
          )}

          {error && (
            <div className="flex items-start gap-2 rounded border border-destructive bg-destructive/10 px-3 py-2 text-xs text-destructive" data-testid="collection-import-error">
              <AlertCircle className="h-4 w-4 shrink-0" />
              <span>{error}</span>
            </div>
          )}

          {result && (
            <div className="space-y-2 rounded border bg-muted/50 px-3 py-2 text-sm" data-testid="collection-import-result">
              <div className="flex items-center gap-2 text-success">
                <CheckCircle className="h-4 w-4" />
                <span className="font-medium">Import successful</span>
              </div>
              <div className="grid grid-cols-2 gap-2 text-xs text-muted-foreground">
                <span>Collections: {result.collections.length}</span>
                <span>Environments: {result.environments.length}</span>
                <span>Requests: {result.requestCount}</span>
                <span>Capture rules: {result.captureRuleCount}</span>
              </div>
              {result.warnings.length > 0 && (
                <div className="text-xs text-warning">
                  <p className="font-medium">Warnings:</p>
                  <ul className="list-disc pl-4">
                    {result.warnings.map((w, i) => (<li key={i}>{w}</li>))}
                  </ul>
                </div>
              )}
            </div>
          )}
        </div>

        <div className="flex justify-end gap-2 border-t px-4 py-3">
          <button
            onClick={onClose}
            className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}

export function CollectionImportButton({ onOpen }: { onOpen: () => void }) {
  return (
    <button
      onClick={onOpen}
      className="rounded p-1 hover:bg-accent"
      title="Import collection"
      data-testid="collection-import-button"
    >
      <Upload className="h-4 w-4" />
    </button>
  );
}
