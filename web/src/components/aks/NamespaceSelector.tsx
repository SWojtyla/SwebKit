import { useState, useRef, useEffect, useMemo } from "react";
import { Check } from "lucide-react";

interface NamespaceSelectorProps {
  namespaces: string[] | undefined;
  selected: string[];
  isLoading?: boolean;
  onChange: (selected: string[]) => void;
}

export function NamespaceSelector({ namespaces = [], selected, isLoading, onChange }: NamespaceSelectorProps) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [pending, setPending] = useState<string[]>(selected);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    setPending(selected);
  }, [selected, open]);

  useEffect(() => {
    function onDocClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    if (open) document.addEventListener("mousedown", onDocClick);
    return () => document.removeEventListener("mousedown", onDocClick);
  }, [open]);

  const all = namespaces ?? [];
  const filtered = all.filter((ns) => ns.toLowerCase().includes(search.toLowerCase()));

  const sortedFiltered = useMemo(() => {
    const selectedSet = new Set(pending);
    return [...filtered].sort((a, b) => {
      const aSelected = selectedSet.has(a);
      const bSelected = selectedSet.has(b);
      if (aSelected && !bSelected) return -1;
      if (!aSelected && bSelected) return 1;
      return a.localeCompare(b);
    });
  }, [filtered, pending]);

  const isAllSelected = all.length > 0 && (selected.includes("*") || selected.length === all.length);
  const display = isAllSelected
    ? "All namespaces"
    : selected.length === 0
      ? "Select namespace..."
      : selected.length === 1
        ? selected[0]
        : `${selected.length} namespaces`;

  const toggleNs = (ns: string) => {
    setPending((prev) => (prev.includes(ns) ? prev.filter((n) => n !== ns) : [...prev, ns]));
  };

  const hasChanges = pending.length !== selected.length || pending.some((ns) => !selected.includes(ns));
  const apply = () => {
    const result = all.length > 0 && pending.length === all.length ? all : pending;
    onChange(result);
    setOpen(false);
    setSearch("");
  };

  const selectAll = () => setPending(all);
  const selectNone = () => setPending([]);

  return (
    <div ref={ref} className="relative flex items-center gap-2">
      {/*
        Hidden native select keeps Playwright tests working. `sr-only` alone is
        the right class: it renders a 1x1 clipped element that is invisible to
        users but still has a bounding box, so Playwright can interact with it.
        Adding `h-0 w-0` collapsed that box to nothing, which made every
        selectOption() call fail its actionability check.
      */}
      <select
        data-testid="aks-namespace-select"
        multiple
        value={selected}
        onChange={(e) => {
          const options = Array.from(e.target.selectedOptions).map((o) => o.value);
          onChange(options.length ? options : all.length > 0 ? [all[0]] : []);
        }}
        className="sr-only"
      >
        <option value="*">All namespaces</option>
        {all.map((ns) => (
          <option key={ns} value={ns}>
            {ns}
          </option>
        ))}
      </select>

      <button
        type="button"
        onClick={() => !isLoading && setOpen((v) => !v)}
        disabled={isLoading}
        aria-haspopup="listbox"
        aria-expanded={open}
        title={display}
        className="flex min-w-[14rem] max-w-[24rem] items-center justify-between rounded-md border bg-card px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
        data-testid="aks-namespace-dropdown"
      >
        <span className="truncate">{display}</span>
        <span className="text-muted-foreground">{open ? "▲" : "▼"}</span>
      </button>

      {isAllSelected && (
        <span className="text-sm text-primary" title="All namespaces selected">
          *
        </span>
      )}

      {open && (
        <div className="absolute top-full z-50 mt-1 w-96 rounded-md border bg-popover shadow-md">
          <div className="border-b p-2">
            <div className="flex items-center gap-2">
              <input
                autoFocus
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search namespaces..."
                className="flex-1 rounded border bg-background px-2 py-1 text-xs"
                aria-label="Filter namespaces"
              />
              {search && (
                <button
                  type="button"
                  onClick={() => setSearch("")}
                  className="text-xs text-muted-foreground hover:text-foreground"
                >
                  Clear
                </button>
              )}
            </div>
            <div className="mt-1 flex gap-2 text-xs text-muted-foreground">
              <span>{all.length} total</span>
              {filtered.length !== all.length && <span>· {filtered.length} matching</span>}
            </div>
          </div>
          <div className="max-h-72 overflow-auto p-1">
            {sortedFiltered.length === 0 && (
              <div className="px-2 py-2 text-xs text-muted-foreground">No namespaces found</div>
            )}
            {sortedFiltered.map((ns) => {
              const isSelected = pending.includes(ns);
              return (
                <label
                  key={ns}
                  className={`flex cursor-pointer items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-accent ${isSelected ? "bg-accent/40" : ""}`}
                >
                  <input
                    type="checkbox"
                    checked={isSelected}
                    onChange={() => toggleNs(ns)}
                    className="h-4 w-4"
                  />
                  <span className="flex-1 truncate">{ns}</span>
                  {isSelected && <Check className="h-3.5 w-3.5 text-primary" />}
                </label>
              );
            })}
          </div>
          <div className="flex items-center justify-between border-t p-2 text-xs">
            <div className="flex gap-2">
              <button type="button" onClick={selectAll} className="rounded px-2 py-1 hover:bg-accent">
                All
              </button>
              <button type="button" onClick={selectNone} className="rounded px-2 py-1 hover:bg-accent">
                None
              </button>
            </div>
            <button
              type="button"
              onClick={apply}
              disabled={!hasChanges}
              className="rounded bg-primary px-3 py-1 text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
            >
              Apply {pending.length > 0 && `(${pending.length})`}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
