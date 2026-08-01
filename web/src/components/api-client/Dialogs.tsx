import { useState } from "react";
import { X } from "lucide-react";
import { Dialog } from "@/components/shared/Dialog";

interface NameDialogProps {
  title: string;
  label: string;
  defaultValue?: string;
  confirmText?: string;
  onConfirm: (name: string) => void;
  onCancel: () => void;
}

export function NameDialog({ title, label, defaultValue = "", confirmText = "OK", onConfirm, onCancel }: NameDialogProps) {
  const [value, setValue] = useState(defaultValue);

  return (
    <Dialog onClose={onCancel} label={title} testId="name-dialog">
      <div className="flex items-center justify-between border-b px-4 py-3">
        <h2 className="text-sm font-semibold">{title}</h2>
        <button onClick={onCancel} className="text-muted-foreground hover:text-foreground" data-testid="name-dialog-cancel-x">
          <X className="h-4 w-4" />
        </button>
      </div>
      <div className="p-4 space-y-3">
        <div>
          <label className="mb-1 block text-xs font-medium text-muted-foreground">{label}</label>
          <input
            type="text"
            data-testid="name-dialog-input"
            value={value}
            onChange={(e) => setValue(e.target.value)}
            autoFocus
            onKeyDown={(e) => {
              if (e.key === "Enter" && value.trim()) onConfirm(value.trim());
              // Escape is already handled globally by Dialog; avoid double-handling here.
            }}
            className="w-full rounded-md border bg-background px-3 py-1.5 text-sm"
          />
        </div>
        <div className="flex justify-end gap-2">
          <button
            onClick={onCancel}
            className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
            data-testid="name-dialog-cancel"
          >
            Cancel
          </button>
          <button
            onClick={() => value.trim() && onConfirm(value.trim())}
            disabled={!value.trim()}
            className="rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
            data-testid="name-dialog-confirm"
          >
            {confirmText}
          </button>
        </div>
      </div>
    </Dialog>
  );
}

interface ConfirmDialogProps {
  message: string;
  confirmText?: string;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmDialog({ message, confirmText = "Delete", onConfirm, onCancel }: ConfirmDialogProps) {
  return (
    <Dialog onClose={onCancel} label={message} testId="confirm-dialog">
      <div className="p-4">
        <p className="text-sm">{message}</p>
      </div>
      <div className="flex justify-end gap-2 border-t px-4 py-3">
        <button
          onClick={onCancel}
          className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
          data-testid="confirm-dialog-cancel"
        >
          Cancel
        </button>
        <button
          onClick={onConfirm}
          className="rounded-md bg-destructive px-3 py-1.5 text-xs text-destructive-foreground hover:opacity-90"
          data-testid="confirm-dialog-confirm"
        >
          {confirmText}
        </button>
      </div>
    </Dialog>
  );
}
