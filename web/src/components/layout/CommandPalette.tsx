import { useState, useEffect, useRef, useMemo, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { Search } from "lucide-react";
import { useCommandPaletteItems, type CommandPaletteItem } from "@/lib/hooks";

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

function fuzzyScore(query: string, text: string): number {
  const q = query.toLowerCase().trim();
  const t = text.toLowerCase();
  if (!q) return 0;
  if (t === q) return 10000;
  if (t.startsWith(q)) return 5000 - t.length;

  let idx = 0;
  let score = 0;
  let consecutive = 0;
  for (let i = 0; i < q.length; i++) {
    const pos = t.indexOf(q[i], idx);
    if (pos === -1) return -1;
    if (pos === idx) {
      consecutive++;
      score += 20 + consecutive * 10;
    } else {
      consecutive = 0;
      score += 2;
      if (
        pos === 0 ||
        t[pos - 1] === " " ||
        t[pos - 1] === "-" ||
        t[pos - 1] === "/" ||
        t[pos - 1] === ">" ||
        t[pos - 1] === "•"
      ) {
        score += 8;
      }
    }
    idx = pos + 1;
  }
  score -= (t.length - q.length) * 0.5;
  return score;
}

function scoreItem(query: string, item: CommandPaletteItem): number {
  const text = `${item.label} ${item.subtitle ?? ""} ${item.keywords}`;
  return fuzzyScore(query, text);
}

interface CommandPaletteProps {
  open: boolean;
  onClose: () => void;
}

export function CommandPalette({ open, onClose }: CommandPaletteProps) {
  const [query, setQuery] = useState("");
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [mru, setMru] = useState<string[]>([]);
  const navigate = useNavigate();
  const inputRef = useRef<HTMLInputElement>(null);
  const scrollRef = useRef<HTMLDivElement>(null);
  const allItems = useCommandPaletteItems(open);

  useEffect(() => {
    if (open) {
      setQuery("");
      setSelectedIndex(0);
      setMru(loadMru());
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [open]);

  useEffect(() => {
    setSelectedIndex(0);
  }, [query]);

  useEffect(() => {
    const container = scrollRef.current;
    if (!container) return;
    const child = container.children[selectedIndex] as HTMLElement | undefined;
    child?.scrollIntoView({ block: "nearest" });
  }, [selectedIndex]);

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

    const scored = allItems
      .map((item) => ({ item, score: scoreItem(q, item) }))
      .filter(({ score }) => score > 0)
      .sort((a, b) => b.score - a.score);
    return scored.map(({ item }) => item);
  }, [query, allItems, mru]);

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
      setSelectedIndex((i) => Math.min(i + 1, filtered.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setSelectedIndex((i) => Math.max(i - 1, 0));
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
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-black/50 pt-24"
      onClick={onClose}
      onKeyDown={(e) => {
        if (e.key === "Escape") {
          e.preventDefault();
          onClose();
        }
      }}
      tabIndex={-1}
      data-testid="command-palette-overlay"
    >
      <div
        className="w-full max-w-lg rounded-lg border bg-card shadow-lg"
        onClick={(e) => e.stopPropagation()}
        data-testid="command-palette"
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
      </div>
    </div>
  );
}
