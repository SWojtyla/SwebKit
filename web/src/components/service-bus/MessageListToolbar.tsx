import { Filter, Pin, Plus, Search, X } from "lucide-react";
import { Bookmark } from "lucide-react";
import { AdvancedFilterPanel } from "./AdvancedFilterPanel";
import { describeRule } from "./filterTypes";
import type { AdvancedFilterRule } from "./filterTypes";
import { isRuleConfigured } from "./filterTypes";
import {
    ALL_BUILTIN_COLUMNS,
    type SbListPreferences,
} from "@/lib/stores/sb-preferences";
import type { SbSessionSummary } from "@/lib/types";
import { formatLocalDateTime } from "@/lib/datetime";
import type { SbListControls } from "./useSbListControls";

/**
 * Saved-filter dropdown — lives in the ribbon's Messages tab (the whole dropdown content
 * moved verbatim out of the old list toolbar).
 */
export function SavedFiltersMenu({
    controls: p,
    triggerTestId,
}: {
    controls: SbListControls;
    triggerTestId?: string;
}) {
    return (
        <div className="relative">
            <button
                type="button"
                data-testid={triggerTestId ?? "saved-filters-toggle"}
                onClick={() => p.setShowSavedFilters(!p.showSavedFilters)}
                disabled={p.savedFilters.length === 0 && !p.canSaveFilter}
                title="Saved filters"
                className="flex items-center gap-1.5 rounded-md border px-2.5 py-1 text-xs text-muted-foreground hover:bg-accent hover:text-foreground disabled:opacity-50"
            >
                <Bookmark className="h-3.5 w-3.5" />
                Saved
            </button>
            {p.showSavedFilters && (
                <div className="absolute left-0 top-full z-20 mt-1 w-64 rounded-md border bg-popover p-2 shadow-lg">
                    {p.savedFilters.length === 0 ? (
                        <div className="text-xs text-muted-foreground">
                            No saved filters
                        </div>
                    ) : (
                        <div className="space-y-1">
                            {p.savedFilters.map((f) => (
                                <div
                                    key={f.name}
                                    className="flex items-center justify-between gap-1"
                                >
                                    <button
                                        className="flex-1 rounded px-1 py-0.5 text-left text-xs hover:bg-accent"
                                        onClick={() => p.applySavedFilter(f)}
                                    >
                                        {f.name}
                                    </button>
                                    <button
                                        onClick={() => p.removeSavedFilter(f)}
                                        className="text-muted-foreground hover:text-foreground"
                                        title="Delete saved filter"
                                    >
                                        <X className="h-3 w-3" />
                                    </button>
                                </div>
                            ))}
                        </div>
                    )}
                    {p.showSaveFilterInput ? (
                        <div className="mt-2 flex items-center gap-1">
                            <input
                                type="text"
                                value={p.saveFilterName}
                                onChange={(e) =>
                                    p.setSaveFilterName(e.target.value)
                                }
                                onKeyDown={(e) =>
                                    e.key === "Enter" && p.saveFilter()
                                }
                                placeholder="Filter name..."
                                className="flex-1 rounded border bg-card px-2 py-1 text-xs"
                                autoFocus
                            />
                            <button
                                onClick={p.saveFilter}
                                disabled={!p.saveFilterName.trim()}
                                title={
                                    !p.saveFilterName.trim()
                                        ? "Name the filter first"
                                        : undefined
                                }
                                className="rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                            >
                                Save
                            </button>
                            <button
                                onClick={() => {
                                    p.setShowSaveFilterInput(false);
                                    p.setSaveFilterName("");
                                }}
                                className="rounded border px-2 py-1 text-xs hover:bg-accent"
                            >
                                Cancel
                            </button>
                        </div>
                    ) : (
                        p.canSaveFilter && (
                            <button
                                onClick={() => p.setShowSaveFilterInput(true)}
                                className="mt-2 w-full rounded border px-2 py-1 text-xs hover:bg-accent"
                            >
                                Save current filter
                            </button>
                        )
                    )}
                </div>
            )}
        </div>
    );
}

/**
 * The always-visible filter strip: the text search plus one chip per active condition, so a
 * narrowed list can never look unfiltered. Rules are removed individually here; editing
 * happens in the advanced panel (opened via "+ rule").
 */
