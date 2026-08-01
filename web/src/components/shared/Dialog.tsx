import { useEffect, useRef, type ReactNode } from "react";

interface DialogProps {
  children: ReactNode;
  onClose: () => void;
  label: string;
  testId?: string;
  widthClassName?: string;
}

/**
 * Shared centered-modal shell: dimmed backdrop, `role="dialog"`/`aria-modal`, focus moved to the
 * dialog on open and returned to the previously-focused element on close, and Escape-to-close.
 * Mirrors the pattern already proven in `api-client/GitDrawer.tsx` (that one is a side drawer;
 * this is the centered-modal equivalent used by confirm/name/export/rule dialogs across the app).
 */
export function Dialog({ children, onClose, label, testId = "dialog", widthClassName = "w-[360px]" }: DialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const previouslyFocused = useRef<HTMLElement | null>(null);

  useEffect(() => {
    previouslyFocused.current = document.activeElement as HTMLElement | null;
    // Don't steal focus from a child that already grabbed it (e.g. an `autoFocus` input) —
    // only focus the dialog shell itself when nothing inside it has focus yet.
    if (!dialogRef.current?.contains(document.activeElement)) {
      dialogRef.current?.focus();
    }
    return () => previouslyFocused.current?.focus();
  }, []);

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.stopPropagation();
        onClose();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" data-testid={`${testId}-overlay`}>
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-label={label}
        tabIndex={-1}
        className={`${widthClassName} rounded-lg border bg-card shadow-lg outline-none`}
        data-testid={testId}
      >
        {children}
      </div>
    </div>
  );
}
