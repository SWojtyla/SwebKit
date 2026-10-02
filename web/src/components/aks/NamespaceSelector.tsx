import { useState, useRef, useEffect, useMemo } from "react";
import { AlertTriangle, Check, Loader2 } from "lucide-react";
import type {
    AksNamespaceScope,
    NsSelection,
} from "./shared/aks-workspace-context";

interface NamespaceSelectorProps {
    /** One scope per selected context — its namespace list, loading, and error state. */
    scopes: AksNamespaceScope[];
    /** Resolved picks across contexts. */
    selected: NsSelection[];
    /** The default context — its picks encode bare (legacy URL shape) in the hidden select. */
    defaultContext: string | null;
    isLoading?: boolean;
    /**
     * What the button shows while `isLoading` — e.g. "Switching to staging…" during a context
     * switch vs. "Loading namespaces…" for the list fetch. Without it the button displayed
     * either stale names or a bare spinner with no idea what was happening.
     */
    loadingLabel?: string;
    /**
     * Primary context's namespace-list error, shown next to the button. Per-context errors
     * also render inside their dropdown group.
     */
    error?: string | null;
    onChange: (selected: NsSelection[]) => void;
    /** Set on cluster-scoped tabs (e.g. GatewayClasses) where a namespace choice has no effect,
     * so the control doesn't sit there fully live and interactive while silently doing nothing. */
    disabledReason?: string;
}

function selKey(context: string, namespace: string): string {
    return `${context}:${namespace}`;
}

