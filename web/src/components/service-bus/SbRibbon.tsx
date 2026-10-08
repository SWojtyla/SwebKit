import { useEffect, useState, type ReactNode } from "react";
import {
    ChevronDown,
    ChevronUp,
    Clock,
    Columns,
    Crosshair,
    Download,
    FileText,
    Filter,
    Layers,
    ListOrdered,
    Plus,
    RefreshCw,
    RotateCcw,
    Search,
    Sparkles,
    Timer,
    Trash2,
    Upload,
    ArrowRightToLine,
    type LucideIcon,
} from "lucide-react";
import { SearchableSelect } from "@/components/shared/SearchableSelect";
import type {
    SbEntityInfo,
    SbViewMode,
    ServiceBusNamespace,
} from "@/lib/types";
import {
    AUTO_REFRESH_OPTIONS,
    PEEK_COUNT_OPTIONS,
    type RowDensity,
} from "@/lib/stores/sb-preferences";
import { SESSIONS_NOT_SUPPORTED_TOOLTIP } from "./sessionHelpers";
import { SavedFiltersMenu } from "./MessageListToolbar";
import type { SbListControls } from "./useSbListControls";

type RibbonTab = "home" | "messages" | "recovery" | "view";

const RIBBON_STATE_KEY = "sb-ribbon-state";

function loadRibbonState(): { tab: RibbonTab; collapsed: boolean } {
    try {
        const raw = localStorage.getItem(RIBBON_STATE_KEY);
        if (raw) {
            const parsed = JSON.parse(raw) as {
                tab?: RibbonTab;
                collapsed?: boolean;
            };
            return {
                tab: parsed.tab ?? "home",
                collapsed: parsed.collapsed ?? false,
            };
        }
    } catch {
        // ignore storage errors
    }
    return { tab: "home", collapsed: false };
}

function saveRibbonState(tab: RibbonTab, collapsed: boolean) {
    try {
        localStorage.setItem(
            RIBBON_STATE_KEY,
            JSON.stringify({ tab, collapsed }),
        );
    } catch {
        // ignore storage errors
    }
}

/** Compact ribbon control — icon + label, never the chunky Office-style block. */
function RibbonButton({
    icon: Icon,
    label,
    title,
    onClick,
    disabled,
    testId,
    primary,
    danger,
    active,
}: {
    icon?: LucideIcon;
    label: string;
    title?: string;
    onClick: () => void;
    disabled?: boolean;
    testId?: string;
    primary?: boolean;
    danger?: boolean;
    active?: boolean;
}) {
    return (
        <button
            type="button"
            data-testid={testId}
            onClick={onClick}
            disabled={disabled}
            title={title}
            className={`flex items-center gap-1.5 rounded-md border px-2.5 py-1 text-xs whitespace-nowrap ${
                primary
                    ? "border-primary bg-primary text-primary-foreground hover:opacity-90"
                    : danger
                      ? "border-destructive/40 text-destructive hover:bg-destructive/10"
                      : active
                        ? "border-primary bg-primary/10 text-primary"
                        : "text-muted-foreground hover:bg-accent hover:text-foreground"
            } disabled:opacity-50`}
        >
            {Icon && <Icon className="h-3.5 w-3.5" />}
            {label}
        </button>
    );
}

/** A labelled group of controls separated from its neighbours by a hairline divider. */
function RibbonGroup({
    label,
    children,
}: {
    label: string;
    children: ReactNode;
}) {
    return (
        <div className="flex flex-col justify-between border-r border-border/60 px-3 first:pl-1 last:border-r-0">
            <div className="flex items-center gap-1">{children}</div>
            <div className="pt-0.5 text-center text-[9px] font-medium uppercase tracking-wider text-muted-foreground/70">
                {label}
            </div>
        </div>
    );
}

function RibbonSelect({
    icon: Icon,
    title,
    value,
    onChange,
    options,
    testId,
    renderOption,
}: {
    icon: LucideIcon;
    title: string;
    value: number | string;
    onChange: (value: string) => void;
    options: readonly { value: number | string; label: string }[];
    testId?: string;
    renderOption?: (opt: { value: number | string; label: string }) => string;
}) {
    return (
        <label
            className="flex items-center gap-1 rounded-md border bg-card px-1.5 py-1 text-xs"
            title={title}
        >
            <Icon className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
            <select
                data-testid={testId}
                value={value}
                onChange={(e) => onChange(e.target.value)}
                className="bg-transparent py-0.5 text-xs outline-none"
            >
                {options.map((o) => (
                    <option key={o.value} value={o.value}>
                        {renderOption ? renderOption(o) : o.label}
                    </option>
                ))}
            </select>
        </label>
    );
}

