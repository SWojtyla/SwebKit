import { useEffect, useRef, useState, type ReactNode } from "react";
import { ListFilter } from "lucide-react";
import {
    loadViewPreference,
    saveViewPreference,
} from "@/lib/stores/panel-preferences";
import {
    detectNewItemId,
    filterItems,
    groupItems,
    resolveSelectedItem,
} from "./profile-list-utils";

export interface ProfileListLayoutProps<T> {
    /** The entries listed on the left — one row each, editor shown for the selected one. */
    items: T[];
    getKey: (item: T) => string;
    /** Primary list-row text — usually the display name/alias. */
    getTitle: (item: T) => string;
    /** Secondary muted line in the row (e.g. the host/server it points at). */
    getSubtitle?: (item: T) => string;
    /** Optional grouping for the list (SQL groups connections under their server). */
    getGroup?: (item: T) => string;
    /** Marks the entry the app will actually use — shown as an "Active" badge. */
    isActive?: (item: T) => boolean;
    /** Extra text matched by the filter box, beyond title/subtitle/group. */
    getFilterText?: (item: T) => string;
    /** Editor for the selected entry, rendered in the right pane. Second arg is the
     * item's index in `items` for index-based test ids. */
    renderEditor: (item: T, index: number) => ReactNode;
    /** Optional per-group affordance rendered on group headers (SQL's "add database"). */
    renderGroupAction?: (group: string) => ReactNode;
    /** Shown when `items` is empty — the list/editor split is skipped entirely. */
    emptyMessage: ReactNode;
    /** Extra content above the split view (e.g. Redis's namespace separator field). */
    above?: ReactNode;
    /**
     * Prefix for generated test ids: `{prefix}-item-{id}` (list rows),
     * `{prefix}-filter` (filter box), `{prefix}-list`/`{prefix}-detail` (panes).
     * Also namespaces the persisted selection under `view-pref:{prefix}-selected`.
     */
    testIdPrefix: string;
}

/**
 * Master-detail wrapper for settings sections that manage a list of configured things
 * (Service Bus namespaces, Redis caches, …). Rendering a full editor card per entry
 * didn't scale — ten Redis caches meant a wall of forms — so the list lives in a
 * compact left column and only the selected entry's editor mounts on the right.
 *
 * Selection semantics, chosen to keep e2e tests and user expectations honest:
 * - A newly-added entry is auto-selected (the add flows just append to `items`; the
 *   layout notices the new key and selects it), so tests resolving `.last()`-style
 *   editors always land on the new one.
 * - Removing the selected entry moves selection to the first remaining/active entry.
 * - The selection is persisted under `view-pref:` (same store as panel sizes), so a
 *   reload re-opens the entry that was being edited.
 */