export function NamespaceSelector({
    scopes,
    selected,
    defaultContext,
    isLoading,
    loadingLabel,
    error,
    onChange,
    disabledReason,
}: NamespaceSelectorProps) {
    const [open, setOpen] = useState(false);
    const [search, setSearch] = useState("");
    const [pending, setPending] = useState<NsSelection[]>(selected);
    const ref = useRef<HTMLDivElement>(null);

    // Re-snapshot the external selection whenever it moves or the dropdown toggles —
    // done during render so the draft never paints stale for a frame.
    const [prevSync, setPrevSync] = useState({ selected, open });
    if (prevSync.selected !== selected || prevSync.open !== open) {
        setPrevSync({ selected, open });
        setPending(selected);
    }

    useEffect(() => {
        function onDocClick(e: MouseEvent) {
            if (ref.current && !ref.current.contains(e.target as Node))
                setOpen(false);
        }
        if (open) document.addEventListener("mousedown", onDocClick);
        return () => document.removeEventListener("mousedown", onDocClick);
    }, [open]);

    const pendingKeys = useMemo(
        () => new Set(pending.map((s) => selKey(s.context, s.namespace))),
        [pending],
    );
    const selectedKeys = useMemo(
        () => new Set(selected.map((s) => selKey(s.context, s.namespace))),
        [selected],
    );
    const multiContext = scopes.length > 1;
    const needle = search.toLowerCase();

    const isAllSelected =
        scopes.length > 0 &&
        scopes.every((scope) => {
            const all = scope.namespaces ?? [];
            if (all.length === 0) return false;
            const picked = selected.filter((s) => s.context === scope.context);
            return (
                picked.some((s) => s.namespace === "*") ||
                picked.length === all.length
            );
        });

    const display = isLoading
        ? (loadingLabel ?? "Loading namespaces…")
        : selected.length === 0
          ? "Select namespace..."
          : isAllSelected
            ? "All namespaces"
            : selected.length === 1
              ? selected[0].namespace === "*"
                  ? "All namespaces"
                  : multiContext
                    ? `${selected[0].context}: ${selected[0].namespace}`
                    : selected[0].namespace
              : `${selected.length} namespaces${multiContext ? ` · ${scopes.length} clusters` : ""}`;

    const toggleNs = (context: string, ns: string) => {
        setPending((prev) =>
            prev.some((s) => s.context === context && s.namespace === ns)
                ? prev.filter(
                      (s) => !(s.context === context && s.namespace === ns),
                  )
                : [...prev, { context, namespace: ns }],
        );
    };

    // A single namespace row applies immediately, like the adjacent context selector —
    // no reason to make the common single-choice case wait on a separate Apply click. The
    // checkbox itself (stopPropagation'd below) is the escape hatch for building a multi-select,
    // which still needs the explicit Apply below since "add these 3" isn't a single atomic choice.
    const selectSingle = (context: string, ns: string) => {
        onChange([{ context, namespace: ns }]);
        setOpen(false);
        setSearch("");
    };

    const hasChanges =
        pending.length !== selected.length ||
        pending.some((s) => !selectedKeys.has(selKey(s.context, s.namespace)));
    const apply = () => {
        onChange(pending);
        setOpen(false);
        setSearch("");
    };

    const selectAll = () =>
        setPending(
            scopes.flatMap((s) =>
                (s.namespaces ?? []).map((namespace) => ({
                    context: s.context,
                    namespace,
                })),
            ),
        );
    const selectNone = () => setPending([]);

    const onKeyDown = (e: React.KeyboardEvent) => {
        if (open && e.key === "Escape") {
            e.preventDefault();
            setOpen(false);
        }
    };

    // Hidden native select keeps Playwright tests working — see the note on `sr-only` below.
    // Values: bare namespace names for the primary context (the legacy shape tests rely on),
    // `ctx:ns` for attached contexts. `ctx` may itself contain `:` (EKS ARNs) — namespaces
    // never do, so onChange splits at the LAST colon.
    const selectValue = selected.map((s) =>
        s.context === defaultContext
            ? s.namespace
            : `${s.context}:${s.namespace}`,
    );
    const parseSelectValue = (value: string): NsSelection => {
        const colon = value.lastIndexOf(":");
        if (colon === -1)
            return { context: defaultContext ?? "", namespace: value };
        return {
            context: value.slice(0, colon),
            namespace: value.slice(colon + 1),
        };
    };
    const firstScope = scopes[0];

    return (
        <div
            ref={ref}
            className="relative flex items-center gap-2"
            onKeyDown={onKeyDown}
        >
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
                value={selectValue}
                onChange={(e) => {
                    const options = Array.from(e.target.selectedOptions).map(
                        (o) => o.value,
                    );
                    onChange(
                        options.length
                            ? options.map(parseSelectValue)
                            : firstScope?.namespaces?.length
                              ? [
                                    {
                                        context: firstScope.context,
                                        namespace: firstScope.namespaces[0],
                                    },
                                ]
                              : [],
                    );
                }}
                className="sr-only"
            >
                {scopes.map((scope) =>
                    (scope.namespaces ?? []).length === 0 ? null : (
                        <optgroup key={scope.context} label={scope.context}>
                            <option
                                value={
                                    scope.context === defaultContext
                                        ? "*"
                                        : `${scope.context}:*`
                                }
                            >
                                All namespaces
                            </option>
                            {(scope.namespaces ?? []).map((ns) => (
                                <option
                                    key={ns}
                                    value={
                                        scope.context === defaultContext
                                            ? ns
                                            : `${scope.context}:${ns}`
                                    }
                                >
                                    {ns}
                                </option>
                            ))}
                        </optgroup>
                    ),
                )}
            </select>

            <button
                type="button"
                onClick={() =>
                    !isLoading && !disabledReason && setOpen((v) => !v)
                }
                disabled={isLoading || !!disabledReason}
                aria-haspopup="listbox"
                aria-expanded={open}
                title={disabledReason ?? display}
                className="flex min-w-[14rem] max-w-[24rem] items-center justify-between rounded-md border bg-card px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
                data-testid="aks-namespace-dropdown"
            >
                <span className="flex min-w-0 items-center gap-1.5">
                    {isLoading && (
                        <Loader2 className="h-3.5 w-3.5 shrink-0 animate-spin" />
                    )}
                    <span className="truncate">{display}</span>
                </span>
                <span className="text-muted-foreground">
                    {open ? "▲" : "▼"}
                </span>
            </button>

            {isAllSelected && (
                <span
                    className="text-sm text-primary"
                    title="All namespaces selected"
                >
                    *
                </span>
            )}

            {error && (
                <span
                    className="flex items-center gap-1 text-xs text-destructive"
                    title={error}
                    data-testid="aks-namespace-error"
                >
                    <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
                    <span className="max-w-[18rem] truncate">
                        Namespaces unavailable
                    </span>
                </span>
            )}

            {open && (
                <div
                    className="absolute top-full z-50 mt-1 w-96 rounded-md border bg-popover shadow-md"
                    data-testid="aks-namespace-menu"
                >
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
                        {!multiContext && defaultContext && (
                            <div className="mt-1 flex gap-2 text-xs text-muted-foreground">
                                <span
                                    className="truncate"
                                    data-testid="aks-namespace-context"
                                >
                                    {defaultContext} ›
                                </span>
                                <span>
                                    {scopes[0]?.namespaces?.length ?? 0} total
                                </span>
                            </div>
                        )}
                    </div>
                    <div className="max-h-72 overflow-auto p-1">
                        {scopes.map((scope) => {
                            const all = scope.namespaces ?? [];
                            const filtered = all.filter((ns) =>
                                ns.toLowerCase().includes(needle),
                            );
                            const sortedFiltered = [...filtered].sort(
                                (a, b) => {
                                    const aSelected = pendingKeys.has(
                                        selKey(scope.context, a),
                                    );
                                    const bSelected = pendingKeys.has(
                                        selKey(scope.context, b),
                                    );
                                    if (aSelected && !bSelected) return -1;
                                    if (!aSelected && bSelected) return 1;
                                    return a.localeCompare(b);
                                },
                            );
                            return (
                                <div
                                    key={scope.context}
                                    data-testid={`aks-ns-group-${scope.context}`}
                                >
                                    {multiContext && (
                                        <div className="mt-1 flex items-center justify-between px-2 py-1 text-xs font-medium text-muted-foreground">
                                            <span
                                                className="truncate"
                                                title={scope.context}
                                            >
                                                {scope.context}
                                            </span>
                                            <span className="flex gap-2">
                                                <button
                                                    type="button"
                                                    onClick={() =>
                                                        setPending((prev) => [
                                                            ...prev.filter(
                                                                (s) =>
                                                                    s.context !==
                                                                    scope.context,
                                                            ),
                                                            ...all.map(
                                                                (
                                                                    namespace,
                                                                ) => ({
                                                                    context:
                                                                        scope.context,
                                                                    namespace,
                                                                }),
                                                            ),
                                                        ])
                                                    }
                                                    className="hover:text-foreground"
                                                >
                                                    All
                                                </button>
                                                <button
                                                    type="button"
                                                    onClick={() =>
                                                        setPending((prev) =>
                                                            prev.filter(
                                                                (s) =>
                                                                    s.context !==
                                                                    scope.context,
                                                            ),
                                                        )
                                                    }
                                                    className="hover:text-foreground"
                                                >
                                                    None
                                                </button>
                                            </span>
                                        </div>
                                    )}
                                    {scope.isLoading && (
                                        <div className="flex items-center gap-2 px-2 py-2 text-xs text-muted-foreground">
                                            <Loader2 className="h-3.5 w-3.5 animate-spin" />
                                            Loading…
                                        </div>
                                    )}
                                    {scope.error && (
                                        <div
                                            className="flex items-start gap-2 px-2 py-2 text-xs text-destructive"
                                            data-testid={`aks-namespace-error-detail${multiContext ? `-${scope.context}` : ""}`}
                                        >
                                            <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                                            <span>{scope.error}</span>
                                        </div>
                                    )}
                                    {!scope.isLoading &&
                                        !scope.error &&
                                        sortedFiltered.length === 0 && (
                                            <div className="px-2 py-2 text-xs text-muted-foreground">
                                                No namespaces found
                                            </div>
                                        )}
                                    {sortedFiltered.map((ns) => {
                                        const isSelected = pendingKeys.has(
                                            selKey(scope.context, ns),
                                        );
                                        return (
                                            <label
                                                key={ns}
                                                onClick={() =>
                                                    selectSingle(
                                                        scope.context,
                                                        ns,
                                                    )
                                                }
                                                className={`flex cursor-pointer items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-accent ${isSelected ? "bg-accent/40" : ""}`}
                                                title="Click to select just this namespace, or use the checkbox to build a multi-namespace selection"
                                            >
                                                <input
                                                    type="checkbox"
                                                    checked={isSelected}
                                                    onClick={(e) =>
                                                        e.stopPropagation()
                                                    }
                                                    onChange={() =>
                                                        toggleNs(
                                                            scope.context,
                                                            ns,
                                                        )
                                                    }
                                                    className="h-4 w-4"
                                                />
                                                <span className="flex-1 truncate">
                                                    {ns}
                                                </span>
                                                {isSelected && (
                                                    <Check className="h-3.5 w-3.5 text-primary" />
                                                )}
                                            </label>
                                        );
                                    })}
                                </div>
                            );
                        })}
                    </div>
                    <div className="flex items-center justify-between border-t p-2 text-xs">
                        <div className="flex gap-2">
                            <button
                                type="button"
                                onClick={selectAll}
                                className="rounded px-2 py-1 hover:bg-accent"
                            >
                                All
                            </button>
                            <button
                                type="button"
                                onClick={selectNone}
                                className="rounded px-2 py-1 hover:bg-accent"
                            >
                                None
                            </button>
                        </div>
                        <button
                            type="button"
                            onClick={apply}
                            disabled={!hasChanges}
                            title={
                                !hasChanges
                                    ? "No changes to apply — tick namespaces to build a multi-selection, or click a name to select just it"
                                    : undefined
                            }
                            className="rounded bg-primary px-3 py-1 text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
                            data-testid="aks-namespace-apply"
                        >
                            Apply {pending.length > 0 && `(${pending.length})`}
                        </button>
                    </div>
                </div>
            )}
        </div>
    );
}
