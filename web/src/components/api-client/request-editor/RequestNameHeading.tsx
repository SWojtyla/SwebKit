import { useState, useEffect, useRef } from "react";
import { Pencil } from "lucide-react";

interface RequestNameHeadingProps {
  name: string;
  onRename: (name: string) => void;
}

/**
 * The request name as a title that becomes an input on click or F2, rather than a
 * permanently visible form field.
 */
export function RequestNameHeading({ name, onRename }: RequestNameHeadingProps) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(name);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (editing) {
      inputRef.current?.focus();
      inputRef.current?.select();
    }
  }, [editing]);

  const commit = () => {
    const trimmed = draft.trim();
    if (trimmed && trimmed !== name) onRename(trimmed);
    setEditing(false);
  };

  if (editing) {
    return (
      <input
        ref={inputRef}
        type="text"
        data-testid="request-name-input"
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === "Enter") commit();
          if (e.key === "Escape") { setDraft(name); setEditing(false); }
        }}
        className="mr-2 w-48 shrink-0 rounded border bg-background px-2 py-1 text-sm"
        placeholder="Request name"
      />
    );
  }

  return (
    <button
      data-testid="request-name-heading"
      onClick={() => { setDraft(name); setEditing(true); }}
      onKeyDown={(e) => { if (e.key === "F2") { setDraft(name); setEditing(true); } }}
      title="Click to rename"
      className="group mr-2 flex max-w-[14rem] shrink-0 items-center gap-1.5 rounded px-2 py-1 text-sm font-semibold hover:bg-accent"
    >
      <span className="truncate">{name}</span>
      <Pencil className="h-3 w-3 shrink-0 opacity-0 transition-opacity group-hover:opacity-60" />
    </button>
  );
}
