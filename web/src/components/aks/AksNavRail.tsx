import { useEffect, useMemo, useState } from "react";
import {
    ChevronDown,
    ChevronRight,
    PanelLeftClose,
    PanelLeftOpen,
} from "lucide-react";
import {
    loadNavCollapsedGroups,
    navGroups,
    navGroupForTab,
    navItemForTab,
    NAV_RAIL_MIN_PREF,
    saveNavCollapsedGroups,
    useAksNav,
    type AksNavItem,
    type NavGroupId,
} from "./shared/aks-workspace-context";

/**
 * Left navigator rail for the 18 resource views — `docs/prototypes/aks/rail.html`
 * brought to life. Replaces the dropdown nav bar: sections are always visible so
 * every view stays one click away, and each group header collapses its items.
 *
 * Two deliberate divergences from the prototype:
 * - Group headers are collapsible (the prototype's are static labels) — the whole
 *   reason this layout won.
 * - No per-item counts: only the pods list is fetched at page level, so badges on
 *   17 other items would cost 17 extra queries for decoration.
 *
 * Testid contract kept from the nav bar: `aks-nav-group-<id>` carries
 * `aria-expanded`, `aks-tab-<id>` marks each item, `aks-nav-current` anchors the
 * "where am I" readout — `openAksTab` in e2e keeps working unchanged.
 */
