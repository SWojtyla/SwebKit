import { useEffect, useMemo, useRef, useState } from "react";
import type { KubeContextInfo } from "@/lib/types";
import { CheckSquare, Square } from "lucide-react";

interface ContextSelectorProps {
    contexts: KubeContextInfo[] | undefined;
    /** Every selected context — the merged view queries all of them. */
    selectedContexts: string[];
    /** Add/remove a context from the selection. */
    onToggle: (context: string) => void;
    /** Replace the selection with this context alone. */
    onSelectOnly: (context: string) => void;
}

/**
 * Multi-select context picker: every checked context feeds the merged view and gets
 * its own namespace group in the namespace picker. There is no "primary".
 *
 * Click semantics mirror the namespace picker beside it: clicking a row selects
 * just that one cluster and closes; the checkbox toggles it into the merged set.
 */
export function ContextSelector({
    contexts,
    selectedContexts,
    onToggle,
    onSelectOnly,
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
        // Selected first (so what's in view is visible at a glance), then alphabetical.
        const selected = new Set(selectedContexts);
        return [...filtered].sort((a, b) => {
            const aSel = selected.has(a.name);
            const bSel = selected.has(b.name);
            if (aSel && !bSel) return -1;
            if (!aSel && bSel) return 1;
            return a.name.localeCompare(b.name);
        });
        // eslint-disable-next-line react-hooks/exhaustive-deps -- re-sort on open so selection changes re-order
    }, [filtered, selectedContexts, open]);

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
            onSelectOnly(sortedFiltered[highlight].name);
            close();
        }
    };

    const display =
        selectedContexts.length === 0
            ? "Select contexts…"
            : selectedContexts.length === 1
              ? selectedContexts[0]
              : `${selectedContexts.length} contexts`;

    return (
        <div ref={ref} className="relative" onKeyDown={onKeyDown}>
            <button
                ref={buttonRef}
                type="button"
                onClick={() => setOpen((v) => !v)}
                aria-haspopup="listbox"
                aria-expanded={open}
                title={display}
                className="flex h-7 min-w-[12rem] items-center justify-between rounded-md border bg-surface-1 px-2.5 text-xs hover:bg-surface-2 disabled:opacity-70"
                data-testid="aks-context-select"
            >
                <span className="flex min-w-0 items-center gap-1.5">
                    <span className="truncate">{display}</span>
                </span>
                <span className="text-fg-muted">{open ? "▲" : "▼"}</span>
            </button>

            {open && (
                <div
                    className="absolute z-50 mt-1 w-96 rounded-lg border bg-popover shadow-md"
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
                            className="h-7 w-full rounded-md border bg-surface-1 px-2 text-xs placeholder:text-fg-subtle"
                            data-testid="aks-context-filter"
                        />
                    </div>
                    <div className="max-h-60 overflow-auto p-1">
                        {sortedFiltered.length === 0 && (
                            <div className="px-2 py-2 text-xs text-fg-muted">
                                No matches found
                            </div>
                        )}
                        {sortedFiltered.map((ctx, i) => {
                            const isSelected = selectedContexts.includes(
                                ctx.name,
                            );
                            const isLastSelected =
                                isSelected && selectedContexts.length === 1;
                            const subtitle =
                                ctx.cluster || ctx.namespace
                                    ? `${ctx.cluster ?? ""}${ctx.cluster && ctx.namespace ? " · " : ""}${ctx.namespace ? `ns: ${ctx.namespace}` : ""}`
                                    : undefined;
                            return (
                                <div
                                    key={ctx.name}
                                    id={`aks-context-option-row-${i}`}
                                    role="option"
                                    aria-selected={isSelected}
                                    title={`Select only ${ctx.name} — or tick the box to combine clusters`}
                                    onClick={() => {
                                        onSelectOnly(ctx.name);
                                        close();
                                    }}
                                    onMouseEnter={() => setHighlight(i)}
                                    className={`w-full cursor-pointer rounded px-2 py-1.5 text-left text-sm hover:bg-accent ${i === highlight ? "bg-accent/40" : ""}`}
                                    data-testid={`aks-context-option-${ctx.name}`}
                                >
                                    <div className="flex items-center gap-2">
                                        <input
                                            type="checkbox"
                                            checked={isSelected}
                                            disabled={isLastSelected}
                                            title={
                                                isLastSelected
                                                    ? "At least one cluster must stay selected"
                                                    : isSelected
                                                      ? "Remove this cluster from the view"
                                                      : "Add this cluster to the view"
                                            }
                                            onClick={(e) => e.stopPropagation()}
                                            onChange={() => onToggle(ctx.name)}
                                            className="h-3.5 w-3.5 accent-primary"
                                            data-testid={`aks-context-check-${ctx.name}`}
                                        />
                                        <div className="min-w-0 flex-1">
                                            <div className="truncate">
                                                {ctx.name}
                                            </div>
                                            {subtitle && (
                                                <div className="truncate text-xs text-fg-muted">
                                                    {subtitle}
                                                </div>
                                            )}
                                        </div>
                                        <button
                                            type="button"
                                            title={`View only ${ctx.name}`}
                                            aria-label={`Select only ${ctx.name}`}
                                            onClick={(e) => {
                                                e.stopPropagation();
                                                onSelectOnly(ctx.name);
                                                close();
                                            }}
                                            className="inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-md text-fg-muted hover:bg-surface-2 hover:text-fg"
                                            data-testid={`aks-context-only-${ctx.name}`}
                                        >
                                            {isSelected &&
                                            selectedContexts.length === 1 ? (
                                                <CheckSquare className="h-3.5 w-3.5 text-primary" />
                                            ) : (
                                                <Square className="h-3.5 w-3.5" />
                                            )}
                                        </button>
                                    </div>
                                </div>
                            );
                        })}
                    </div>
                    <div className="border-t px-2 py-1.5 text-xs text-fg-subtle">
                        Click a row to select just it · tick boxes to combine
                        clusters in the merged view
                    </div>
                </div>
            )}
        </div>
    );
}
