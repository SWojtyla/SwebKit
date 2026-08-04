import { X } from "lucide-react";
import { Dialog } from "@/components/shared/Dialog";

interface Props {
  open: boolean;
  onClose: () => void;
}

const shortcuts = [
  { keys: "Ctrl+K", description: "Open command palette" },
  { keys: "Ctrl+G", description: "Go to Settings" },
  { keys: "Ctrl+B", description: "Toggle sidebar navigation" },
  { keys: "Ctrl+Shift+L", description: "Toggle the AI Agent side panel" },
  { keys: "Shift+?", description: "Show this keyboard shortcuts panel" },
  { keys: "R", description: "Refresh current view (AKS)" },
  { keys: "L", description: "Jump to Pods tab (AKS)" },
  { keys: "Y", description: "View YAML for selected resource (AKS)" },
  { keys: "Ctrl+S", description: "Save current request (API Client)" },
  { keys: "Ctrl+Enter", description: "Send request (API Client)" },
];

export function KeyboardShortcutsPanel({ open, onClose }: Props) {
  if (!open) return null;

  return (
    <Dialog onClose={onClose} label="Keyboard Shortcuts" testId="keyboard-shortcuts-panel" widthClassName="w-96">
      <div className="p-6">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-lg font-semibold">Keyboard Shortcuts</h2>
          <button onClick={onClose} className="rounded p-1 hover:bg-accent" data-testid="keyboard-shortcuts-close">
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="space-y-2">
          {shortcuts.map((s) => (
            <div key={s.keys} className="flex items-center justify-between py-1">
              <span className="text-sm text-muted-foreground">{s.description}</span>
              <kbd className="rounded border bg-muted px-2 py-1 text-xs font-mono">{s.keys}</kbd>
            </div>
          ))}
        </div>
      </div>
    </Dialog>
  );
}
