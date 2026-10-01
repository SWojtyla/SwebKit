import { useState, useEffect, useRef, useMemo, useCallback } from "react";
import { X } from "lucide-react";
import { SIDECAR_BASE_URL } from "@/lib/api";
import { mergeByTimestamp, type TimestampMode } from "@/lib/log-window";
import {
    loadViewPreference,
    saveViewPreference,
} from "@/lib/stores/panel-preferences";
import { LogToolbar } from "./shared/LogToolbar";
import { LogOutput } from "./shared/LogOutput";
import { useLogBuffer } from "./shared/useLogBuffer";
import { useLogWindow } from "./shared/useLogWindow";
import { multiPodLogRangeOptions, type LogRange } from "./shared/logRange";
import type { ScopedKey } from "./shared/aks-workspace-context";

interface Props {
    /** Pods to correlate — scoped so a pod picked from an attached cluster streams
     * from its own context and namespace, not the primary's. */
    pods: ScopedKey[];
    onClose: () => void;
}

const VISIBLE = 200;
const MAX_BUFFER = 50_000;
const TAIL_INITIAL = 2_000;
const EXPORT_TAIL = 200_000;
const TIMESTAMP_PREF_KEY = "aks-log-timestamp-mode";

/// Builds the per-pod stream query. Mirrors `PodLogView`'s `streamParams` so "Last 5m" means
/// exactly the same thing in both views. `previousContainer` is a required (non-nullable, no
/// default) query parameter on the sidecar's `/logs/stream` route — omitting it entirely, as
/// this view previously did, fails ASP.NET's minimal-API model binding with a 400 before any
/// application code runs, indistinguishable client-side from a stream that just never delivers
/// anything. Always sending it (single-pod always did) is what actually made every multi-pod
/// stream request fail, in both demo and real mode alike.
function streamParams(
    range: LogRange,
    container: string,
    follow: boolean,
    tail?: number,
) {
    const since = multiPodLogRangeOptions.find((r) => r.value === range)?.since;
    const params = new URLSearchParams({
        follow: String(follow),
        tail: String(tail ?? (since ? 100 : TAIL_INITIAL)),
        timestamps: "true",
        previousContainer: "false",
    });
    if (container) params.set("container", container);
    if (since) params.set("sinceSeconds", String(since));
    return params;
}

/** Stream map key — one SSE source per (context, ns, pod). */
function podKey(p: ScopedKey): string {
    return `${p.context ?? ""}:${p.ns}/${p.name}`;
}

/** Chip label — the context prefix only matters when pods come from more than one
 * cluster. `context` null means the primary context (legacy bare URL keys), which the
 * view can't resolve to a name — the namespace is the better disambiguator there. */
function podLabel(p: ScopedKey, multiContext: boolean): string {
    return multiContext ? `${p.context ?? p.ns}/${p.name}` : p.name;
}

