import { useState } from "react";
import { Download, X } from "lucide-react";
import type { ApiCollection, ApiEnvironment } from "@/lib/types";

interface CollectionExportDialogProps {
  collection: ApiCollection;
  environments: ApiEnvironment[];
  onClose: () => void;
}

type ExportFormat = "sweb" | "postman" | "json";

const formats: { value: ExportFormat; label: string; ext: string }[] = [
  { value: "sweb", label: "SwebKit JSON", ext: ".sweb.json" },
  { value: "postman", label: "Postman v2.1", ext: ".postman_collection.json" },
  { value: "json", label: "Raw JSON", ext: ".json" },
];

function downloadFile(content: string, fileName: string, mimeType: string) {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

function exportSwebKit(collection: ApiCollection, environments: ApiEnvironment[]): string {
  const bundle = {
    schemaVersion: 1,
    exportedAt: new Date().toISOString(),
    collection,
    environments,
  };
  return JSON.stringify(bundle, null, 2);
}

function exportRawJson(collection: ApiCollection): string {
  return JSON.stringify(collection, null, 2);
}

function exportPostman(collection: ApiCollection): string {
  const buildItems = (nodes: ApiCollection["nodes"]): unknown[] => {
    return nodes
      .filter((n) => n.type === "Folder" || n.type === "Request")
      .map((node) => {
        if (node.type === "Folder") {
          return {
            name: node.name,
            item: buildItems(node.children),
          };
        }
        const req = node.request;
        if (!req) return null;
        return {
          name: req.name,
          request: {
            method: req.method.toUpperCase(),
            header: req.headers
              .filter((h) => h.isEnabled && h.key)
              .map((h) => ({ key: h.key, value: h.value ?? "", type: "text" })),
            url: {
              raw: req.url,
              host: ["{{baseUrl}}"],
              path: req.url.replace(/^https?:\/\/[^/]+/, "").split("/").filter(Boolean),
            },
            body: req.body.mode !== "None" && req.body.rawContent
              ? { mode: "raw", raw: req.body.rawContent }
              : undefined,
          },
        };
      })
      .filter(Boolean);
  };

  const postman = {
    info: {
      _postman_id: collection.id,
      name: collection.name,
      schema: "https://schema.getpostman.com/json/collection/v2.1.0/collection.json",
    },
    item: buildItems(collection.nodes),
    variable: collection.variables
      .filter((v) => v.isEnabled)
      .map((v) => ({ key: v.key, value: v.value ?? "" })),
  };
  return JSON.stringify(postman, null, 2);
}

export function CollectionExportDialog({ collection, environments, onClose }: CollectionExportDialogProps) {
  const [selectedFormat, setSelectedFormat] = useState<ExportFormat>("sweb");
  const [includeEnvironments, setIncludeEnvironments] = useState(true);

  const handleExport = () => {
    let content: string;
    let fileName: string;
    const ext = formats.find((f) => f.value === selectedFormat)?.ext ?? ".json";

    switch (selectedFormat) {
      case "sweb":
        content = exportSwebKit(collection, includeEnvironments ? environments : []);
        fileName = `${collection.name}${ext}`;
        break;
      case "postman":
        content = exportPostman(collection);
        fileName = `${collection.name}${ext}`;
        break;
      case "json":
        content = exportRawJson(collection);
        fileName = `${collection.name}${ext}`;
        break;
    }

    downloadFile(content, fileName, "application/json");
    onClose();
  };

  return (
    <>
      <div className="fixed inset-0 z-40 bg-black/50" onClick={onClose} />
      <div
        className="fixed left-1/2 top-1/2 z-50 w-96 -translate-x-1/2 -translate-y-1/2 rounded-lg border bg-card p-6 shadow-lg"
        data-testid="collection-export-dialog"
      >
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-lg font-semibold">Export Collection</h2>
          <button onClick={onClose} className="rounded p-1 hover:bg-accent">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="mb-4">
          <label className="mb-2 block text-sm font-medium">Format</label>
          <div className="space-y-2">
            {formats.map((fmt) => (
              <label
                key={fmt.value}
                className="flex cursor-pointer items-center gap-2 rounded border p-2 text-sm hover:bg-accent"
              >
                <input
                  type="radio"
                  name="export-format"
                  value={fmt.value}
                  checked={selectedFormat === fmt.value}
                  onChange={() => setSelectedFormat(fmt.value)}
                  data-testid={`export-format-${fmt.value}`}
                />
                <span>{fmt.label}</span>
                <span className="text-xs text-muted-foreground">{fmt.ext}</span>
              </label>
            ))}
          </div>
        </div>

        {environments.length > 0 && (
          <div className="mb-4">
            <label className="flex cursor-pointer items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={includeEnvironments}
                onChange={(e) => setIncludeEnvironments(e.target.checked)}
                data-testid="export-include-envs"
              />
              <span>Include all environments ({environments.length})</span>
            </label>
            <p className="mt-1 text-xs text-muted-foreground">
              Environments are only included in SwebKit format.
            </p>
          </div>
        )}

        <div className="flex justify-end gap-2">
          <button
            onClick={onClose}
            className="rounded border px-4 py-2 text-sm hover:bg-accent"
          >
            Cancel
          </button>
          <button
            onClick={handleExport}
            className="flex items-center gap-1 rounded bg-primary px-4 py-2 text-sm font-medium text-primary-foreground hover:bg-primary/90"
            data-testid="export-download-button"
          >
            <Download className="h-4 w-4" />
            Download
          </button>
        </div>
      </div>
    </>
  );
}
