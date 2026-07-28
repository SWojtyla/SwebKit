import { useState, useEffect, useCallback } from "react";

interface Props {
  visible?: boolean;
  title?: string;
  children: React.ReactNode;
  onClose?: () => void;
  defaultWidth?: number;
  minWidth?: number;
  maxWidth?: number;
}

export function SidePanel({
  visible = true,
  title = "Details",
  children,
  onClose,
  defaultWidth = 380,
  minWidth = 240,
  maxWidth = 600,
}: Props) {
  const [width, setWidth] = useState(defaultWidth);
  const [isDragging, setIsDragging] = useState(false);
  const [startX, setStartX] = useState(0);
  const [startWidth, setStartWidth] = useState(defaultWidth);

  const handleMouseDown = useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    setIsDragging(true);
    setStartX(e.clientX);
    setStartWidth(width);
    if (typeof document !== "undefined") {
      document.body.style.userSelect = "none";
    }
  }, [width]);

  const handleMouseMove = useCallback(
    (e: MouseEvent) => {
      if (!isDragging) return;
      // Handle is on the left edge: dragging left makes the panel wider.
      const delta = startX - e.clientX;
      const next = Math.min(maxWidth, Math.max(minWidth, startWidth + delta));
      setWidth(next);
    },
    [isDragging, startX, startWidth, minWidth, maxWidth],
  );

  const handleMouseUp = useCallback(() => {
    if (!isDragging) return;
    setIsDragging(false);
    if (typeof document !== "undefined") {
      document.body.style.userSelect = "";
    }
  }, [isDragging]);

  const resetWidth = useCallback(() => {
    setWidth(defaultWidth);
  }, [defaultWidth]);

  useEffect(() => {
    if (!isDragging) return;
    window.addEventListener("mousemove", handleMouseMove);
    window.addEventListener("mouseup", handleMouseUp);
    return () => {
      window.removeEventListener("mousemove", handleMouseMove);
      window.removeEventListener("mouseup", handleMouseUp);
    };
  }, [isDragging, handleMouseMove, handleMouseUp]);

  if (!visible) return null;

  return (
    <div
      className="relative flex h-full flex-shrink-0 flex-col overflow-hidden border-l bg-card"
      style={{ width }}
      data-testid="side-panel"
    >
      <div
        className="absolute left-0 top-0 bottom-0 z-10 w-1 cursor-col-resize hover:bg-primary/50 active:bg-primary/50"
        onMouseDown={handleMouseDown}
        onDoubleClick={resetWidth}
        title="Drag to resize, double-click to reset"
      />
      {(title || onClose) && (
        <div className="flex items-center justify-between border-b px-3 py-2">
          <span className="text-sm font-medium">{title}</span>
          {onClose && (
            <button
              type="button"
              onClick={onClose}
              className="rounded px-1.5 py-0.5 text-muted-foreground hover:bg-accent hover:text-foreground"
              title="Close details"
              data-testid="side-panel-close"
            >
              &#x2715;
            </button>
          )}
        </div>
      )}
      <div className="min-h-0 flex-1 overflow-auto">{children}</div>
    </div>
  );
}
