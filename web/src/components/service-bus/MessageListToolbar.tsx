import {
    Search,
    Filter,
    Pin,
    Plus,
    X,
    Columns,
    Download,
    Bookmark,
} from "lucide-react";
import { AdvancedFilterPanel } from "./AdvancedFilterPanel";
import type { AdvancedFilterRule } from "./filterTypes";
import {
    PEEK_COUNT_OPTIONS,
    AUTO_REFRESH_OPTIONS,
    ALL_BUILTIN_COLUMNS,
    type SbListPreferences,
    type RowDensity,
} from "@/lib/stores/sb-preferences";
import type { SbSavedFilter } from "@/lib/stores/sb-filters";

/** Everything the toolbar needs from MessageList — grouped so the JSX sections can move
 * without the parent re-deriving each field per section. */
export interface FilterToolbarProps {
    textFilter: string;
    onTextFilterChange: (value: string) => void;
    // Saved filters
    savedFilters: SbSavedFilter[];
    showSavedFilters: boolean;
    canSaveFilter: boolean;
    showSaveFilterInput: boolean;
    saveFilterName: string;
    onToggleSavedFilters: () => void;
    onShowSaveFilterInput: (show: boolean) => void;
    onSaveFilterNameChange: (name: string) => void;
    onApplySavedFilter: (filter: SbSavedFilter) => void;
    onDeleteSavedFilter: (filter: SbSavedFilter) => void;
    onSaveFilter: () => void;
    // Preferences
    prefs: SbListPreferences;
    onPrefsChange: (updater: (p: SbListPreferences) => SbListPreferences) => void;
    nsbMode: boolean;
    // Filter toggles
    filtersEnabled: boolean;
    onToggleFiltersEnabled: () => void;
    advancedEnabled: boolean;
    onToggleAdvanced: () => void;
    activeRuleCount: number;
    anyFiltersActive: boolean;
    onClearAllFilters: () => void;
    onAddRule: () => void;
    // Column panel + ZIP
    showColumnToggle: boolean;
    onToggleColumnPanel: () => void;
    onDownloadZip: () => void;
    downloadDisabled: boolean;
}