export function FilterChipBar({
    controls: c,
    filteredCount,
    loadedCount,
}: {
    controls: SbListControls;
    filteredCount: number;
    loadedCount: number;
}) {
    const activeRules = c.advancedRules.filter(
        (r) => r.enabled && isRuleConfigured(r),
    );
    return (
        <div
            className="flex flex-wrap items-center gap-1.5 border-b px-2 py-1"
            data-testid="filter-chip-bar"
        >
            <div className="relative w-52">
                <Search className="absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
                <input
                    type="text"
                    data-testid="message-text-filter"
                    value={c.textFilter}
                    onChange={(e) => c.setTextFilter(e.target.value)}
                    placeholder="Search messages..."
                    className="w-full rounded-md border bg-card py-1 pl-7 pr-6 text-xs"
                />
                {c.textFilter && (
                    <button
                        onClick={() => c.setTextFilter("")}
                        className="absolute right-1.5 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                        title="Clear search"
                    >
                        <X className="h-3.5 w-3.5" />
                    </button>
                )}
            </div>

            {c.textFilter.trim() && (
                <Chip
                    label={`search "${c.textFilter.trim()}"`}
                    testId="filter-chip-text"
                    onRemove={() => c.setTextFilter("")}
                />
            )}
            {c.pinnedSessionId && (
                <Chip
                    label={`session:${c.pinnedSessionId}`}
                    testId="filter-chip-session"
                    onRemove={() => c.setPinnedSessionId(null)}
                />
            )}
            {activeRules.map((rule) => (
                <Chip
                    key={rule.id}
                    label={describeRule(rule)}
                    testId={`filter-chip-rule-${rule.id}`}
                    onRemove={() =>
                        c.setAdvancedRules(
                            c.advancedRules.filter((r) => r.id !== rule.id),
                        )
                    }
                />
            ))}

            <button
                type="button"
                data-testid="filter-chip-add-rule"
                onClick={c.addRule}
                title="Add a typed filter rule (text, numeric or time)"
                className="flex items-center gap-1 rounded-full border border-dashed px-2 py-0.5 text-xs text-muted-foreground hover:bg-accent hover:text-foreground"
            >
                <Plus className="h-3 w-3" /> rule
            </button>

            <div className="ml-auto flex items-center gap-2">
                {/* The filter master-switch is now in the ribbon — surface it here too, or a
                    switched-off filter would silently leave the list unfiltered. */}
                {!c.filtersEnabled && c.anyFiltersActive && (
                    <button
                        type="button"
                        data-testid="filters-off-chip"
                        onClick={() => c.setFiltersEnabled(true)}
                        title="Filters are configured but disabled — click to re-enable"
                        className="flex items-center gap-1 rounded-full border border-amber-500/50 bg-amber-500/10 px-2 py-0.5 text-xs text-amber-600 dark:text-amber-400"
                    >
                        <Filter className="h-3 w-3" /> filters off — showing all
                    </button>
                )}
                {c.anyFiltersActive && (
                    <button
                        type="button"
                        data-testid="clear-all-filters"
                        onClick={c.clearAllFilters}
                        title="Clear all filters — text search, pinned session, and rules"
                        className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground"
                    >
                        <X className="h-3 w-3" /> Clear all
                    </button>
                )}
                <span
                    className="text-xs text-muted-foreground"
                    data-testid="filter-match-count"
                >
                    {filteredCount === loadedCount
                        ? `${filteredCount} shown`
                        : `${filteredCount} of ${loadedCount}`}
                </span>
            </div>
        </div>
    );
}

function Chip({
    label,
    onRemove,
    testId,
}: {
    label: string;
    onRemove: () => void;
    testId: string;
}) {
    return (
        <span
            data-testid={testId}
            className="flex items-center gap-1 rounded-full border bg-accent/40 px-2 py-0.5 text-xs"
        >
            {label}
            <button
                type="button"
                onClick={onRemove}
                title="Remove this filter"
                className="text-muted-foreground hover:text-foreground"
            >
                <X className="h-3 w-3" />
            </button>
        </span>
    );
}

export interface ColumnTogglePanelProps {
    prefs: SbListPreferences;
    onPrefsChange: (
        updater: (p: SbListPreferences) => SbListPreferences,
    ) => void;
    visibleColumns: Set<string>;
    suggestedColumns: string[];
    customColumnInput: string;
    onCustomColumnInputChange: (value: string) => void;
    onAddCustomColumn: () => void;
    onRemoveCustomColumn: (col: string) => void;
    onToggleBuiltInColumn: (col: string) => void;
}