export interface SbRibbonProps {
    namespaces: ServiceBusNamespace[];
    selectedNsId: string | null;
    onSelectNamespace: (id: string | null) => void;
    entity: SbEntityInfo | null;
    viewMode: SbViewMode;
    controls: SbListControls;
    // List-side commands surfaced via MessageList's action registration.
    onRefresh: () => void;
    isFetching: boolean;
    onDownloadZip: () => void;
    zipDisabled: boolean;
    // Page-level panels.
    onCompose: () => void;
    onBatchSend: () => void;
    onScheduled: () => void;
    onTemplates: () => void;
    onEntitySearch: () => void;
    onAskAi: () => void;
    onBatchReplay: () => void;
    onReachMessage: () => void;
    onDlqTriage: () => void;
    onReplayTo: () => void;
    onPurge: () => void;
    purgePending: boolean;
    sessionBlocked: boolean;
    showEntityTree: boolean;
    onToggleEntityTree: () => void;
}

export function SbRibbon(p: SbRibbonProps) {
    const [state, setState] = useState(loadRibbonState);
    const { tab, collapsed } = state;
    useEffect(() => saveRibbonState(tab, collapsed), [tab, collapsed]);

    const c = p.controls;
    const noNs = !p.selectedNsId;
    const noEntity = !p.entity;
    const entityTitle = noEntity ? "Select a queue or topic first" : undefined;
    const sessionTitle = p.sessionBlocked
        ? SESSIONS_NOT_SUPPORTED_TOOLTIP
        : undefined;
    const entityOrSessionTitle = entityTitle ?? sessionTitle;

    const selectTab = (t: RibbonTab) =>
        setState((s) => ({
            tab: t,
            collapsed: s.tab === t ? !s.collapsed : false,
        }));

    const TABS: { id: RibbonTab; label: string; testId: string }[] = [
        { id: "home", label: "Home", testId: "sb-tab-home" },
        { id: "messages", label: "Messages", testId: "sb-tab-messages" },
        { id: "recovery", label: "DLQ & Recovery", testId: "sb-tab-dlq" },
        { id: "view", label: "View", testId: "sb-tab-view" },
    ];

    return (
        <div className="border-b" data-testid="sb-ribbon">
            {/* Tab strip — namespace picker lives here because it's global context, not a per-tab command. */}
            <div className="flex items-center gap-1 px-2 pt-1">
                {TABS.map((t) => (
                    <button
                        key={t.id}
                        type="button"
                        data-testid={t.testId}
                        onClick={() => selectTab(t.id)}
                        className={`rounded-t-md border-b-2 px-3 py-1 text-xs font-medium ${
                            tab === t.id && !collapsed
                                ? "border-primary text-foreground"
                                : "border-transparent text-muted-foreground hover:text-foreground"
                        }`}
                    >
                        {t.label}
                    </button>
                ))}
                <div className="flex-1" />
                <div className="flex items-center gap-2 pb-1">
                    <span className="text-[11px] text-muted-foreground">
                        Namespace
                    </span>
                    <SearchableSelect
                        items={p.namespaces.map((ns) => ({
                            value: ns.id,
                            label: ns.alias || ns.fullyQualifiedNamespace,
                            subtitle: ns.alias
                                ? ns.fullyQualifiedNamespace
                                : undefined,
                        }))}
                        value={p.selectedNsId}
                        onChange={(item) =>
                            p.onSelectNamespace(item.value || null)
                        }
                        placeholder="Select namespace..."
                        filterPlaceholder="Filter namespaces..."
                        testId="sb-namespace"
                        nativeSelectTestId="sb-namespace-select"
                        nativeExtraOptions={[
                            { value: "", label: "Select namespace..." },
                        ]}
                        listAriaLabel="Service Bus namespaces"
                        buttonClassName="min-w-[14rem]"
                    />
                    <button
                        type="button"
                        data-testid="sb-ribbon-collapse"
                        onClick={() =>
                            setState((s) => ({ ...s, collapsed: !s.collapsed }))
                        }
                        title={collapsed ? "Expand ribbon" : "Collapse ribbon"}
                        className="rounded-md px-1.5 py-1 text-muted-foreground hover:bg-accent hover:text-foreground"
                    >
                        {collapsed ? (
                            <ChevronDown className="h-3.5 w-3.5" />
                        ) : (
                            <ChevronUp className="h-3.5 w-3.5" />
                        )}
                    </button>
                </div>
            </div>

            {!collapsed && (
                <div
                    className="flex items-stretch border-t px-1 py-1"
                    data-testid="sb-ribbon-body"
                >
                    {tab === "home" && (
                        <>
                            <RibbonGroup label="Send">
                                <RibbonButton
                                    icon={Plus}
                                    label="Compose"
                                    testId="sb-compose-button"
                                    primary
                                    disabled={noNs}
                                    title={
                                        noNs
                                            ? "Select a namespace first"
                                            : "Compose a new message"
                                    }
                                    onClick={p.onCompose}
                                />
                                <RibbonButton
                                    icon={Upload}
                                    label="Batch Send"
                                    testId="sb-batch-send-button"
                                    disabled={noNs}
                                    title={
                                        noNs
                                            ? "Select a namespace first"
                                            : "Send a batch — paste CSV/JSON"
                                    }
                                    onClick={p.onBatchSend}
                                />
                                <RibbonButton
                                    icon={Clock}
                                    label="Scheduled"
                                    testId="sb-scheduled-button"
                                    disabled={noEntity}
                                    title={
                                        entityTitle ??
                                        "View and cancel scheduled messages on this entity"
                                    }
                                    onClick={p.onScheduled}
                                />
                                <RibbonButton
                                    icon={FileText}
                                    label="Templates"
                                    testId="sb-templates-button"
                                    title="Manage message templates"
                                    onClick={p.onTemplates}
                                />
                            </RibbonGroup>
                            <RibbonGroup label="Entity">
                                <RibbonButton
                                    icon={Search}
                                    label="Search Entities"
                                    testId="sb-entity-search"
                                    disabled={noNs}
                                    title={
                                        noNs
                                            ? "Select a namespace first"
                                            : "Find a queue, topic or subscription (Ctrl+Shift+E)"
                                    }
                                    onClick={p.onEntitySearch}
                                />
                                <RibbonButton
                                    icon={Sparkles}
                                    label="Ask AI"
                                    testId="sb-ask-ai-ribbon"
                                    disabled={noEntity}
                                    title={
                                        entityTitle ??
                                        "Ask AI about this entity"
                                    }
                                    onClick={p.onAskAi}
                                />
                            </RibbonGroup>
                        </>
                    )}

                    {tab === "messages" && (
                        <>
                            <RibbonGroup label="Fetch">
                                <RibbonButton
                                    icon={RefreshCw}
                                    label="Refresh"
                                    testId="ribbon-refresh"
                                    disabled={noEntity || p.isFetching}
                                    title="Refresh messages"
                                    onClick={p.onRefresh}
                                />
                                <RibbonSelect
                                    icon={ListOrdered}
                                    title="Messages per peek — how many are fetched per load"
                                    testId="peek-count-select"
                                    value={c.prefs.peekCount}
                                    onChange={(v) =>
                                        c.setPrefs((prev) => ({
                                            ...prev,
                                            peekCount: Number(v),
                                        }))
                                    }
                                    options={PEEK_COUNT_OPTIONS.map((n) => ({
                                        value: n,
                                        label: String(n),
                                    }))}
                                />
                                <RibbonSelect
                                    icon={Timer}
                                    title="Auto-refresh interval — how often the list re-peeks"
                                    testId="auto-refresh-select"
                                    value={c.prefs.autoRefreshInterval}
                                    onChange={(v) =>
                                        c.setPrefs((prev) => ({
                                            ...prev,
                                            autoRefreshInterval: Number(v),
                                        }))
                                    }
                                    options={AUTO_REFRESH_OPTIONS.map((o) => ({
                                        value: o.value,
                                        label: o.label,
                                    }))}
                                    renderOption={(o) =>
                                        o.value === 0
                                            ? "Off"
                                            : `every ${o.label}`
                                    }
                                />
                            </RibbonGroup>
                            <RibbonGroup label="Filters">
                                <RibbonButton
                                    label={
                                        c.filtersEnabled
                                            ? "Filters: On"
                                            : "Filters: Off"
                                    }
                                    testId="toggle-filters-enabled"
                                    active={c.filtersEnabled}
                                    title="Toggle all filters"
                                    onClick={() =>
                                        c.setFiltersEnabled(!c.filtersEnabled)
                                    }
                                />
                                <RibbonButton
                                    icon={Filter}
                                    label={
                                        c.advancedEnabled &&
                                        c.activeRuleCount > 0
                                            ? `Rules (${c.activeRuleCount})`
                                            : "Rules"
                                    }
                                    testId="toggle-advanced-filter"
                                    active={
                                        c.advancedEnabled && c.filtersEnabled
                                    }
                                    disabled={!c.filtersEnabled}
                                    title="Typed rule builder — text, numeric and time comparisons"
                                    onClick={() =>
                                        c.setAdvancedEnabled(!c.advancedEnabled)
                                    }
                                />
                                <RibbonButton
                                    icon={Plus}
                                    label="Add rule"
                                    testId="add-rule"
                                    disabled={!c.filtersEnabled}
                                    title="Add a typed filter rule"
                                    onClick={c.addRule}
                                />
                                <SavedFiltersMenu
                                    controls={c}
                                    triggerTestId="saved-filters-toggle"
                                />
                            </RibbonGroup>
                            <RibbonGroup label="Export">
                                <RibbonButton
                                    icon={Download}
                                    label="ZIP"
                                    testId="message-download-zip"
                                    disabled={p.zipDisabled}
                                    title="Download selected or filtered messages as ZIP"
                                    onClick={p.onDownloadZip}
                                />
                            </RibbonGroup>
                        </>
                    )}

                    {tab === "recovery" && (
                        <>
                            <RibbonGroup label="Recovery">
                                <RibbonButton
                                    icon={RotateCcw}
                                    label="Batch Replay"
                                    testId="sb-batch-replay-button"
                                    disabled={noEntity || p.sessionBlocked}
                                    title={
                                        entityOrSessionTitle ??
                                        "Resubmit dead-lettered messages on this entity"
                                    }
                                    onClick={p.onBatchReplay}
                                />
                                <RibbonButton
                                    icon={Layers}
                                    label="DLQ Triage"
                                    testId="sb-dlq-triage-button"
                                    disabled={noEntity || p.sessionBlocked}
                                    title={
                                        entityOrSessionTitle ??
                                        "Group the DLQ by reason/description and act per group"
                                    }
                                    onClick={p.onDlqTriage}
                                />
                                <RibbonButton
                                    icon={Crosshair}
                                    label="Reach Message"
                                    testId="sb-reach-message-button"
                                    disabled={noEntity || p.sessionBlocked}
                                    title={
                                        entityOrSessionTitle ??
                                        "Park, act on a target message, and restore copies at the tail"
                                    }
                                    onClick={p.onReachMessage}
                                />
                                <RibbonButton
                                    icon={ArrowRightToLine}
                                    label="Replay To…"
                                    testId="sb-replay-to-button"
                                    disabled={noEntity || p.sessionBlocked}
                                    title={
                                        entityOrSessionTitle ??
                                        "Send selected messages to another namespace or entity as new copies"
                                    }
                                    onClick={p.onReplayTo}
                                />
                            </RibbonGroup>
                            <RibbonGroup label="Danger">
                                <RibbonButton
                                    icon={Trash2}
                                    label="Purge All"
                                    testId="sb-purge-all-button"
                                    danger
                                    disabled={
                                        noEntity ||
                                        p.sessionBlocked ||
                                        p.purgePending ||
                                        p.viewMode === "scheduled"
                                    }
                                    title={
                                        entityOrSessionTitle ??
                                        (p.viewMode === "scheduled"
                                            ? "Scheduled messages aren't purgeable — cancel them individually or purge the active queue"
                                            : `Purge all ${p.viewMode === "dlq" ? "dead-lettered" : "active"} messages in this entity — cannot be undone`)
                                    }
                                    onClick={p.onPurge}
                                />
                            </RibbonGroup>
                        </>
                    )}

                    {tab === "view" && (
                        <>
                            <RibbonGroup label="Panes">
                                <RibbonButton
                                    icon={ListOrdered}
                                    label={
                                        p.showEntityTree
                                            ? "Hide Entities"
                                            : "Show Entities"
                                    }
                                    testId="toggle-entity-tree"
                                    title="Show or hide the entity tree"
                                    onClick={p.onToggleEntityTree}
                                />
                            </RibbonGroup>
                            <RibbonGroup label="Columns">
                                <RibbonButton
                                    icon={Columns}
                                    label="Columns"
                                    testId="toggle-column-visibility"
                                    active={c.showColumnToggle}
                                    title="Column visibility"
                                    onClick={() =>
                                        c.setShowColumnToggle(
                                            !c.showColumnToggle,
                                        )
                                    }
                                />
                                <RibbonSelect
                                    icon={Layers}
                                    title="Row density"
                                    testId="row-density-select"
                                    value={c.prefs.rowDensity}
                                    onChange={(v) =>
                                        c.setPrefs((prev) => ({
                                            ...prev,
                                            rowDensity: v as RowDensity,
                                        }))
                                    }
                                    options={[
                                        { value: "compact", label: "Compact" },
                                        { value: "default", label: "Default" },
                                        { value: "comfort", label: "Comfort" },
                                    ]}
                                />
                            </RibbonGroup>
                            <RibbonGroup label="Mode">
                                <RibbonButton
                                    label="NSB"
                                    testId="toggle-nsb-mode"
                                    active={c.prefs.nsbMode ?? false}
                                    title="NServiceBus view — shows endpoint, message type, conversation ID"
                                    onClick={() =>
                                        c.setPrefs((prev) => ({
                                            ...prev,
                                            nsbMode: !prev.nsbMode,
                                        }))
                                    }
                                />
                            </RibbonGroup>
                        </>
                    )}
                </div>
            )}
        </div>
    );
}