export function ProfileListLayout<T>({
    items,
    getKey,
    getTitle,
    getSubtitle,
    getGroup,
    isActive,
    getFilterText,
    renderEditor,
    renderGroupAction,
    emptyMessage,
    above,
    testIdPrefix,
}: ProfileListLayoutProps<T>) {
    const selectionPrefKey = `${testIdPrefix}-selected`;
    const [selectedId, setSelectedId] = useState<string | null>(
        () => loadViewPreference<string>(selectionPrefKey, "") || null,
    );
    const [filter, setFilter] = useState("");

    // Resolve the effective selection during render: a stored/removed id simply falls
    // through to the active entry, then the first one. `selectedId` itself is only set
    // by a click or the add-detection effect below.
    const selectedItem = resolveSelectedItem(
        items,
        selectedId,
        getKey,
        isActive,
    );
    const effectiveSelectedId = selectedItem ? getKey(selectedItem) : null;

    useEffect(() => {
        if (effectiveSelectedId)
            saveViewPreference(selectionPrefKey, effectiveSelectedId);
    }, [effectiveSelectedId, selectionPrefKey]);

    // Auto-select entries that appear in `items` — that's how "Add X" focuses the new
    // row's editor without each settings page having to wire selection itself. The ref
    // starts null so the first effect run (initial mount) doesn't count every entry as
    // "new" and clobber a selection restored from the preference above.
    const prevIdsRef = useRef<Set<string> | null>(null);
    useEffect(() => {
        const newId = detectNewItemId(items, getKey, prevIdsRef.current);
        if (newId) setSelectedId(newId);
        prevIdsRef.current = new Set(items.map(getKey));
    }, [items, getKey]);

    const filtered = filterItems(items, filter, (item) => [
        getTitle(item),
        getSubtitle?.(item),
        getGroup?.(item),
        getFilterText?.(item),
    ]);
    const groups = getGroup ? groupItems(filtered, getGroup) : null;
    const term = filter.trim();

    const renderRow = (item: T) => {
        const id = getKey(item);
        const selected = id === effectiveSelectedId;
        return (
            <li key={id}>
                <button
                    type="button"
                    onClick={() => setSelectedId(id)}
                    aria-current={selected}
                    data-testid={`${testIdPrefix}-item-${id}`}
                    className={`flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm ${
                        selected
                            ? "bg-primary/15 font-medium"
                            : "text-muted-foreground hover:bg-accent hover:text-accent-foreground"
                    }`}
                >
                    <span className="min-w-0 flex-1">
                        <span className="block truncate">{getTitle(item)}</span>
                        {getSubtitle && (
                            <span className="block truncate text-xs text-muted-foreground">
                                {getSubtitle(item)}
                            </span>
                        )}
                    </span>
                    {isActive?.(item) && (
                        <span
                            className="shrink-0 rounded bg-success/15 px-1.5 py-0.5 text-[10px] font-medium text-success"
                            data-testid={`${testIdPrefix}-active-${id}`}
                        >
                            Active
                        </span>
                    )}
                </button>
            </li>
        );
    };

    return (
        <div className="space-y-3" data-testid={`${testIdPrefix}-profiles`}>
            {above}
            {items.length === 0 ? (
                <p className="text-sm text-muted-foreground">{emptyMessage}</p>
            ) : (
                <div className="flex gap-4">
                    <div className="w-56 shrink-0">
                        {items.length > 5 && (
                            <div className="mb-2 flex items-center gap-2">
                                <ListFilter className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                                <input
                                    type="text"
                                    value={filter}
                                    onChange={(e) => setFilter(e.target.value)}
                                    placeholder="Filter…"
                                    className="w-full rounded-md border bg-card px-2 py-1 text-sm"
                                    data-testid={`${testIdPrefix}-filter`}
                                />
                            </div>
                        )}
                        <ul
                            className="max-h-[32rem] space-y-0.5 overflow-auto"
                            data-testid={`${testIdPrefix}-list`}
                        >
                            {groups
                                ? groups.map(([group, groupItems]) => (
                                      <li key={group || "(none)"}>
                                          <div className="flex items-center justify-between px-2 pb-0.5 pt-2 first:pt-0">
                                              <span className="truncate text-xs font-medium text-muted-foreground">
                                                  {group || "No server set"}
                                              </span>
                                              {renderGroupAction?.(group)}
                                          </div>
                                          <ul className="space-y-0.5">
                                              {groupItems.map(renderRow)}
                                          </ul>
                                      </li>
                                  ))
                                : filtered.map(renderRow)}
                        </ul>
                        {term && filtered.length === 0 && (
                            <p
                                className="mt-1 text-xs text-muted-foreground"
                                data-testid={`${testIdPrefix}-filter-empty`}
                            >
                                No entries match "{filter}".
                            </p>
                        )}
                    </div>
                    <div
                        className="min-w-0 flex-1"
                        data-testid={`${testIdPrefix}-detail`}
                    >
                        {selectedItem &&
                            renderEditor(
                                selectedItem,
                                items.indexOf(selectedItem),
                            )}
                    </div>
                </div>
            )}
        </div>
    );
}
