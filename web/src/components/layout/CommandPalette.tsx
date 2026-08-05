import { useCallback, useMemo, useState } from "react";
import { useNavigate } from "react-router";
import { Search } from "lucide-react";
import { useCommandPaletteItems, type CommandPaletteItem } from "@/lib/hooks";
import { usePaletteFocusOnOpen, usePaletteNavigation } from "@/lib/hooks/usePaletteNavigation";
import { fuzzyFilter } from "@/lib/paletteSearch";
import { PaletteOverlay } from "./PaletteOverlay";

const MRU_KEY = "swebkit:command-palette-mru";
const MAX_MRU = 8;

function loadMru(): string[] {
  try {
    return JSON.parse(localStorage.getItem(MRU_KEY) || "[]") as string[];
  } catch {
    return [];
  }
}

function saveMru(ids: string[]) {
  try {
    localStorage.setItem(MRU_KEY, JSON.stringify(ids.slice(0, MAX_MRU)));
  } catch {
    // ignore storage errors
  }
}

function itemSearchText(item: CommandPaletteItem): string {
  return `${item.label} ${item.subtitle ?? ""} ${item.keywords}`;
}

interface CommandPaletteProps {
  open: boolean;
  onClose: () => void;
}

export function CommandPalette({ open, onClose }: CommandPaletteProps) {
  const [query, setQuery] = useState("");
  const [mru, setMru] = useState<string[]>([]);
  const navigate = useNavigate();
  const allItems = useCommandPaletteItems(open);

  const inputRef = usePaletteFocusOnOpen(open, () => {
    setQuery("");
    setMru(loadMru());
  });

  const filtered = useMemo(() => {
    const q = query.trim();
    if (!q) {
      const mruIds = new Set(mru);
      const mruItems = mru
        .map((id) => allItems.find((item) => item.id === id))
        .filter((item): item is CommandPaletteItem => Boolean(item));
      const staticItems = allItems.filter((item) => item.type === "nav" && !mruIds.has(item.id));
      return [...mruItems, ...staticItems];
    }
    return fuzzyFilter(q, allItems, itemSearchText);
  }, [query, allItems, mru]);

  const { selectedIndex, setSelectedIndex, scrollRef, moveDown, moveUp } = usePaletteNavigation(
    filtered.length,
    query,
  );

  const handleSelect = useCallback(
    (item: CommandPaletteItem) => {
      const nextMru = [item.id, ...mru.filter((id) => id !== item.id)].slice(0, MAX_MRU);
      setMru(nextMru);
      saveMru(nextMru);
      navigate(item.to, { state: item.state });
      onClose();
    },
    [mru, navigate, onClose],
  );

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      moveDown();
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      moveUp();
    } else if (e.key === "Enter") {
      e.preventDefault();
      if (filtered[selectedIndex]) {
        handleSelect(filtered[selectedIndex]);
      }
    } else if (e.key === "Escape") {
      e.preventDefault();
      onClose();
    }
  };

  if (!open) return null;

  return (
    <PaletteOverlay
      overlayTestId="command-palette-overlay"
      dialogTestId="command-palette"
      ariaLabel="Command palette"
      dialogClassName="w-full max-w-lg rounded-lg border bg-card shadow-lg"
      onClose={onClose}
      onOverlayKeyDown={(e) => {
        if (e.key === "Escape") {
          e.preventDefault();
          onClose();
        }
      }}
    >
      <div className="flex items-center gap-2 border-b px-4 py-3">
        <Search className="h-4 w-4 text-muted-foreground" />
        <input
          ref={inputRef}
          type="text"
          placeholder="Search commands or resources..."
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={handleKeyDown}
          className="flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground"
          data-testid="command-palette-input"
        />
        <kbd className="rounded border px-1.5 py-0.5 text-xs text-muted-foreground">ESC</kbd>
      </div>
      <div ref={scrollRef} className="max-h-72 overflow-auto p-2">
        {filtered.length === 0 && (
          <div className="px-3 py-4 text-sm text-muted-foreground">No commands found</div>
        )}
        {filtered.map((item, i) => {
          const Icon = item.icon;
          return (
            <button
              key={item.id}
              onClick={() => handleSelect(item)}
              onMouseEnter={() => setSelectedIndex(i)}
              className={`flex w-full items-center gap-3 rounded-md px-3 py-2 text-left text-sm transition-colors ${
                i === selectedIndex ? "bg-accent" : "hover:bg-accent"
              }`}
              data-testid={`command-palette-item-${item.id}`}
            >
              <Icon className="h-4 w-4 shrink-0 text-muted-foreground" />
              <div className="flex min-w-0 flex-1 flex-col">
                <span className="truncate">{item.label}</span>
                {item.subtitle && (
                  <span className="truncate text-xs text-muted-foreground">{item.subtitle}</span>
                )}
              </div>
            </button>
          );
        })}
      </div>
    </PaletteOverlay>
  );
}
