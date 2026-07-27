import { useState, useRef, useCallback, useEffect, type ReactNode, Fragment } from "react";

interface ResizablePanelsProps {
  children: ReactNode[];
  initialWidths?: (number | string | null)[];
  minWidths?: number[];
  className?: string;
}

function toNumber(width: number | string | null | undefined, total: number): number | null {
  if (width == null || width === "auto") return null;
  if (typeof width === "number") return width;
  if (typeof width === "string" && width.endsWith("px")) return parseInt(width, 10);
  if (typeof width === "string" && width.endsWith("%")) return (total * parseFloat(width)) / 100;
  return 260;
}

export function ResizablePanels({
  children,
  initialWidths,
  minWidths = [180, 280, 280],
  className = "",
}: ResizablePanelsProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [widths, setWidths] = useState<(number | null)[]>(() =>
    children.map((_, i) => toNumber(initialWidths?.[i], 0)),
  );
  const activeRef = useRef<number | null>(null);
  const startXRef = useRef(0);
  const startWidthsRef = useRef<number[]>([]);

  const handleMouseDown = useCallback((index: number, e: React.MouseEvent) => {
    e.preventDefault();
    activeRef.current = index;
    startXRef.current = e.clientX;
    const container = containerRef.current;
    if (!container) return;
    const total = container.clientWidth;
    const computed = Array.from(container.children)
      .filter((_, i) => i % 2 === 0)
      .map((el) => (el as HTMLElement).offsetWidth);
    startWidthsRef.current = computed.length ? computed : widths.map((w) => (w ?? total / children.length));
  }, [children.length, widths]);

  useEffect(() => {
    const move = (e: MouseEvent) => {
      if (activeRef.current === null || !containerRef.current) return;
      const total = containerRef.current.clientWidth;
      const delta = e.clientX - startXRef.current;
      const leftIndex = activeRef.current;
      const rightIndex = leftIndex + 1;
      const next = [...startWidthsRef.current];
      const leftMin = minWidths[leftIndex] ?? 120;
      const rightMin = minWidths[rightIndex] ?? 120;
      const leftNew = Math.max(leftMin, Math.min(next[leftIndex] + delta, total - rightMin - sumOthers(next, leftIndex, rightIndex)));
      const rightNew = next[leftIndex] + next[rightIndex] - leftNew;
      next[leftIndex] = leftNew;
      next[rightIndex] = Math.max(rightMin, rightNew);
      setWidths(next.map((w) => w));
    };
    const up = () => {
      activeRef.current = null;
    };
    window.addEventListener("mousemove", move);
    window.addEventListener("mouseup", up);
    return () => {
      window.removeEventListener("mousemove", move);
      window.removeEventListener("mouseup", up);
    };
  }, [minWidths]);

  const validChildren = Array.isArray(children) ? children : [children];
  if (validChildren.length === 0) return null;

  return (
    <div ref={containerRef} className={`flex h-full overflow-hidden ${className}`}>
      {validChildren.map((child, i) => (
        <Fragment key={i}>
          {i > 0 && (
            <div
              onMouseDown={(e) => handleMouseDown(i - 1, e)}
              className="w-1.5 shrink-0 cursor-col-resize bg-muted-foreground/30 hover:bg-primary/50 active:bg-primary/70 transition-colors"
              data-testid={`resizer-${i - 1}`}
            />
          )}
          <div
            className="flex h-full flex-col overflow-hidden"
            style={widths[i] != null ? { width: widths[i]!, flex: "none" } : { flex: 1 }}
          >
            {child}
          </div>
        </Fragment>
      ))}
    </div>
  );
}

function sumOthers(widths: number[], i1: number, i2: number): number {
  return widths.reduce((sum, w, i) => (i === i1 || i === i2 ? sum : sum + w), 0);
}
