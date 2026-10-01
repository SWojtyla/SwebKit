import { useEffect, useMemo, useRef, useState } from "react";
import type { KubeContextInfo } from "@/lib/types";
import { loadViewPreference } from "@/lib/stores/panel-preferences";
import { Check, Loader2 } from "lucide-react";

interface ContextSelectorProps {
    contexts: KubeContextInfo[] | undefined;
    /** The primary context — actions and agent tools route here. */
    currentContext: string | null;
    /** Every selected context (primary + attached). */
    selectedContexts: string[];
    isLoading?: boolean;
    /** Context the switch is targeting, so the button can say "Switching to X…" rather
     * than just sitting disabled with the old context's name still showing. */
    pendingContext?: string | null;
    /** Promote a context to primary — a real context switch (POST /api/aks/context). */
    onChange: (context: string, defaultNamespace?: string) => void;
    /** Attach/detach a secondary context — no POST, just a read fan-out scope change. */
    onToggleAttached: (context: string) => void;
}

/**
 * Multi-context picker. Clicking a row promotes that context to primary (the single
 * configured context the actions default to); the checkbox attaches a context as a
 * secondary read source without touching the profile. The button shows the primary plus
 * a "+N" badge while extra clusters are attached.
 */
export function ContextSelector({
    contexts,
    currentContext,
    selectedContexts,
    isLoading,
    pendingContext,
    onChange,
    onToggleAttached,
}: ContextSelectorProps) {
    const [open, setOpen] = useState(false);
    const [search, setSearch] = useState("");
    const [highlight, setHighlight] = useState(0);
    const ref = useRef<HTMLDivElement>(null);
    const buttonRef = useRef<HTMLButtonElement>(null);

    useEffect(() => {
        function onDocClick(e: MouseEvent) {
            if (ref.current && !ref.current.contains(e.target as Node))
                setOpen(false);
        }
        document.addEventListener("mousedown", onDocClick);
        return () => document.removeEventListener("mousedown", onDocClick);
    }, []);

    const filtered = useMemo(
        () =>
            (contexts ?? []).filter(
                (ctx) =>
                    ctx.name.toLowerCase().includes(search.toLowerCase()) ||
                    (ctx.cluster ?? "")
                        .toLowerCase()
                        .includes(search.toLowerCase()),
            ),
        [contexts, search],
    );

    const sortedFiltered = useMemo(() => {
        // Current first, then most-recently-used — persisted by the workspace on each
        // successful switch — then alphabetical.
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
        // eslint-disable-next-line react-hooks/exhaustive-deps -- re-sort on open so persisted MRU is re-read
    }, [filtered, currentContext, open]);

    const [prevNav, setPrevNav] = useState({ search, open });
    if (prevNav.search !== search || prevNav.open !== open) {
        setPrevNav({ search, open });
        setHighlight(0);
    }

    const close = () => {
        setOpen(false);
        setSearch("");
        buttonRef.current?.focus();
    };

    const pick = (ctx: KubeContextInfo) => {
        if (ctx.name !== currentContext)
            onChange(ctx.name, ctx.namespace ?? undefined);
        close();
    };

    const onKeyDown = (e: React.KeyboardEvent) => {
        if (!open) return;
        if (e.key === "Escape") {
            e.preventDefault();
            close();
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

    const attachedCount = selectedContexts.length - (currentContext ? 1 : 0);
    const display = isLoading
        ? `Switching to ${pendingContext ?? "…"}`
        : currentContext || "Select context...";

    return (
        <div ref={ref} className="relative" onKeyDown={onKeyDown}>
            <button
                ref={buttonRef}
                type="button"
                onClick={() => !isLoading && setOpen((v) => !v)}
                disabled={isLoading}
                aria-haspopup="listbox"
                aria-expanded={open}
                title={display}
                className="flex min-w-[12rem] items-center justify-between rounded-md border bg-card px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-70"
                data-testid="aks-context-select"
            >
                <span className="flex min-w-0 items-center gap-1.5">
                    {isLoading && (
                        <Loader2 className="h-3.5 w-3.5 shrink-0 animate-spin" />
                    )}
                    <span className="truncate">{display}</span>
                    {attachedCount > 0 && (
                        <span
                            className="shrink-0 rounded bg-primary/15 px-1.5 py-0.5 text-xs font-medium text-primary"
                            data-testid="aks-context-attached-count"
                        >
                            +{attachedCount}
                        </span>
                    )}
                </span>
                <span className="text-muted-foreground">
                    {open ? "▲" : "▼"}
                </span>
            </button>

            {open && (
                <div
                    className="absolute z-50 mt-1 w-96 rounded-md border bg-popover shadow-md"
                    role="listbox"
                    aria-label="Kubernetes contexts"
                    aria-multiselectable="true"
                >
                    <div className="border-b p-2">
                        <input
                            autoFocus
                            role="combobox"
                            aria-expanded="true"
                            aria-activedescendant={`aks-context-option-row-${highlight}`}
                            value={search}
                            onChange={(e) => setSearch(e.target.value)}
                            placeholder="Filter contexts..."
                            className="w-full rounded border bg-background px-2 py-1 text-xs"
                            data-testid="aks-context-filter"
                        />
                    </div>
                    <div className="max-h-60 overflow-auto p-1">
                        {sortedFiltered.length === 0 && (
                            <div className="px-2 py-2 text-xs text-muted-foreground">
                                No matches found
                            </div>
                        )}
                        {sortedFiltered.map((ctx, i) => {
                            const isPrimary = ctx.name === currentContext;
                            const isAttached = selectedContexts.includes(
                                ctx.name,
                            );
                            const subtitle =
                                ctx.cluster || ctx.namespace
                                    ? `${ctx.cluster ?? ""}${ctx.cluster && ctx.namespace ? " · " : ""}${ctx.namespace ? `ns: ${ctx.namespace}` : ""}`
                                    : undefined;
                            return (
                                <div
                                    key={ctx.name}
                                    id={`aks-context-option-row-${i}`}
                                    role="option"
                                    aria-selected={isPrimary}
                                    onClick={() => pick(ctx)}
                                    onMouseEnter={() => setHighlight(i)}
                                    className={`w-full cursor-pointer rounded px-2 py-1.5 text-left text-sm hover:bg-accent ${isPrimary ? "bg-accent/50 font-medium" : ""} ${i === highlight ? "bg-accent/40" : ""}`}
                                    data-testid={`aks-context-option-${ctx.name}`}
                                >
                                    <div className="flex items-center gap-2">
                                        <input
                                            type="checkbox"
                                            checked={isAttached || isPrimary}
                                            disabled={isPrimary}
                                            title={
                                                isPrimary
                                                    ? "Primary context is always selected"
                                                    : isAttached
                                                      ? "Detach this cluster"
                                                      : "Attach this cluster to the merged view"
                                            }
                                            onClick={(e) => e.stopPropagation()}
                                            onChange={() =>
                                                onToggleAttached(ctx.name)
                                            }
                                            className="h-4 w-4"
                                            data-testid={`aks-context-attach-${ctx.name}`}
                                        />
                                        <div className="min-w-0 flex-1">
                                            <div className="flex items-center gap-2 truncate">
                                                <span className="truncate">
                                                    {ctx.name}
                                                </span>
                                                {isPrimary && (
                                                    <span
                                                        className="shrink-0 rounded bg-primary/15 px-1 py-0.5 text-[10px] font-medium text-primary"
                                                        data-testid="aks-context-primary-badge"
                                                    >
                                                        primary
                                                    </span>
                                                )}
                                            </div>
                                            {subtitle && (
                                                <div className="truncate text-xs text-muted-foreground">
                                                    {subtitle}
                                                </div>
                                            )}
                                        </div>
                                        {isPrimary ? (
                                            <Check className="h-3.5 w-3.5 shrink-0 text-primary" />
                                        ) : (
                                            <span className="h-3.5 w-3.5 shrink-0" />
                                        )}
                                    </div>
                                </div>
                            );
                        })}
                    </div>
                    <div className="border-t px-2 py-1.5 text-[11px] text-muted-foreground">
                        Checkbox merges a cluster into the view · clicking a row
                        makes it primary
                    </div>
                </div>
            )}
        </div>
    );
}
