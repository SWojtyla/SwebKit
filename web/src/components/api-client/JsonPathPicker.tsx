import { useEffect, useMemo, useState } from "react";
import { X, Search, Check } from "lucide-react";
import { evaluateJsonPath } from "@/lib/api";
import { useNotification } from "@/components/layout/NotificationSystem";

interface JsonPathPickerProps {
  initialBody?: string;
  initialPath?: string;
  onSelect: (path: string, previewValue: string | null) => void;
  onClose: () => void;
}

export function JsonPathPicker({ initialBody, initialPath, onSelect, onClose }: JsonPathPickerProps) {
  const { notify } = useNotification();
  const [body, setBody] = useState(initialBody ?? "{}");
  const [path, setPath] = useState(initialPath ?? "");
  const [preview, setPreview] = useState<{ value: string | null; error: string | null } | null>(null);
  const [validJson, setValidJson] = useState(true);

  useEffect(() => {
    try {
      JSON.parse(body);
      setValidJson(true);
    } catch {
      setValidJson(false);
    }
  }, [body]);

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, [onClose]);

  const parsed = useMemo(() => {
    try {
      return JSON.parse(body) as unknown;
    } catch {
      return null;
    }
  }, [body]);

  const handleEvaluate = async (expression?: string) => {
    const target = expression ?? path;
    if (!target.trim()) return;
    try {
      const result = await evaluateJsonPath(body, target);
      setPreview(result);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Evaluation failed";
      setPreview({ value: null, error: message });
      notify("error", "JSONPath evaluation failed", message);
    }
  };

  const handleSelect = () => {
    onSelect(path, preview?.value ?? null);
    onClose();
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
      data-testid="jsonpath-picker-overlay"
    >
      <div
        className="flex h-[600px] w-[700px] flex-col rounded-lg border bg-card shadow-lg"
        role="dialog"
        aria-modal="true"
        aria-label="JSONPath picker"
        data-testid="jsonpath-picker-dialog"
      >
        <div className="flex items-center justify-between border-b px-4 py-3">
          <h2 className="text-sm font-semibold">JSONPath Picker</h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground" data-testid="jsonpath-picker-close">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="flex flex-1 overflow-hidden">
          <div className="flex w-1/2 flex-col border-r p-3">
            <label className="mb-1 text-xs font-medium text-muted-foreground">Sample JSON</label>
            <textarea
              value={body}
              onChange={(e) => setBody(e.target.value)}
              className={`flex-1 resize-none rounded border bg-background p-2 font-mono text-xs ${validJson ? "" : "border-destructive"}`}
              data-testid="jsonpath-picker-body"
            />
            {!validJson && (
              <div className="mt-1 text-xs text-destructive">Invalid JSON</div>
            )}
          </div>

          <div className="flex w-1/2 flex-col p-3">
            <label className="mb-1 text-xs font-medium text-muted-foreground">JSONPath</label>
            <div className="flex gap-2">
              <input
                type="text"
                value={path}
                onChange={(e) => setPath(e.target.value)}
                placeholder="$.data.id"
                className="flex-1 rounded border bg-background px-2 py-1 text-sm font-mono"
                data-testid="jsonpath-picker-input"
              />
              <button
                onClick={() => handleEvaluate()}
                className="rounded border px-2 py-1 text-xs hover:bg-accent"
                data-testid="jsonpath-picker-evaluate"
              >
                <Search className="h-3 w-3" />
              </button>
            </div>

            {parsed !== null && (
              <div className="mt-3 flex-1 overflow-auto rounded border p-2">
                <JsonNodeTree value={parsed} path="$" onSelect={(p) => { setPath(p); handleEvaluate(p); }} />
              </div>
            )}

            {preview && (
              <div className="mt-3 rounded border bg-muted/50 p-2" data-testid="jsonpath-picker-preview">
                {preview.error ? (
                  <div className="text-xs text-destructive">{preview.error}</div>
                ) : (
                  <div className="text-xs font-mono break-all">{preview.value ?? "(no match)"}</div>
                )}
              </div>
            )}
          </div>
        </div>

        <div className="flex justify-end gap-2 border-t px-4 py-3">
          <button
            onClick={onClose}
            className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
          >
            Cancel
          </button>
          <button
            onClick={handleSelect}
            className="flex items-center gap-1 rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90"
            data-testid="jsonpath-picker-select"
          >
            <Check className="h-3 w-3" /> Use path
          </button>
        </div>
      </div>
    </div>
  );
}

interface JsonNodeTreeProps {
  value: unknown;
  path: string;
  onSelect: (path: string) => void;
}

function JsonNodeTree({ value, path, onSelect }: JsonNodeTreeProps) {
  if (value === null || typeof value !== "object") {
    return (
      <button
        onClick={() => onSelect(path)}
        className="block w-full text-left text-xs font-mono hover:bg-accent truncate px-1"
        data-testid={`jsonpath-node-${path.replace(/\W/g, "-")}`}
      >
        {JSON.stringify(value)}
      </button>
    );
  }

  if (Array.isArray(value)) {
    return (
      <div className="pl-2">
        <div className="text-xs text-muted-foreground">[ {value.length} items ]</div>
        {value.map((item, i) => (
          <div key={i} className="border-l pl-2">
            <button
              onClick={() => onSelect(`${path}[${i}]`)}
              className="block w-full text-left text-xs font-mono hover:bg-accent truncate px-1"
            >
              [{i}]
            </button>
            <JsonNodeTree value={item} path={`${path}[${i}]`} onSelect={onSelect} />
          </div>
        ))}
      </div>
    );
  }

  const obj = value as Record<string, unknown>;
  return (
    <div className="pl-2">
      {Object.entries(obj).map(([key, val]) => (
        <div key={key} className="border-l pl-2">
          <button
            onClick={() => onSelect(`${path}.${key}`)}
            className="block w-full text-left text-xs font-mono hover:bg-accent truncate px-1"
            data-testid={`jsonpath-node-${key}`}
          >
            {key}
          </button>
          <JsonNodeTree value={val} path={`${path}.${key}`} onSelect={onSelect} />
        </div>
      ))}
    </div>
  );
}
