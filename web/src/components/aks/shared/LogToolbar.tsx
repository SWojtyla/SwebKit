import type { ReactNode } from "react";
import {
  Play,
  Pause,
  ChevronUp,
  ChevronDown,
  ArrowDown,
  Download,
  ClipboardCopy,
  Trash2,
  Search,
  Clock,
} from "lucide-react";
import type { TimestampMode } from "@/lib/log-window";

/// The log toolbar, shared by the single-pod and multi-pod views.
///
/// It exists as one component because it previously existed as one *implementation* —
/// in `PodLogView` only. The React port of the multi-pod view dropped it, leaving that
/// view with no filter, export, copy, clear or pause at all. Rebuilding it separately
/// would set up the same drift again.
///
/// Purely presentational: every piece of state is owned by the caller, so the two views
/// can differ in what they offer (multi-pod has no container dropdown of its own) without
/// this component knowing which one it is rendering.

export interface LogToolbarProps {
  /** Rendered before the filter — the caller's own controls (live toggle, pod chips). */
  leading?: ReactNode;
  textFilter: string;
  onTextFilterChange: (value: string) => void;
  summary: string;
  timestampMode: TimestampMode;
  onTimestampModeChange: (mode: TimestampMode) => void;
  paused: boolean;
  onTogglePause: () => void;
  canShowOlder: boolean;
  canShowNewer: boolean;
  canJumpToLatest: boolean;
  onShowOlder: () => void;
  onShowNewer: () => void;
  onJumpToLatest: () => void;
  onCopyVisible: () => void;
  onExport: () => void;
  onClear: () => void;
  isExporting?: boolean;
  /** Disables pause and paging, for a view showing a terminated container. */
  disableFollowControls?: boolean;
  /** Lines held back while the window is frozen, shown on the Latest button. */
  pendingCount?: number;
  /** Omit to hide the Go-to-live control entirely — the multi-pod view always follows. */
  goLive?: {
    onGoLive: () => void;
    /** True when already tailing the newest lines, which disables the button. */
    isFollowing: boolean;
    disabled?: boolean;
  };
  /** Rendered at the very end of the toolbar. */
  trailing?: ReactNode;
}

const TIMESTAMP_OPTIONS: { value: TimestampMode; label: string }[] = [
  { value: "off", label: "No timestamps" },
  { value: "time", label: "Time" },
  { value: "full", label: "Full timestamp" },
];

export function LogToolbar({
  leading,
  textFilter,
  onTextFilterChange,
  summary,
  timestampMode,
  onTimestampModeChange,
  paused,
  onTogglePause,
  canShowOlder,
  canShowNewer,
  canJumpToLatest,
  onShowOlder,
  onShowNewer,
  onJumpToLatest,
  onCopyVisible,
  onExport,
  onClear,
  isExporting = false,
  disableFollowControls = false,
  pendingCount = 0,
  goLive,
  trailing,
}: LogToolbarProps) {
  return (
    <div className="flex flex-wrap items-center gap-2 border-b bg-card px-3 py-2">
      {leading}

      <div className="relative min-w-[120px] flex-1">
        <Search className="absolute left-2 top-1/2 h-3 w-3 -translate-y-1/2 text-muted-foreground" />
        <input
          type="text"
          value={textFilter}
          onChange={(e) => onTextFilterChange(e.target.value)}
          placeholder="Filter..."
          className="w-full rounded border bg-background py-1 pl-7 pr-2 text-xs"
          data-testid="log-filter-input"
        />
      </div>

      <label className="flex items-center gap-1 text-xs" title="Timestamp display">
        <Clock className="h-3 w-3 text-muted-foreground" />
        <select
          value={timestampMode}
          onChange={(e) => onTimestampModeChange(e.target.value as TimestampMode)}
          className="rounded border bg-background px-2 py-1 text-xs"
          data-testid="log-timestamp-select"
        >
          {TIMESTAMP_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>{o.label}</option>
          ))}
        </select>
      </label>

      <span className="whitespace-nowrap text-xs text-muted-foreground" data-testid="log-line-count">
        {summary}
      </span>

      <div className="ml-auto flex flex-wrap items-center gap-1">
        {/* Navigate */}
        <div className="flex items-center gap-0.5">
          <button
            onClick={onShowOlder}
            disabled={!canShowOlder || disableFollowControls}
            className={BUTTON_CLASS}
            title="Show older buffered lines"
            data-testid="log-older-btn"
          >
            <ChevronUp className="h-3 w-3" /> Older
          </button>
          <button
            onClick={onShowNewer}
            disabled={!canShowNewer || disableFollowControls}
            className={BUTTON_CLASS}
            title="Show newer buffered lines"
            data-testid="log-newer-btn"
          >
            <ChevronDown className="h-3 w-3" /> Newer
          </button>
          <button
            onClick={onJumpToLatest}
            disabled={!canJumpToLatest || disableFollowControls}
            className={BUTTON_CLASS}
            title="Jump back to the newest buffered lines"
            data-testid="log-latest-btn"
          >
            <ArrowDown className="h-3 w-3" />
            {pendingCount > 0 ? `Latest (${pendingCount})` : "Latest"}
          </button>
        </div>

        <span className="mx-1 h-4 w-px bg-border" />

        {goLive && (
          <button
            onClick={goLive.onGoLive}
            disabled={goLive.disabled || goLive.isFollowing}
            className={`flex items-center gap-1 rounded px-2 py-1 text-xs ${
              goLive.isFollowing
                ? "border border-success/50 text-success"
                : "border bg-primary text-primary-foreground hover:bg-primary/90"
            }`}
            title="Resume live tailing and jump to the newest lines"
            data-testid="log-go-live-btn"
          >
            <Play className="h-3 w-3" />
            {goLive.isFollowing ? "Live" : "Go to live"}
          </button>
        )}

        <button
          onClick={onTogglePause}
          disabled={disableFollowControls}
          className={BUTTON_CLASS}
          title={paused ? "Resume tailing" : "Pause tailing"}
          data-testid="log-pause-btn"
        >
          {paused ? <Play className="h-3 w-3" /> : <Pause className="h-3 w-3" />}
          {paused ? "Resume" : "Pause"}
        </button>

        <span className="mx-1 h-4 w-px bg-border" />

        {/* Data */}
        <button
          onClick={onCopyVisible}
          className={BUTTON_CLASS}
          title="Copy the currently visible log window"
          data-testid="log-copy-visible-btn"
        >
          <ClipboardCopy className="h-3 w-3" /> Copy visible
        </button>
        <button
          onClick={onExport}
          disabled={isExporting}
          className={BUTTON_CLASS}
          title="Export the full log stream"
          data-testid="log-export-btn"
        >
          <Download className="h-3 w-3" />
          {isExporting ? "Exporting…" : "Export all"}
        </button>
        <button
          onClick={onClear}
          className={BUTTON_CLASS}
          title="Clear log buffer"
          data-testid="log-clear-btn"
        >
          <Trash2 className="h-3 w-3" /> Clear
        </button>
      </div>

      {trailing}
    </div>
  );
}

const BUTTON_CLASS =
  "flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50";
