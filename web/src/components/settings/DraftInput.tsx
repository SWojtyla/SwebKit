import { useEffect, useRef, useState, type InputHTMLAttributes } from "react";

/// A text input that keeps what you type locally and only commits on blur or Enter.
///
/// Every settings field used to write straight through to `useUpdateProfile` on each
/// `onChange`. Because the whole profile is one document, that meant a full `PUT`, an
/// atomic rewrite of `profiles.json` on disk and a refetch **per character** — and since
/// the input was controlled off server state, each character had to complete that round
/// trip before it appeared. Typing a namespace name was visibly sluggish.
///
/// Committing on blur keeps the save granularity at "one edit" instead of "one keystroke",
/// which is also the granularity the undo story wants.

export interface DraftInputProps
  extends Omit<InputHTMLAttributes<HTMLInputElement>, "value" | "onChange" | "onBlur"> {
  value: string;
  /** Called with the final text, on blur or Enter, and only when it actually changed. */
  onCommit: (value: string) => void;
}

export function DraftInput({ value, onCommit, onKeyDown, ...rest }: DraftInputProps) {
  const [draft, setDraft] = useState(value);
  const committedRef = useRef(value);

  // Latest draft and callback, for the unmount commit below — a cleanup closure captures
  // the values from the render it was created in, which would be stale by then.
  const draftRef = useRef(draft);
  draftRef.current = draft;
  const onCommitRef = useRef(onCommit);
  onCommitRef.current = onCommit;

  // Re-sync when the stored value changes underneath us — another save landing, or a
  // different record being rendered into the same input. Guarded on the last value we
  // committed so a save echoing back our own text does not fight the cursor.
  useEffect(() => {
    if (value !== committedRef.current) {
      committedRef.current = value;
      setDraft(value);
    }
  }, [value]);

  // Committing only on blur would lose an edit when the field goes away without one —
  // switching settings tabs or navigating unmounts the input, and React fires no blur.
  useEffect(() => {
    return () => {
      if (draftRef.current !== committedRef.current) {
        onCommitRef.current(draftRef.current);
      }
    };
  }, []);

  const commit = () => {
    if (draft === committedRef.current) return;
    committedRef.current = draft;
    onCommit(draft);
  };

  return (
    <input
      {...rest}
      value={draft}
      onChange={(e) => setDraft(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => {
        if (e.key === "Enter") {
          commit();
          // Blur too, so Enter and click-away feel the same and the value is visibly settled.
          (e.target as HTMLInputElement).blur();
        } else if (e.key === "Escape") {
          setDraft(committedRef.current);
          (e.target as HTMLInputElement).blur();
        }
        onKeyDown?.(e);
      }}
    />
  );
}
