import { useState, type JSX } from "react";
import { Minus, Plus, X } from "lucide-react";
import { Dialog } from "@/components/shared/Dialog";

interface ScaleDialogProps {
  /** Human-readable kind, e.g. `Deployment` — used in the heading and confirm copy. */
  kind: string;
  name: string;
  namespace: string;
  currentReplicas: number;
  isSaving?: boolean;
  onConfirm: (replicas: number) => void;
  onCancel: () => void;
}

const MAX_REPLICAS = 1000;
const PRESETS = [0, 1, 2, 3, 5, 10];

function clamp(value: number): number {
  if (!Number.isFinite(value)) return 0;
  return Math.min(MAX_REPLICAS, Math.max(0, Math.trunc(value)));
}

/**
 * Replica scaling as a modal rather than an inline row editor.
 *
 * The previous inline form swapped a `Scale` button for an input plus two more
 * buttons inside the Actions cell, which widened the column and re-laid out every
 * row in the table the moment you clicked Scale — you lost your place in the list
 * you were acting on. A modal also gives room to state which resource and
 * namespace is being scaled, which the inline editor never did.
 */
export function ScaleDialog({
  kind,
  name,
  namespace,
  currentReplicas,
  isSaving = false,
  onConfirm,
  onCancel,
}: ScaleDialogProps): JSX.Element {
  const [replicas, setReplicas] = useState(() => clamp(currentReplicas));
  const [raw, setRaw] = useState(() => String(clamp(currentReplicas)));

  const unchanged = replicas === currentReplicas;

  const apply = (next: number): void => {
    const value = clamp(next);
    setReplicas(value);
    setRaw(String(value));
  };

  const submit = (): void => {
    if (unchanged || isSaving) return;
    onConfirm(replicas);
  };

  return (
    <Dialog onClose={onCancel} label={`Scale ${kind} ${name}`} testId="aks-scale-dialog" widthClassName="w-[420px]">
      <div className="flex items-start justify-between gap-3 border-b px-4 py-3">
        <div className="min-w-0">
          <h2 className="text-sm font-semibold">Scale {kind.toLowerCase()}</h2>
          <p className="truncate text-xs text-muted-foreground" title={`${namespace}/${name}`}>
            {namespace} / <span className="font-medium text-foreground">{name}</span>
          </p>
        </div>
        <button
          onClick={onCancel}
          className="shrink-0 text-muted-foreground hover:text-foreground"
          aria-label="Close"
          data-testid="aks-scale-dialog-close"
        >
          <X className="h-4 w-4" />
        </button>
      </div>

      <div className="space-y-4 p-4">
        <div className="flex items-center gap-3">
          <button
            onClick={() => apply(replicas - 1)}
            disabled={replicas <= 0}
            className="rounded-md border p-1.5 hover:bg-accent disabled:opacity-40"
            aria-label="Decrease replicas"
            data-testid="aks-scale-decrement"
          >
            <Minus className="h-3.5 w-3.5" />
          </button>
          <input
            type="number"
            min={0}
            max={MAX_REPLICAS}
            value={raw}
            onChange={(e) => {
              // Keep the raw text so clearing the field to retype does not snap to 0
              // under the cursor; `replicas` stays on the last valid value.
              setRaw(e.target.value);
              if (e.target.value !== "") setReplicas(clamp(Number(e.target.value)));
            }}
            onBlur={() => setRaw(String(replicas))}
            onKeyDown={(e) => {
              if (e.key === "Enter") submit();
            }}
            autoFocus
            className="w-24 rounded-md border bg-background px-3 py-1.5 text-center text-sm tabular-nums"
            aria-label="Replica count"
            data-testid="aks-scale-input"
          />
          <button
            onClick={() => apply(replicas + 1)}
            disabled={replicas >= MAX_REPLICAS}
            className="rounded-md border p-1.5 hover:bg-accent disabled:opacity-40"
            aria-label="Increase replicas"
            data-testid="aks-scale-increment"
          >
            <Plus className="h-3.5 w-3.5" />
          </button>
          <span className="text-xs text-muted-foreground" data-testid="aks-scale-summary">
            {unchanged ? (
              <>Currently {currentReplicas} — unchanged</>
            ) : (
              <>
                <span className="tabular-nums">{currentReplicas}</span> →{" "}
                <span className="font-medium text-foreground tabular-nums">{replicas}</span> replicas
              </>
            )}
          </span>
        </div>

        <div className="flex flex-wrap items-center gap-1">
          <span className="mr-1 text-xs text-muted-foreground">Quick set</span>
          {PRESETS.map((preset) => (
            <button
              key={preset}
              onClick={() => apply(preset)}
              className={`rounded-md border px-2 py-1 text-xs tabular-nums hover:bg-accent ${
                replicas === preset ? "border-primary text-primary" : ""
              }`}
              data-testid={`aks-scale-preset-${preset}`}
            >
              {preset}
            </button>
          ))}
        </div>

        {replicas === 0 && currentReplicas > 0 && (
          <p className="rounded-md bg-warning/10 px-3 py-2 text-xs text-warning" data-testid="aks-scale-zero-warning">
            Scaling to 0 stops all replicas — this {kind.toLowerCase()} will serve no traffic.
          </p>
        )}
      </div>

      <div className="flex justify-end gap-2 border-t px-4 py-3">
        <button
          onClick={onCancel}
          className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
          data-testid="aks-scale-cancel"
        >
          Cancel
        </button>
        <button
          onClick={submit}
          disabled={unchanged || isSaving}
          className="rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
          data-testid="aks-scale-confirm"
        >
          {isSaving ? "Scaling…" : `Scale to ${replicas}`}
        </button>
      </div>
    </Dialog>
  );
}
