import { useEffect, useRef } from "react";
import { createPortal } from "react-dom";

export interface ContextMenuItem {
  label: string;
  icon?: string;
  onClick: () => void;
  destructive?: boolean;
  disabled?: boolean;
  separator?: boolean;
}

interface ContextMenuProps {
  x: number;
  y: number;
  items: ContextMenuItem[];
  onClose: () => void;
}

export function ContextMenu({ x, y, items, onClose }: ContextMenuProps) {
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        onClose();
      }
    };
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    const handleScroll = () => onClose();

    document.addEventListener("mousedown", handleClickOutside);
    document.addEventListener("keydown", handleEscape);
    document.addEventListener("scroll", handleScroll, true);
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
      document.removeEventListener("keydown", handleEscape);
      document.removeEventListener("scroll", handleScroll, true);
    };
  }, [onClose]);

  const adjustedX = Math.min(x, window.innerWidth - 220);
  const adjustedY = Math.min(y, window.innerHeight - items.length * 32 - 20);

  return createPortal(
    <div className="fixed inset-0 z-50" style={{ pointerEvents: "auto" }}>
      <div
        ref={menuRef}
        className="fixed min-w-[200px] rounded-md border bg-popover shadow-lg"
        style={{ left: adjustedX, top: adjustedY }}
        data-testid="aks-context-menu"
      >
        {items.map((item, i) => (
          item.separator ? (
            <div key={i} className="my-1 border-t" />
          ) : (
            <button
              key={i}
              disabled={item.disabled}
              onClick={() => {
                if (!item.disabled) {
                  item.onClick();
                  onClose();
                }
              }}
              className={`flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm hover:bg-accent disabled:opacity-50 ${
                item.destructive ? "text-destructive" : "text-foreground"
              }`}
              data-testid={`ctx-item-${item.label.toLowerCase().replace(/\s+/g, "-")}`}
            >
              {item.icon && <span className="w-4 text-center text-xs">{item.icon}</span>}
              <span>{item.label}</span>
            </button>
          )
        ))}
      </div>
    </div>,
    document.body,
  );
}
