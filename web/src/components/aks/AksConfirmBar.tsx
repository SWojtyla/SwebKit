import { useState } from "react";
import { AlertTriangle, X, Check } from "lucide-react";

interface Props {
  message: string;
  onConfirm: () => void;
  onCancel: () => void;
  confirmLabel?: string;
  /**
   * When set, the confirm button stays disabled until the user types this
   * exact string into an inline input — the production-environment safety
   * guard the MAUI app had (type the resource name to confirm a destructive
   * action against production).
   */
  requireTypedName?: string;
}

export function AksConfirmBar({
  message,
  onConfirm,
  onCancel,
  confirmLabel = "Confirm",
  requireTypedName,
}: Props) {
  const [typed, setTyped] = useState("");
  const canConfirm = !requireTypedName || typed === requireTypedName;

  return (
    <div className="flex flex-wrap items-center gap-3 border-b bg-destructive/10 px-4 py-3" data-testid="aks-confirm-bar">
      <AlertTriangle className="h-5 w-5 shrink-0 text-destructive" />
      <span className="flex-1 text-sm">{message}</span>
      {requireTypedName && (
        <input
          type="text"
          value={typed}
          onChange={(e) => setTyped(e.target.value)}
          placeholder={`Type "${requireTypedName}" to confirm`}
          className="rounded-md border bg-background px-2 py-1 text-xs"
          data-testid="aks-confirm-typed-name"
          autoFocus
        />
      )}
      <button
        onClick={onConfirm}
        disabled={!canConfirm}
        className="rounded-md bg-destructive px-3 py-1.5 text-xs text-destructive-foreground hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-40"
        data-testid="aks-confirm-yes"
      >
        <Check className="inline h-3 w-3 mr-1" /> {confirmLabel}
      </button>
      <button
        onClick={onCancel}
        className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
        data-testid="aks-confirm-cancel"
      >
        <X className="inline h-3 w-3 mr-1" /> Cancel
      </button>
    </div>
  );
}