export function ColumnTogglePanel(p: ColumnTogglePanelProps) {
    return (
        <div
            className="border-b bg-muted/20 px-3 py-2"
            data-testid="column-toggle-dropdown"
        >
            <div className="mb-2 text-xs font-medium text-muted-foreground">
                Built-in columns
            </div>
            <div className="flex flex-wrap gap-2">
                {ALL_BUILTIN_COLUMNS.map((col) => (
                    <label
                        key={col}
                        className="flex items-center gap-1 text-xs"
                    >
                        <input
                            type="checkbox"
                            checked={p.visibleColumns.has(col)}
                            onChange={() => p.onToggleBuiltInColumn(col)}
                            data-testid={`column-toggle-${col}`}
                        />
                        {col}
                    </label>
                ))}
            </div>
            {p.prefs.customColumns.length > 0 && (
                <>
                    <div className="mb-1 mt-2 text-xs font-medium text-muted-foreground">
                        Custom property columns
                    </div>
                    <div className="flex flex-wrap gap-2">
                        {p.prefs.customColumns.map((col) => (
                            <span
                                key={col}
                                className="flex items-center gap-1 rounded bg-accent px-1.5 py-0.5 text-xs"
                            >
                                {col}
                                <button
                                    onClick={() => p.onRemoveCustomColumn(col)}
                                    className="text-muted-foreground hover:text-foreground"
                                >
                                    <X className="h-3 w-3" />
                                </button>
                            </span>
                        ))}
                    </div>
                </>
            )}
            {p.suggestedColumns.length > 0 && (
                <>
                    <div className="mb-1 mt-2 text-xs font-medium text-muted-foreground">
                        Suggested from data
                    </div>
                    <div className="flex flex-wrap gap-1">
                        {p.suggestedColumns.map((col) => (
                            <button
                                key={col}
                                onClick={() =>
                                    p.onPrefsChange((prev) => ({
                                        ...prev,
                                        customColumns: [
                                            ...prev.customColumns,
                                            col,
                                        ],
                                    }))
                                }
                                className="flex items-center gap-0.5 rounded border px-1.5 py-0.5 text-xs hover:bg-accent"
                                data-testid={`suggest-column-${col}`}
                            >
                                <Plus className="h-2.5 w-2.5" /> {col}
                            </button>
                        ))}
                    </div>
                </>
            )}
            <div className="mt-2 flex items-center gap-1">
                <input
                    type="text"
                    value={p.customColumnInput}
                    onChange={(e) =>
                        p.onCustomColumnInputChange(e.target.value)
                    }
                    onKeyDown={(e) =>
                        e.key === "Enter" && p.onAddCustomColumn()
                    }
                    placeholder="Add custom property column..."
                    className="flex-1 rounded border bg-card px-2 py-1 text-xs"
                    data-testid="custom-column-input"
                />
                <button
                    onClick={p.onAddCustomColumn}
                    className="rounded border px-2 py-1 text-xs hover:bg-accent"
                    data-testid="add-custom-column"
                >
                    Add
                </button>
            </div>
        </div>
    );
}

export function SessionPinFilter({
    pinnedSessionId,
    onChange,
}: {
    pinnedSessionId: string | null;
    onChange: (value: string | null) => void;
}) {
    return (
        <div className="flex items-center gap-1.5 border-b px-2 py-1">
            <Pin className="h-3.5 w-3.5 text-muted-foreground" />
            <input
                type="text"
                data-testid="session-pin-filter"
                value={pinnedSessionId ?? ""}
                onChange={(e) => onChange(e.target.value || null)}
                placeholder="Filter by Session ID..."
                className="flex-1 rounded-md border bg-card px-2 py-1 text-xs"
            />
            {pinnedSessionId && (
                <button
                    onClick={() => onChange(null)}
                    className="text-muted-foreground hover:text-foreground"
                    data-testid="session-pin-clear"
                >
                    <X className="h-3.5 w-3.5" />
                </button>
            )}
        </div>
    );
}

/**
 * One chip per session visible in the peek window — clicking a chip drives the existing
 * SessionPinFilter rather than filtering on its own, so the chips and the pin input can never
 * disagree about which session is pinned. Only rendered for `requiresSession` entities.
 */
export function SessionChipBar({
    sessions,
    pinnedSessionId,
    onPin,
}: {
    sessions: SbSessionSummary[];
    pinnedSessionId: string | null;
    onPin: (sessionId: string | null) => void;
}) {
    if (sessions.length === 0) return null;
    return (
        <div
            className="flex flex-wrap items-center gap-1 border-b px-2 py-1"
            data-testid="session-chip-bar"
        >
            <Pin className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
            <span className="mr-1 text-xs text-muted-foreground">
                Sessions:
            </span>
            {sessions.map((session) => {
                const active = pinnedSessionId === session.sessionId;
                return (
                    <button
                        key={session.sessionId}
                        type="button"
                        data-testid={`session-chip-${session.sessionId}`}
                        aria-pressed={active}
                        onClick={() => onPin(active ? null : session.sessionId)}
                        title={`${session.messageCount} message(s) · ${formatLocalDateTime(session.firstEnqueuedAt)} – ${formatLocalDateTime(session.lastEnqueuedAt)}${active ? " · click to unpin" : " · click to pin this session"}`}
                        className={`rounded-full border px-2 py-0.5 text-xs ${
                            active
                                ? "border-primary bg-primary/15 text-primary"
                                : "text-muted-foreground hover:bg-accent hover:text-foreground"
                        }`}
                    >
                        {session.sessionId} ({session.messageCount})
                    </button>
                );
            })}
        </div>
    );
}

export function AdvancedFilterSection({
    rules,
    onChange,
}: {
    rules: AdvancedFilterRule[];
    onChange: (rules: AdvancedFilterRule[]) => void;
}) {
    return (
        <>
            <div className="flex items-center justify-between border-b bg-muted/20 px-2 py-1">
                <span className="text-xs font-medium">Advanced filters</span>
                {rules.length > 0 && (
                    <button
                        onClick={() => onChange([])}
                        className="text-xs text-muted-foreground hover:text-foreground"
                    >
                        Clear all
                    </button>
                )}
            </div>
            <AdvancedFilterPanel rules={rules} onChange={onChange} />
        </>
    );
}