export function MultiPodLogView({ pods, onClose }: Props) {
    // Every pod starts streaming. The user opened a correlation view for this exact set of
    // pods, so opening onto a dead panel that needs a click per pod before any log appears
    // inverts the intent — correlation is the default, deselecting is the exception. `pods`
    // is memoised off the URL param upstream, so this re-syncs only when the set itself
    // genuinely changes, not on every render.
    const [selectedPods, setSelectedPods] = useState<string[]>(
        pods.map(podKey),
    );
    const [prevPods, setPrevPods] = useState(pods);
    if (prevPods !== pods) {
        setPrevPods(pods);
        setSelectedPods(pods.map(podKey));
    }
    const podByKey = useMemo(
        () => new Map(pods.map((p) => [podKey(p), p])),
        [pods],
    );
    const multiContext = new Set(pods.map((p) => p.context ?? "")).size > 1;

    const [container, setContainer] = useState("");
    const [range, setRange] = useState<LogRange>("5m");
    const [timestampMode, setTimestampMode] = useState<TimestampMode>(() =>
        loadViewPreference<TimestampMode>(TIMESTAMP_PREF_KEY, "time"),
    );
    const [isExporting, setIsExporting] = useState(false);
    // Per-pod stream failures (pod deleted mid-correlation, RBAC, etc.) — surfaced instead of
    // leaving the panel showing "Connecting..." forever with no indication anything went wrong.
    const [streamErrors, setStreamErrors] = useState<Map<string, string>>(
        new Map(),
    );

    const sourcesRef = useRef<Map<string, EventSource>>(new Map());
    const streamKeyRef = useRef(`${container}::${range}`);
    const scrollRef = useRef<HTMLDivElement>(null);

    const buffer = useLogBuffer({ maxBuffer: MAX_BUFFER });
    const { push, clear } = buffer;

    // Correlation is the point of this view, and arrival order does not give it: network
    // jitter delivers lines out of order, so two events seconds apart can read as
    // simultaneous. Sorting by the pods' own timestamps is what makes the view honest.
    const ordered = useMemo(
        () => mergeByTimestamp(buffer.entries),
        [buffer.entries],
    );
    const win = useLogWindow(ordered, VISIBLE);

    const applyTimestampMode = (mode: TimestampMode) => {
        setTimestampMode(mode);
        saveViewPreference(TIMESTAMP_PREF_KEY, mode);
    };

    // One SSE stream per selected pod (matches the sidecar's actual log-stream contract —
    // it's SSE, not a WebSocket). Deselecting a pod closes just its stream; selecting one
    // opens a new one alongside the rest, so logs from multiple pods interleave for
    // correlation. Changing namespace/container tears down and reopens every stream since
    // the filter itself changed.
    useEffect(() => {
        const sources = sourcesRef.current;
        const streamKey = `${container}::${range}`;
        const filterChanged = streamKeyRef.current !== streamKey;
        streamKeyRef.current = streamKey;

        if (filterChanged) {
            for (const es of sources.values()) es.close();
            sources.clear();
            clear();
            setStreamErrors(new Map());
        } else {
            for (const [pod, es] of sources) {
                if (!selectedPods.includes(pod)) {
                    es.close();
                    sources.delete(pod);
                    setStreamErrors((prev) => {
                        if (!prev.has(pod)) return prev;
                        const next = new Map(prev);
                        next.delete(pod);
                        return next;
                    });
                }
            }
        }

        for (const pod of selectedPods) {
            if (sources.has(pod)) continue;
            setStreamErrors((prev) => {
                if (!prev.has(pod)) return prev;
                const next = new Map(prev);
                next.delete(pod);
                return next;
            });
            const target = podByKey.get(pod);
            if (!target) continue;
            // `timestamps` asks the container runtime for the real emission time. Without it
            // the only clock available is the browser's arrival time, which is what made the
            // correlation misleading. Leaving `container` unset is safe: the sidecar resolves an
            // ambiguous multi-container pod to its first container instead of erroring.
            const params = streamParams(range, container, true);
            if (target.context) params.set("context", target.context);
            const es = new EventSource(
                `${SIDECAR_BASE_URL}/api/aks/${target.ns}/pods/${target.name}/logs/stream?${params}`,
            );
            es.onmessage = (e) => push(e.data, podLabel(target, multiContext));
            // A named event the sidecar frames ahead of `done` when the underlying stream failed
            // (pod gone, RBAC, ...). Deliberately not `error` — EventSource reserves that type for
            // native connection failures, and a server frame named `event: error` would land on the
            // same listener as one, indistinguishable without inspecting the event shape.
            es.addEventListener("stream-error", (e) => {
                const message = (e as MessageEvent).data || "Stream failed";
                setStreamErrors((prev) => new Map(prev).set(pod, message));
            });
            es.addEventListener("done", () => {
                es.close();
                sources.delete(pod);
            });
            es.onerror = () => {
                es.close();
                sources.delete(pod);
                setStreamErrors((prev) =>
                    new Map(prev).set(pod, "Connection closed unexpectedly."),
                );
            };
            sources.set(pod, es);
        }
    }, [selectedPods, podByKey, container, range, push, clear, multiContext]);

    // Full teardown on unmount: an SSE stream left open after the panel closes keeps
    // delivering into a dead component.
    useEffect(() => {
        const sources = sourcesRef.current;
        return () => {
            for (const es of sources.values()) es.close();
            sources.clear();
        };
    }, []);

    // Stick to the bottom while following, exactly as the single-pod view does.
    useEffect(() => {
        if (!win.frozen && scrollRef.current) {
            scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
        }
    }, [ordered.length, win.frozen]);

    const togglePod = (pod: string) => {
        setSelectedPods((prev) =>
            prev.includes(pod) ? prev.filter((p) => p !== pod) : [...prev, pod],
        );
    };

    const handleCopyVisible = useCallback(async () => {
        if (win.visible.length === 0) return;
        try {
            await navigator.clipboard.writeText(
                win.visible
                    .map((e) => `${e.ts ?? ""} ${e.pod}: ${e.text}`.trim())
                    .join("\n"),
            );
        } catch {
            // Clipboard can be unavailable; nothing useful to tell the user.
        }
    }, [win.visible]);

    // Exports every selected pod, not just what is on screen, and tags each line with its
    // pod so the merged file is still readable.
    const handleExport = useCallback(() => {
        if (isExporting || selectedPods.length === 0) return;
        setIsExporting(true);

        const collected: { pod: string; line: string }[] = [];
        let remaining = selectedPods.length;

        const finish = () => {
            remaining -= 1;
            if (remaining > 0) return;
            setIsExporting(false);
            const body = collected.map((c) => `${c.pod}: ${c.line}`).join("\n");
            const blob = new Blob([body], { type: "text/plain" });
            const a = document.createElement("a");
            a.href = URL.createObjectURL(blob);
            a.download = `${pods.map(podKey).join(",")}-multi-pod.log`;
            a.click();
            URL.revokeObjectURL(a.href);
        };

        for (const pod of selectedPods) {
            const target = podByKey.get(pod);
            if (!target) {
                finish();
                continue;
            }
            const params = streamParams(range, container, false, EXPORT_TAIL);
            if (target.context) params.set("context", target.context);
            const es = new EventSource(
                `${SIDECAR_BASE_URL}/api/aks/${target.ns}/pods/${target.name}/logs/stream?${params}`,
            );
            es.onmessage = (e) => collected.push({ pod, line: e.data });
            es.addEventListener("done", () => {
                es.close();
                finish();
            });
            es.onerror = () => {
                es.close();
                finish();
            };
        }
    }, [isExporting, selectedPods, podByKey, container, range, pods]);

    const handleClear = useCallback(() => {
        clear();
        win.reset();
    }, [clear, win]);

    return (
        <div className="flex h-full flex-col" data-testid="multi-pod-log-view">
            <div className="flex items-center justify-between border-b px-4 py-3">
                <h2 className="text-sm font-semibold">
                    Multi-Pod Log Correlation
                </h2>
                <button
                    onClick={onClose}
                    className="text-muted-foreground hover:text-foreground"
                    data-testid="multi-pod-log-close"
                >
                    <X className="h-4 w-4" />
                </button>
            </div>

            <div className="border-b px-4 py-2">
                <span className="text-xs text-muted-foreground">
                    Pods being correlated:
                </span>
                <div className="mt-1 flex flex-wrap gap-1">
                    {pods.map((pod) => {
                        const key = podKey(pod);
                        return (
                            <button
                                key={key}
                                onClick={() => togglePod(key)}
                                className={`rounded px-2 py-1 text-xs ${selectedPods.includes(key) ? "bg-primary text-primary-foreground" : "border hover:bg-accent"}`}
                                data-testid={`multi-pod-toggle-${pod.name}`}
                                title={
                                    pod.context
                                        ? `${pod.context} · ${pod.ns}/${pod.name}`
                                        : `${pod.ns}/${pod.name}`
                                }
                            >
                                {podLabel(pod, multiContext)}
                            </button>
                        );
                    })}
                </div>
                <div className="mt-2 flex items-center gap-2">
                    <input
                        type="text"
                        value={container}
                        onChange={(e) => setContainer(e.target.value)}
                        placeholder="Container name (optional)"
                        className="rounded border bg-background px-2 py-1 text-xs"
                        data-testid="multi-pod-container-input"
                    />
                    <select
                        value={range}
                        onChange={(e) => setRange(e.target.value as LogRange)}
                        className="rounded border bg-background px-2 py-1 text-xs"
                        data-testid="multi-pod-range-select"
                    >
                        {multiPodLogRangeOptions.map((opt) => (
                            <option key={opt.value} value={opt.value}>
                                {opt.label}
                            </option>
                        ))}
                    </select>
                </div>
                {streamErrors.size > 0 && (
                    <div
                        className="mt-2 text-xs text-destructive"
                        data-testid="multi-pod-log-error"
                    >
                        {[...streamErrors.entries()].map(([pod, message]) => (
                            <div key={pod}>
                                {pod}: {message}
                            </div>
                        ))}
                    </div>
                )}
            </div>

            <LogToolbar
                textFilter={win.textFilter}
                onTextFilterChange={win.setTextFilter}
                searchMode={win.searchMode}
                onSearchModeChange={win.setSearchMode}
                contextLines={win.contextLines}
                onContextLinesChange={win.setContextLines}
                matchCount={win.matchCount}
                canShowPreviousMatch={win.canShowPreviousMatch}
                canShowNextMatch={win.canShowNextMatch}
                onShowPreviousMatch={win.showPreviousMatch}
                onShowNextMatch={win.showNextMatch}
                summary={win.summary}
                timestampMode={timestampMode}
                onTimestampModeChange={applyTimestampMode}
                paused={win.paused}
                onTogglePause={win.togglePause}
                canShowOlder={win.canShowOlder}
                canShowNewer={win.canShowNewer}
                canJumpToLatest={win.frozen || win.heldBack > 0}
                pendingCount={win.heldBack}
                onShowOlder={win.showOlder}
                onShowNewer={win.showNewer}
                onJumpToLatest={win.jumpToLatest}
                onCopyVisible={handleCopyVisible}
                onExport={handleExport}
                onClear={handleClear}
                isExporting={isExporting}
            />

            <LogOutput
                ref={scrollRef}
                entries={win.visible}
                startIndex={win.visibleStart}
                timestampMode={timestampMode}
                highlightTerm={
                    win.searchMode === "context" ? win.textFilter : ""
                }
                showPod
                emptyMessage={
                    selectedPods.length === 0
                        ? "Select pods to start streaming logs"
                        : "Connecting..."
                }
                testId="multi-pod-log-output"
            />
        </div>
    );
}
