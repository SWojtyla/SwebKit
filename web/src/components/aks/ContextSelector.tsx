import { useState, useRef, useEffect, useMemo } from "react";
import { Check, Loader2 } from "lucide-react";
import type { KubeContextInfo } from "@/lib/types";
import { loadViewPreference } from "@/lib/stores/panel-preferences";

interface ContextSelectorProps {
  contexts: KubeContextInfo[] | undefined;
  currentContext: string | null;
  isLoading?: boolean;
  /** Context the switch is targeting, so the button can say "Switching to X…" rather
   * than just sitting disabled with the old context's name still showing. */
  pendingContext?: string | null;
  onChange: (context: string, defaultNamespace?: string) => void;
}

export function ContextSelector({ contexts, currentContext, isLoading, pendingContext, onChange }: ContextSelectorProps) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [highlight, setHighlight] = useState(0);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function onDocClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", onDocClick);
    return () => document.removeEventListener("mousedown", onDocClick);
  }, []);

  const filtered = (contexts ?? []).filter(
    (c) =>
      c.name.toLowerCase().includes(search.toLowerCase()) ||
      (c.cluster ?? "").toLowerCase().includes(search.toLowerCase())
  );

  const sortedFiltered = useMemo(() => {
    // Current first, then most-recently-used (persisted by the workspace on each
    // successful switch), then alphabetical.
    const mru = loadViewPreference<string[]>("aks-context-mru", []);
    const mruRank = new Map(mru.map((name, i) => [name, i]));
    return [...filtered].sort((a, b) => {
      const aCurrent = a.name === currentContext;
      const bCurrent = b.name === currentContext;
      if (aCurrent && !bCurrent) return -1;
      if (!aCurrent && bCurrent) return 1;
      const aMru = mruRank.get(a.name) ?? Number.MAX_SAFE_INTEGER;
      const bMru = mruRank.get(b.name) ?? Number.MAX_SAFE_INTEGER;
      if (aMru !== bMru) return aMru - bMru;
      return a.name.localeCompare(b.name);
    });
    // `open` is a dep so a fresh open re-reads the MRU preference.
  }, [filtered, currentContext, open]);

  useEffect(() => {
    setHighlight(0);
  }, [search, open]);

  const pick = (ctx: KubeContextInfo) => {
    onChange(ctx.name, ctx.namespace ?? undefined);
    setOpen(false);
    setSearch("");
  };

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (!open) return;
    if (e.key === "Escape") {
      e.preventDefault();
      setOpen(false);
    } else if (e.key === "ArrowDown") {
      e.preventDefault();
      setHighlight((h) => Math.min(h + 1, sortedFiltered.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setHighlight((h) => Math.max(h - 1, 0));
    } else if (e.key === "Enter" && sortedFiltered[highlight]) {
      e.preventDefault();
      pick(sortedFiltered[highlight]);
    }
  };

  const current = contexts?.find((c) => c.name === currentContext);
  const display = isLoading
    ? `Switching to ${pendingContext ?? "…"}`
    : current?.name || currentContext || "Select context...";

  return (
    <div ref={ref} className="relative" onKeyDown={onKeyDown}>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        disabled={isLoading}
        aria-haspopup="listbox"
        aria-expanded={open}
        title={isLoading ? display : (current?.name ?? "Select context")}
        className="flex min-w-[12rem] items-center justify-between rounded-md border bg-card px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-70"
        data-testid="aks-context-select"
      >
        <span className="flex min-w-0 items-center gap-1.5">
          {isLoading && <Loader2 className="h-3.5 w-3.5 shrink-0 animate-spin" />}
          <span className="truncate">{display}</span>
        </span>
        <span className="text-muted-foreground">{open ? "▲" : "▼"}</span>
      </button>
      {open && (
        <div className="absolute z-50 mt-1 w-80 rounded-md border bg-popover shadow-md" role="listbox" aria-label="Kubernetes contexts">
          <div className="border-b p-2">
            <input
              autoFocus
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Filter contexts..."
              aria-label="Filter contexts"
              className="w-full rounded border bg-background px-2 py-1 text-xs"
              data-testid="aks-context-filter"
            />
          </div>
          <div className="max-h-60 overflow-auto p-1">
            {sortedFiltered.length === 0 && (
              <div className="px-2 py-2 text-xs text-muted-foreground">No contexts found</div>
            )}
            {sortedFiltered.map((ctx, i) => {
              const isCurrent = ctx.name === currentContext;
              return (
                <button
                  key={ctx.name}
                  type="button"
                  role="option"
                  aria-selected={isCurrent}
                  onClick={() => pick(ctx)}
                  onMouseEnter={() => setHighlight(i)}
                  className={`w-full rounded px-2 py-1.5 text-left text-sm hover:bg-accent ${isCurrent ? "bg-accent/50 font-medium" : ""} ${i === highlight ? "bg-accent/40" : ""}`}
                >
                  <div className="flex items-center gap-2">
                    {isCurrent ? <Check className="h-3.5 w-3.5 text-primary" /> : <span className="h-3.5 w-3.5" />}
                    <div className="min-w-0 flex-1">
                      <div className="truncate">{ctx.name}</div>
                      {(ctx.cluster || ctx.namespace) && (
                        <div className="truncate text-xs text-muted-foreground">
                          {ctx.cluster}
                          {ctx.cluster && ctx.namespace ? " · " : ""}
                          {ctx.namespace ? `ns: ${ctx.namespace}` : ""}
                        </div>
                      )}
                    </div>
                  </div>
                </button>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
