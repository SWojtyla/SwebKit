import { useState, useEffect, useCallback, useRef } from "react";
import { X } from "lucide-react";

export interface ResizablePanelProps {
  children: React.ReactNode;
  visible?: boolean;
  title?: string;
  onClose?: () => void;
  showHeader?: boolean;
  defaultWidth?: number;
  minWidth?: number;
  maxWidth?: number;
  storageKey?: string;
  position?: "left" | "right";
  className?: string;
  "data-testid"?: string;
  closeTestId?: string;
}

const STORAGE_PREFIX = "swokit-resizable-panel-width";

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

function readStoredWidth(
  storageKey: string | undefined,
  defaultWidth: number,
  minWidth: number,
  maxWidth: number,
): number {
  if (typeof window === "undefined" || !storageKey) {
    return clamp(defaultWidth, minWidth, maxWidth);
  }
  try {
    const raw = window.localStorage.getItem(`${STORAGE_PREFIX}:${storageKey}`);
    if (!raw) return clamp(defaultWidth, minWidth, maxWidth);
    const parsed = parseInt(raw, 10);
    if (Number.isNaN(parsed)) return clamp(defaultWidth, minWidth, maxWidth);
    return clamp(parsed, minWidth, maxWidth);
  } catch {
    return clamp(defaultWidth, minWidth, maxWidth);
  }
}

function writeStoredWidth(storageKey: string | undefined, width: number): void {
  if (typeof window === "undefined" || !storageKey) return;
  try {
    window.localStorage.setItem(`${STORAGE_PREFIX}:${storageKey}`, String(width));
  } catch {
    // localStorage may be unavailable in some contexts; ignore.
  }
}

export function ResizablePanel({
  children,
  visible = true,
  title = "Details",
  onClose,
  showHeader,
  defaultWidth = 380,
  minWidth = 240,
  maxWidth = 600,
  storageKey,
  position = "right",
  className = "",
  "data-testid": panelTestId = "resizable-panel",
  closeTestId = "resizable-panel-close",
}: ResizablePanelProps) {
  const [width, setWidth] = useState(() =>
    readStoredWidth(storageKey, defaultWidth, minWidth, maxWidth),
  );
  const widthRef = useRef(width);
  useEffect(() => {
    widthRef.current = width;
  }, [width]);

  const [isDragging, setIsDragging] = useState(false);
  const startXRef = useRef(0);
  const startWidthRef = useRef(width);

  const handleMouseDown = useCallback(
    (e: React.MouseEvent<HTMLDivElement>) => {
      e.preventDefault();
      startXRef.current = e.clientX;
      startWidthRef.current = widthRef.current;
      setIsDragging(true);
      if (typeof document !== "undefined") {
        document.body.style.userSelect = "none";
      }
    },
    [],
  );

  const handleDoubleClick = useCallback(() => {
    const target = widthRef.current >= maxWidth ? defaultWidth : maxWidth;
    const next = clamp(target, minWidth, maxWidth);
    setWidth(next);
    writeStoredWidth(storageKey, next);
  }, [maxWidth, defaultWidth, minWidth, storageKey]);

  useEffect(() => {
    if (!isDragging) return;

    const handleMouseMove = (e: MouseEvent) => {
      const delta =
        position === "right"
          ? startXRef.current - e.clientX
          : e.clientX - startXRef.current;
      const next = clamp(startWidthRef.current + delta, minWidth, maxWidth);
      setWidth(next);
    };

    const handleMouseUp = () => {
      setIsDragging(false);
      if (typeof document !== "undefined") {
        document.body.style.userSelect = "";
      }
      writeStoredWidth(storageKey, widthRef.current);
    };

    window.addEventListener("mousemove", handleMouseMove);
    window.addEventListener("mouseup", handleMouseUp);
    return () => {
      window.removeEventListener("mousemove", handleMouseMove);
      window.removeEventListener("mouseup", handleMouseUp);
    };
  }, [isDragging, position, minWidth, maxWidth, storageKey]);

  if (!visible) return null;

  const handleClass =
    "absolute top-0 bottom-0 z-10 w-1.5 cursor-col-resize bg-transparent hover:bg-primary/50 active:bg-primary/50 transition-colors";
  const handlePosition = position === "right" ? "left-0" : "right-0";
  const borderClass = position === "right" ? "border-l" : "border-r";
  const shouldShowHeader =
    showHeader ?? (title !== undefined || onClose !== undefined);

  return (
    <div
      className={`relative flex h-full shrink-0 flex-col overflow-hidden bg-card ${borderClass} ${className}`}
      style={{ width }}
      data-testid={panelTestId}
    >
      <div
        className={`${handleClass} ${handlePosition}`}
        onMouseDown={handleMouseDown}
        onDoubleClick={handleDoubleClick}
        title="Drag to resize, double-click to maximize or reset"
        data-testid="resizable-handle"
      />
      {shouldShowHeader && (
        <div className="flex items-center justify-between border-b px-3 py-2">
          <span className="text-sm font-medium">{title}</span>
          {onClose && (
            <button
              type="button"
              onClick={onClose}
              className="rounded px-1.5 py-0.5 text-muted-foreground hover:bg-accent hover:text-foreground"
              title="Close details"
              data-testid={closeTestId}
            >
              <X className="h-4 w-4" />
            </button>
          )}
        </div>
      )}
      <div className="min-h-0 flex-1 overflow-auto">{children}</div>
    </div>
  );
}