export function AksNavRail() {
    const { activeTab, setActiveTab } = useAksNav();
    const [collapsed, setCollapsed] = useState<Set<NavGroupId>>(() =>
        loadNavCollapsedGroups(localStorage),
    );
    const [minimized, setMinimized] = useState(
        () => localStorage.getItem(NAV_RAIL_MIN_PREF) === "1",
    );
    const [filter, setFilter] = useState("");

    const activeGroup = navGroupForTab(activeTab);
    const activeItem = navItemForTab(activeTab);

    // A tab switched from anywhere else (deep link, in-table cross-link) must be
    // visible in the rail — a collapsed section containing it re-expands. Done as
    // a render-time adjustment (React's documented pattern): only fires when the
    // tab actually changes, so collapsing the active tab's own section stays
    // possible.
    const [prevTab, setPrevTab] = useState(activeTab);
    if (prevTab !== activeTab) {
        setPrevTab(activeTab);
        const group = navGroupForTab(activeTab);
        if (group && collapsed.has(group.id)) {
            setCollapsed((prev) => {
                const next = new Set(prev);
                next.delete(group.id);
                return next;
            });
        }
    }

    useEffect(() => {
        saveNavCollapsedGroups(collapsed, localStorage);
    }, [collapsed]);

    useEffect(() => {
        localStorage.setItem(NAV_RAIL_MIN_PREF, minimized ? "1" : "0");
    }, [minimized]);

    const toggleGroup = (id: NavGroupId) =>
        setCollapsed((prev) => {
            const next = new Set(prev);
            if (next.has(id)) next.delete(id);
            else next.add(id);
            return next;
        });

    const query = filter.trim().toLowerCase();
    const visibleItems = useMemo(() => {
        const map = new Map<NavGroupId, AksNavItem[]>();
        for (const g of navGroups) {
            const items: AksNavItem[] = query
                ? g.items.filter((i) => i.label.toLowerCase().includes(query))
                : [...g.items];
            if (items.length) map.set(g.id, items);
        }
        return map;
    }, [query]);

    const firstMatch = navGroups
        .flatMap((g) => visibleItems.get(g.id) ?? [])
        .at(0);

    return (
        <nav
            className={`flex flex-col border-r bg-card/30 ${
                minimized ? "w-11" : "w-56"
            }`}
            data-testid="aks-tabs"
            aria-label="AKS resource views"
        >
            <div className="flex items-center gap-1 border-b p-1.5">
                {!minimized && (
                    <input
                        type="text"
                        value={filter}
                        onChange={(e) => setFilter(e.target.value)}
                        onKeyDown={(e) => {
                            if (e.key === "Enter" && firstMatch) {
                                setActiveTab(firstMatch.id);
                                setFilter("");
                            }
                        }}
                        placeholder="Filter resources…"
                        data-testid="aks-nav-filter"
                        className="h-6 min-w-0 flex-1 rounded border bg-background px-2 text-xs outline-none focus:border-primary"
                    />
                )}
                <button
                    type="button"
                    onClick={() => setMinimized((v) => !v)}
                    title={
                        minimized ? "Expand navigator" : "Collapse navigator"
                    }
                    aria-label={
                        minimized ? "Expand navigator" : "Collapse navigator"
                    }
                    data-testid="aks-nav-minimize"
                    className="ml-auto shrink-0 rounded p-1 text-muted-foreground hover:bg-accent hover:text-foreground"
                >
                    {minimized ? (
                        <PanelLeftOpen className="h-3.5 w-3.5" />
                    ) : (
                        <PanelLeftClose className="h-3.5 w-3.5" />
                    )}
                </button>
            </div>

            <div className="flex-1 overflow-y-auto py-1">
                {navGroups.map((group) => {
                    const items = visibleItems.get(group.id) ?? [];
                    if (query && items.length === 0) return null;
                    // Minimized rail ignores collapse state — every icon stays
                    // reachable; the collapsed set itself is preserved.
                    const expanded =
                        minimized ||
                        (query ? items.length > 0 : !collapsed.has(group.id));
                    const holdsActive = activeGroup?.id === group.id;
                    return (
                        <div key={group.id}>
                            {minimized ? (
                                <div className="mx-2 my-2 border-t" />
                            ) : (
                                <button
                                    type="button"
                                    onClick={() => toggleGroup(group.id)}
                                    aria-expanded={expanded}
                                    data-testid={`aks-nav-group-${group.id}`}
                                    title={group.desc}
                                    className="flex w-full items-center gap-1 px-2.5 pb-0.5 pt-2.5 text-[10px] font-bold uppercase tracking-wider text-muted-foreground hover:text-foreground"
                                >
                                    {expanded ? (
                                        <ChevronDown className="h-3 w-3 shrink-0" />
                                    ) : (
                                        <ChevronRight className="h-3 w-3 shrink-0" />
                                    )}
                                    <span className="truncate">
                                        {group.label}
                                    </span>
                                    {holdsActive && (
                                        <span className="ml-auto h-1.5 w-1.5 shrink-0 rounded-full bg-primary" />
                                    )}
                                </button>
                            )}
                            {expanded && (
                                <div data-testid={`aks-nav-menu-${group.id}`}>
                                    {items.map((item) => {
                                        const isActive = item.id === activeTab;
                                        return (
                                            <button
                                                key={item.id}
                                                type="button"
                                                onClick={() =>
                                                    setActiveTab(item.id)
                                                }
                                                title={
                                                    minimized
                                                        ? `${item.label} — ${item.desc}`
                                                        : item.desc
                                                }
                                                data-testid={`aks-tab-${item.id}`}
                                                className={`flex w-full items-center gap-2 border-l-2 py-1 text-left text-[13px] ${
                                                    minimized
                                                        ? "justify-center px-0"
                                                        : "px-3"
                                                } ${
                                                    isActive
                                                        ? "border-primary bg-primary/10 font-semibold text-primary"
                                                        : "border-transparent text-foreground hover:bg-accent"
                                                }`}
                                            >
                                                <span
                                                    className={`w-4 shrink-0 text-center text-xs ${
                                                        isActive
                                                            ? "text-primary"
                                                            : "text-muted-foreground"
                                                    }`}
                                                >
                                                    {item.glyph}
                                                </span>
                                                {!minimized && (
                                                    <span className="truncate">
                                                        {item.label}
                                                    </span>
                                                )}
                                            </button>
                                        );
                                    })}
                                </div>
                            )}
                        </div>
                    );
                })}
                {query && firstMatch === undefined && (
                    <p
                        className="px-3 py-2 text-xs text-muted-foreground"
                        data-testid="aks-nav-no-results"
                    >
                        No resources match "{filter.trim()}"
                    </p>
                )}
            </div>

            {/* Where-am-I footer: the rail highlights the active item, but a
                collapsed section can hide it — the breadcrumb keeps the anchor. */}
            <div
                className="truncate border-t px-2.5 py-1.5 text-[11px] text-muted-foreground"
                data-testid="aks-nav-current"
            >
                {minimized
                    ? activeItem?.glyph
                    : activeGroup && activeItem
                      ? `${activeGroup.label} › ${activeItem.label}`
                      : null}
            </div>
        </nav>
    );
}
