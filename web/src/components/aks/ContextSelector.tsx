import { useState, useRef, useEffect, useMemo } from "react";
import { Check } from "lucide-react";
import type { KubeContextInfo } from "@/lib/types";

interface ContextSelectorProps {
  contexts: KubeContextInfo[] | undefined;
  currentContext: string | null;
  isLoading?: boolean;
  onChange: (context: string, defaultNamespace?: string) => void;
}

export function ContextSelector({ contexts, currentContext, isLoading, onChange }: ContextSelectorProps) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
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
    return [...filtered].sort((a, b) => {
      const aCurrent = a.name === currentContext;
      const bCurrent = b.name === currentContext;
      if (aCurrent && !bCurrent) return -1;
      if (!aCurrent && bCurrent) return 1;
      return a.name.localeCompare(b.name);
    });
  }, [filtered, currentContext]);

  const current = contexts?.find((c) => c.name === currentContext);
  const display = current?.name || currentContext || "Select context...";

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        disabled={isLoading}
        className="flex min-w-[12rem] items-center justify-between rounded-md border bg-card px-3 py-1.5 text-sm hover:bg-accent"
        data-testid="aks-context-select"
      >
        <span className="truncate">{display}</span>
        <span className="text-muted-foreground">{open ? "▲" : "▼"}</span>
      </button>
      {open && (
        <div className="absolute z-50 mt-1 w-80 rounded-md border bg-popover shadow-md">
          <div className="border-b p-2">
            <input
              autoFocus
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Filter contexts..."
              className="w-full rounded border bg-background px-2 py-1 text-xs"
            />
          </div>
          <div className="max-h-60 overflow-auto p-1">
            {sortedFiltered.length === 0 && (
              <div className="px-2 py-2 text-xs text-muted-foreground">No contexts found</div>
            )}
            {sortedFiltered.map((ctx) => {
              const isCurrent = ctx.name === currentContext;
              return (
                <button
                  key={ctx.name}
                  type="button"
                  onClick={() => {
                    onChange(ctx.name, ctx.namespace ?? undefined);
                    setOpen(false);
                    setSearch("");
                  }}
                  className={`w-full rounded px-2 py-1.5 text-left text-sm hover:bg-accent ${isCurrent ? "bg-accent/50 font-medium" : ""}`}
                >
                  <div className="flex items-center gap-2">
                    {isCurrent ? <Check className="h-3.5 w-3.5 text-primary" /> : <span className="h-3.5 w-3.5" />}
                    <div className="min-w-0 flex-1">
                      <div className="truncate">{ctx.name}</div>
                      {ctx.cluster && (
                        <div className="truncate text-xs text-muted-foreground">{ctx.cluster}</div>
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
