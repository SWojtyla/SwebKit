import { useEffect, useRef, useState, useCallback } from "react";
import { X, GitBranch } from "lucide-react";
import { GitPanel } from "./GitPanel";
import { loadViewPreference, saveViewPreference } from "@/lib/stores/panel-preferences";

const WIDTH_PREF_KEY = "api-client-git-drawer-width";
const MIN_WIDTH = 320;
const MAX_WIDTH = 900;
const DEFAULT_WIDTH = 460;

interface GitDrawerProps {
  onClose: () => void;
}

/**
 * Right-hand drawer for the Git panel: dimmed backdrop, Escape to close, focus
 * moved in on open and returned on close, and a resizable persisted width.
 *
 * The previous implementation was a bare `fixed inset-y-0` div that covered the
 * app titlebar and status bar, with its close button positioned on top of its own
 * header and no way to dismiss it by keyboard.
 */
export function GitDrawer({ onClose }: GitDrawerProps) {
  const drawerRef = useRef<HTMLDivElement>(null);
  const previouslyFocused = useRef<HTMLElement | null>(null);
  const [width, setWidth] = useState<number>(() =>
    clamp(Number(loadViewPreference<string>(WIDTH_PREF_KEY, String(DEFAULT_WIDTH))) || DEFAULT_WIDTH),
  );
  const widthRef = useRef(width);
  widthRef.current = width;
  const draggingRef = useRef(false);

  useEffect(() => {
    previouslyFocused.current = document.activeElement as HTMLElement | null;
    drawerRef.current?.focus();
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

  const startResize = useCallback((e: React.PointerEvent) => {
    e.preventDefault();
    draggingRef.current = true;
    document.body.style.userSelect = "none";
    document.body.style.cursor = "col-resize";
  }, []);

  useEffect(() => {
    const move = (e: PointerEvent) => {
      if (!draggingRef.current) return;
      // The drawer is right-anchored, so width grows as the pointer moves left.
      setWidth(clamp(window.innerWidth - e.clientX));
    };
    const up = () => {
      if (!draggingRef.current) return;
      draggingRef.current = false;
      document.body.style.userSelect = "";
      document.body.style.cursor = "";
      // Persisted once on release rather than per pointermove.
      saveViewPreference(WIDTH_PREF_KEY, String(widthRef.current));
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
    window.addEventListener("pointercancel", up);
    return () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      window.removeEventListener("pointercancel", up);
    };
  }, []);

  return (
    <>
      <div
        className="absolute inset-0 z-30 bg-black/30"
        onClick={onClose}
        data-testid="api-client-git-backdrop"
        aria-hidden="true"
      />
      <div
        ref={drawerRef}
        role="dialog"
        aria-modal="true"
        aria-label="Git"
        tabIndex={-1}
        style={{ width }}
        className="absolute inset-y-0 right-0 z-40 flex flex-col border-l bg-card shadow-lg outline-none"
        data-testid="api-client-git-panel"
      >
        <div
          role="separator"
          aria-orientation="vertical"
          aria-label="Resize Git panel"
          tabIndex={0}
          onPointerDown={startResize}
          onKeyDown={(e) => {
            if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
            e.preventDefault();
            const step = e.shiftKey ? 64 : 16;
            const next = clamp(widthRef.current + (e.key === "ArrowLeft" ? step : -step));
            setWidth(next);
            saveViewPreference(WIDTH_PREF_KEY, String(next));
          }}
          className="absolute inset-y-0 left-0 w-1.5 cursor-col-resize bg-transparent hover:bg-primary/40 focus-visible:bg-primary/60"
          data-testid="git-drawer-resizer"
        />

        <div className="flex shrink-0 items-center gap-2 border-b px-3 py-2">
          <GitBranch className="h-4 w-4 text-muted-foreground" />
          <h2 className="text-sm font-semibold">Git</h2>
          <button
            onClick={onClose}
            className="ml-auto rounded p-1 text-muted-foreground hover:bg-accent"
            aria-label="Close Git panel"
            data-testid="api-client-git-close"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="min-h-0 flex-1">
          <GitPanel />
        </div>
      </div>
    </>
  );
}

function clamp(width: number): number {
  return Math.max(MIN_WIDTH, Math.min(MAX_WIDTH, Math.round(width)));
}