export function MessageListToolbar(p: FilterToolbarProps) {
    return (
        <div className="flex items-center gap-1.5 border-b px-2 py-1.5">
            <div className="relative flex-1">
                <Search className="absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
                <input
                    type="text"
                    data-testid="message-text-filter"
                    value={p.textFilter}
                    onChange={(e) => p.onTextFilterChange(e.target.value)}
                    placeholder="Search messages..."
                    className="w-full rounded-md border bg-card py-1.5 pl-8 pr-7 text-xs"
                />
                {p.textFilter && (
                    <button
                        onClick={() => p.onTextFilterChange("")}
                        className="absolute right-1.5 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                        title="Clear search"
                    >
                        <X className="h-3.5 w-3.5" />
                    </button>
                )}
            </div>

            {/* Saved filters */}
            <div className="relative">
                <button
                    data-testid="saved-filters-toggle"
                    onClick={p.onToggleSavedFilters}
                    disabled={p.savedFilters.length === 0 && !p.canSaveFilter}
                    title="Saved filters"
                    className="flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
                >
                    <Bookmark className="h-3.5 w-3.5" />
                    Saved
                </button>
                {p.showSavedFilters && (
                    <div className="absolute right-0 top-full z-20 mt-1 w-64 rounded-md border bg-card p-2 shadow-lg">
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
                                            onClick={() =>
                                                p.onApplySavedFilter(f)
                                            }
                                        >
                                            {f.name}
                                        </button>
                                        <button
                                            onClick={() =>
                                                p.onDeleteSavedFilter(f)
                                            }
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
                                        p.onSaveFilterNameChange(
                                            e.target.value,
                                        )
                                    }
                                    onKeyDown={(e) =>
                                        e.key === "Enter" && p.onSaveFilter()
                                    }
                                    placeholder="Filter name..."
                                    className="flex-1 rounded border bg-card px-2 py-1 text-xs"
                                    autoFocus
                                />
                                <button
                                    onClick={p.onSaveFilter}
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
                                        p.onShowSaveFilterInput(false);
                                        p.onSaveFilterNameChange("");
                                    }}
                                    className="rounded border px-2 py-1 text-xs hover:bg-accent"
                                >
                                    Cancel
                                </button>
                            </div>
                        ) : (
                            p.canSaveFilter && (
                                <button
                                    onClick={() =>
                                        p.onShowSaveFilterInput(true)
                                    }
                                    className="mt-2 w-full rounded border px-2 py-1 text-xs hover:bg-accent"
                                >
                                    Save current filter
                                </button>
                            )
                        )}
                    </div>
                )}
            </div>

            {/* Peek count selector */}
            <select
                data-testid="peek-count-select"
                value={p.prefs.peekCount}
                onChange={(e) =>
                    p.onPrefsChange((prev) => ({
                        ...prev,
                        peekCount: Number(e.target.value),
                    }))
                }
                className="rounded-md border bg-card px-1.5 py-1.5 text-xs"
                title="Peek count"
            >
                {PEEK_COUNT_OPTIONS.map((c) => (
                    <option key={c} value={c}>
                        {c}
                    </option>
                ))}
            </select>

            {/* Auto-refresh selector */}
            <select
                data-testid="auto-refresh-select"
                value={p.prefs.autoRefreshInterval}
                onChange={(e) =>
                    p.onPrefsChange((prev) => ({
                        ...prev,
                        autoRefreshInterval: Number(e.target.value),
                    }))
                }
                className="rounded-md border bg-card px-1.5 py-1.5 text-xs"
                title="Auto-refresh"
            >
                {AUTO_REFRESH_OPTIONS.map((opt) => (
                    <option key={opt.value} value={opt.value}>
                        {opt.label}
                    </option>
                ))}
            </select>

            {/* Row density selector */}
            <select
                data-testid="row-density-select"
                value={p.prefs.rowDensity}
                onChange={(e) =>
                    p.onPrefsChange((prev) => ({
                        ...prev,
                        rowDensity: e.target.value as RowDensity,
                    }))
                }
                className="rounded-md border bg-card px-1.5 py-1.5 text-xs"
                title="Row density"
            >
                <option value="compact">Compact</option>
                <option value="default">Default</option>
                <option value="comfort">Comfort</option>
            </select>

            <button
                data-testid="toggle-filters-enabled"
                onClick={p.onToggleFiltersEnabled}
                title="Toggle all filters"
                className={`flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs ${
                    p.filtersEnabled
                        ? "border-primary bg-primary/10 text-primary"
                        : "text-muted-foreground hover:bg-accent"
                }`}
            >
                {p.filtersEnabled ? "Filters: On" : "Filters: Off"}
            </button>
            <button
                data-testid="toggle-advanced-filter"
                onClick={p.onToggleAdvanced}
                disabled={!p.filtersEnabled}
                title="Advanced filters"
                className={`flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs ${
                    p.advancedEnabled && p.filtersEnabled
                        ? "border-primary bg-primary/10 text-primary"
                        : "text-muted-foreground hover:bg-accent disabled:opacity-50"
                }`}
            >
                <Filter className="h-3.5 w-3.5" />
                {/* Only meaningful once Advanced is actually on — otherwise a rule count from a
            previous session (or one the user just turned Advanced off to ignore) reads as
            "these rules are filtering your messages right now" when they aren't. */}
                {p.advancedEnabled && p.activeRuleCount > 0 && (
                    <span className="rounded-full bg-primary px-1.5 text-[10px] text-primary-foreground">
                        {p.activeRuleCount}
                    </span>
                )}
                <span className="hidden sm:inline">
                    {p.advancedEnabled ? "Advanced: On" : "Advanced: Off"}
                </span>
            </button>
            {p.anyFiltersActive && (
                <button
                    data-testid="clear-all-filters"
                    onClick={p.onClearAllFilters}
                    title="Clear all filters — text search, pinned session, and advanced rules"
                    className="flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs text-muted-foreground hover:bg-accent"
                >
                    <X className="h-3.5 w-3.5" />
                    <span className="hidden sm:inline">
                        Clear all filters
                    </span>
                </button>
            )}
            {p.advancedEnabled && (
                <button
                    data-testid="add-rule"
                    onClick={p.onAddRule}
                    title="Add advanced filter rule"
                    className="flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs text-muted-foreground hover:bg-accent"
                >
                    <Plus className="h-3.5 w-3.5" /> Rule
                </button>
            )}
            <button
                data-testid="toggle-column-visibility"
                onClick={p.onToggleColumnPanel}
                title="Column visibility"
                className={`flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs ${
                    p.showColumnToggle
                        ? "border-primary bg-primary/10 text-primary"
                        : "text-muted-foreground hover:bg-accent"
                }`}
            >
                <Columns className="h-3.5 w-3.5" />
            </button>
            <button
                data-testid="message-download-zip"
                onClick={p.onDownloadZip}
                disabled={p.downloadDisabled}
                title="Download selected or filtered messages as ZIP"
                className="flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs text-muted-foreground hover:bg-accent disabled:opacity-50"
            >
                <Download className="h-3.5 w-3.5" />
                <span className="hidden sm:inline">ZIP</span>
            </button>
            <button
                data-testid="toggle-nsb-mode"
                onClick={() =>
                    p.onPrefsChange((prev) => ({ ...prev, nsbMode: !p.nsbMode }))
                }
                title="Toggle NServiceBus view — shows endpoint, message type, conversation ID"
                className={`flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs ${
                    p.nsbMode
                        ? "border-primary bg-primary/10 text-primary"
                        : "text-muted-foreground hover:bg-accent"
                }`}
            >
                NSB
            </button>
        </div>
    );
}

export interface ColumnTogglePanelProps {
    prefs: SbListPreferences;
    onPrefsChange: (updater: (p: SbListPreferences) => SbListPreferences) => void;
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
                                    onClick={() =>
                                        p.onRemoveCustomColumn(col)
                                    }
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
