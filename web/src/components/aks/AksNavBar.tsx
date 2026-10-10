import { useEffect, useRef } from "react";
import {
    navGroups,
    navGroupForTab,
    navItemForTab,
    useAksNav,
    type NavGroupId,
} from "./shared/aks-workspace-context";

/**
 * The 18 resource views behind 4 task-grouped dropdowns (Workloads / Configuration /
 * Network / Operations), replacing the flat tab strip that overflowed at ~1280px and
 * kept the Network views behind a second-class submenu row. The strip was also a layout
 * liar — the submenu pushed content down on open; these panels float over it instead.
 *
 * Per-item counts were deliberately left out of the menus: only the pods list is
 * fetched at page level (`useAksQueries`), so a badge on every item would mean lifting
 * 17 more queries into the page just to decorate a menu.
 */
export function AksNavBar() {
    const { activeTab, setActiveTab, openNavGroup, setOpenNavGroup } =
        useAksNav();
    const navRef = useRef<HTMLElement>(null);
    const activeGroup = navGroupForTab(activeTab);
    const activeItem = navItemForTab(activeTab);

    // Outside-click + Esc close — same pattern as ContextMenu, minus the scroll
    // close (the bar is sticky; scrolling the table beneath must not dismiss it).
    useEffect(() => {
        if (openNavGroup === null) return;
        const handleClickOutside = (e: MouseEvent) => {
            if (
                navRef.current &&
                !navRef.current.contains(e.target as Node)
            ) {
                setOpenNavGroup(null);
            }
        };
        const handleEscape = (e: KeyboardEvent) => {
            if (e.key === "Escape") setOpenNavGroup(null);
        };
        document.addEventListener("mousedown", handleClickOutside);
        document.addEventListener("keydown", handleEscape);
        return () => {
            document.removeEventListener("mousedown", handleClickOutside);
            document.removeEventListener("keydown", handleEscape);
        };
    }, [openNavGroup, setOpenNavGroup]);

    const toggleGroup = (id: NavGroupId) =>
        setOpenNavGroup((v) => (v === id ? null : id));

    return (
        <nav
            ref={navRef}
            className="relative flex items-center gap-0.5 border-b px-2"
            data-testid="aks-tabs"
            aria-label="AKS resource views"
        >
            {navGroups.map((group) => {
                const isActiveGroup = activeGroup?.id === group.id;
                const isOpen = openNavGroup === group.id;
                return (
                    <div key={group.id} className="relative">
                        <button
                            type="button"
                            onClick={() => toggleGroup(group.id)}
                            aria-haspopup="menu"
                            aria-expanded={isOpen}
                            data-testid={`aks-nav-group-${group.id}`}
                            className={`flex items-center gap-1.5 whitespace-nowrap border-b-2 px-3 py-2 text-sm font-semibold ${
                                isActiveGroup || isOpen
                                    ? "border-primary text-foreground"
                                    : "border-transparent text-muted-foreground hover:text-foreground"
                            }`}
                        >
                            {group.label}
                            <span className="text-[10px] opacity-70">
                                {isOpen ? "▲" : "▼"}
                            </span>
                        </button>
                        {isOpen && (
                            <div
                                role="menu"
                                aria-label={`${group.label} views`}
                                data-testid={`aks-nav-menu-${group.id}`}
                                className="absolute left-0 top-full z-40 mt-px min-w-[290px] rounded-lg border bg-popover p-1.5 shadow-lg"
                            >
                                <div className="mb-1 border-b px-2.5 pb-1.5 pt-1 text-[11px] text-muted-foreground">
                                    {group.desc}
                                </div>
                                {group.items.map((item) => {
                                    const isActive = item.id === activeTab;
                                    return (
                                        <button
                                            key={item.id}
                                            type="button"
                                            role="menuitem"
                                            onClick={() => {
                                                setActiveTab(item.id);
                                                setOpenNavGroup(null);
                                            }}
                                            data-testid={`aks-tab-${item.id}`}
                                            className={`flex w-full items-baseline gap-2.5 rounded-md px-2.5 py-1.5 text-left hover:bg-accent ${
                                                isActive
                                                    ? "bg-accent text-primary"
                                                    : "text-foreground"
                                            }`}
                                        >
                                            <span className="text-[13px] font-semibold">
                                                <span className="mr-1.5 inline-block w-4 text-center text-xs text-muted-foreground">
                                                    {item.glyph}
                                                </span>
                                                {item.label}
                                            </span>
                                            <span className="flex-1 text-[11px] text-muted-foreground">
                                                {item.desc}
                                            </span>
                                        </button>
                                    );
                                })}
                            </div>
                        )}
                    </div>
                );
            })}
            {/* Where-am-I chip: the menus hide inactive views, so the current one has
                to stay visible here or the user loses the "Pods" anchor entirely. */}
            <span
                className="ml-auto flex items-center gap-1.5 px-2 text-xs text-muted-foreground"
                data-testid="aks-nav-current"
            >
                {activeGroup && activeItem && (
                    <>
                        {activeGroup.label}
                        <span className="font-semibold text-foreground">
                            › {activeItem.label}
                        </span>
                    </>
                )}
            </span>
        </nav>
    );
}
