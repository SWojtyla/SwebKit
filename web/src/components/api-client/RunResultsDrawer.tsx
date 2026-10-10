import { useEffect, useRef, useState, useCallback } from "react";
import {
    X,
    Loader2,
    CheckCircle2,
    XCircle,
    Circle,
    ChevronDown,
    ChevronRight,
    CornerDownRight,
    ListOrdered,
    OctagonX,
} from "lucide-react";
import type { ApiRunState, ApiRunStepState } from "@/lib/api-run-utils";
import { finishedStepCount } from "@/lib/api-run-utils";
import {
    loadViewPreference,
    saveViewPreference,
} from "@/lib/stores/panel-preferences";

const HEIGHT_PREF_KEY = "api-client-run-drawer-height";
const MIN_HEIGHT = 160;
const MAX_HEIGHT = 560;
const DEFAULT_HEIGHT = 260;

interface RunResultsDrawerProps {
    state: ApiRunState;
    onAbort: () => void;
    onClose: () => void;
}

/**
 * Live view of an in-flight or finished request run — one row per plan step,
 * streaming in as SSE events fold into `state`. Opens automatically when a run
 * starts; anchored to the bottom of the API Client page and resizable by its
 * top edge (same pointer/keyboard resizer contract as {@link GitDrawer}).
 *
 * Closing the drawer does not stop the run — only `run-abort` does.
 */
export function RunResultsDrawer({
    state,
    onAbort,
    onClose,
}: RunResultsDrawerProps) {
    const drawerRef = useRef<HTMLDivElement>(null);
    const [height, setHeight] = useState<number>(() =>
        clamp(
            Number(
                loadViewPreference<string>(
                    HEIGHT_PREF_KEY,
                    String(DEFAULT_HEIGHT),
                ),
            ) || DEFAULT_HEIGHT,
        ),
    );
    const heightRef = useRef(height);
    useEffect(() => {
        heightRef.current = height;
    }, [height]);
    const draggingRef = useRef(false);

    // Drawer does not steal focus on open — a run usually starts from a button
    // the user may want to keep the flow of. Escape still closes it while focus
    // is inside; a global Escape would fight the tree's clear-selection Escape.
    useEffect(() => {
        const el = drawerRef.current;
        if (!el) return;
        const onKeyDown = (e: KeyboardEvent) => {
            if (e.key === "Escape") {
                e.stopPropagation();
                onClose();
            }
        };
        el.addEventListener("keydown", onKeyDown);
        return () => el.removeEventListener("keydown", onKeyDown);
    }, [onClose]);

    const startResize = useCallback((e: React.PointerEvent) => {
        e.preventDefault();
        draggingRef.current = true;
        document.body.style.userSelect = "none";
        document.body.style.cursor = "row-resize";
    }, []);

    useEffect(() => {
        const move = (e: PointerEvent) => {
            if (!draggingRef.current) return;
            const bottom = drawerRef.current?.getBoundingClientRect().bottom;
            if (bottom == null) return;
            // Bottom-anchored: height grows as the pointer moves up.
            setHeight(clamp(bottom - e.clientY));
        };
        const up = () => {
            if (!draggingRef.current) return;
            draggingRef.current = false;
            document.body.style.userSelect = "";
            document.body.style.cursor = "";
            saveViewPreference(HEIGHT_PREF_KEY, String(heightRef.current));
        };
        window.addEventListener("pointermove", move);
        window.addEventListener("pointerup", up);
        window.addEventListener("pointercancel", up);
        return () => {
            window.removeEventListener("pointermove", move);
            window.removeEventListener("pointerup", up);
            window.removeEventListener("pointercancel", up);
        };
    }, []);

    const finished = finishedStepCount(state.steps);
    const total = state.steps.length;
    const running = state.status === "running";

    return (
        <div
            ref={drawerRef}
            role="dialog"
            aria-label="Request run"
            tabIndex={-1}
            style={{ height }}
            className="absolute inset-x-0 bottom-0 z-40 flex flex-col border-t bg-popover shadow-lg outline-none"
            data-testid="run-drawer"
        >
            <div
                role="separator"
                aria-orientation="horizontal"
                aria-label="Resize run results"
                tabIndex={0}
                onPointerDown={startResize}
                onKeyDown={(e) => {
                    if (e.key !== "ArrowUp" && e.key !== "ArrowDown") return;
                    e.preventDefault();
                    const step = e.shiftKey ? 64 : 16;
                    const next = clamp(
                        heightRef.current +
                            (e.key === "ArrowUp" ? step : -step),
                    );
                    setHeight(next);
                    saveViewPreference(HEIGHT_PREF_KEY, String(next));
                }}
                className="absolute inset-x-0 top-0 h-1.5 cursor-row-resize bg-transparent hover:bg-primary/40 focus-visible:bg-primary/60"
                data-testid="run-drawer-resizer"
            />

            <div className="flex shrink-0 items-center gap-2 border-b px-3 py-2">
                <ListOrdered className="h-4 w-4 text-muted-foreground" />
                <h2 className="text-sm font-semibold">Request run</h2>
                <span
                    className="text-xs text-muted-foreground"
                    data-testid="run-progress"
                >
                    {total > 0
                        ? `${finished}/${total}`
                        : running
                          ? "Planning…"
                          : ""}
                </span>
                {state.status === "done" && (
                    <span
                        className="text-xs text-muted-foreground"
                        data-testid="run-summary"
                    >
                        ·{" "}
                        {state.summary
                            ? `${state.summary.completedSteps} completed, ${state.summary.failedSteps} failed in ${Math.round(state.summary.durationMs)} ms`
                            : "finished"}
                    </span>
                )}
                {state.status === "aborted" && (
                    <span
                        className="text-xs"
                        style={{ color: "var(--warning)" }}
                        data-testid="run-summary"
                    >
                        ·{" "}
                        {state.abortReason === "stopOnError"
                            ? "Stopped on first error"
                            : "Cancelled"}
                    </span>
                )}
                {state.status === "error" && (
                    <span
                        className="text-xs text-destructive"
                        data-testid="run-summary"
                    >
                        · {state.error ?? "Run failed"}
                    </span>
                )}
                {running && (
                    <button
                        onClick={onAbort}
                        className="ml-auto flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
                        data-testid="run-abort"
                        title="Cancel the run"
                    >
                        <OctagonX className="h-3 w-3" /> Abort
                    </button>
                )}
                <button
                    onClick={onClose}
                    className={`rounded p-1 text-muted-foreground hover:bg-accent ${running ? "" : "ml-auto"}`}
                    aria-label="Close run results"
                    data-testid="run-drawer-close"
                >
                    <X className="h-4 w-4" />
                </button>
            </div>

            <div className="min-h-0 flex-1 overflow-auto px-2 py-1">
                {total === 0 && (
                    <div className="p-2 text-xs text-muted-foreground">
                        Waiting for the run plan…
                    </div>
                )}
                {state.steps.map((step) => (
                    <RunStepRow key={step.index} step={step} />
                ))}
            </div>
        </div>
    );
}

function RunStepRow({ step }: { step: ApiRunStepState }) {
    const [expanded, setExpanded] = useState(false);
    const hasBody =
        step.response?.responseBody != null &&
        step.response.responseBody.length > 0;

    return (
        <div
            className={`border-b border-border/50 py-1 last:border-b-0 ${
                step.isDependency ? "opacity-80" : ""
            }`}
            data-testid={`run-step-${step.index}`}
            data-dependency={step.isDependency || undefined}
        >
            <div
                className={`flex items-center gap-2 text-sm ${
                    // Expanded dependency steps indent under the chain step
                    // that pulled them in — they're not declared steps.
                    step.isDependency ? "ml-4" : ""
                }`}
            >
                <span
                    className="w-6 shrink-0 text-right text-xs text-muted-foreground"
                    aria-label={`Step ${step.index + 1}`}
                >
                    {step.index + 1}
                </span>
                {step.isDependency && (
                    <span
                        className="flex shrink-0 items-center gap-0.5 text-[10px] uppercase tracking-wide text-muted-foreground"
                        title="Dependency pulled in by a chain step"
                        data-testid={`run-dep-${step.index}`}
                    >
                        <CornerDownRight className="h-3 w-3" />
                        dep
                    </span>
                )}
                <span
                    data-testid={`run-status-${step.index}`}
                    data-status={step.status}
                    className="flex shrink-0 items-center"
                >
                    <StepIcon status={step.status} />
                </span>
                <button
                    type="button"
                    className="flex min-w-0 flex-1 items-center gap-1 text-left"
                    onClick={() => hasBody && setExpanded((v) => !v)}
                    disabled={!hasBody}
                    title={
                        hasBody
                            ? "Toggle response body"
                            : step.name
                    }
                    data-testid={`run-step-toggle-${step.index}`}
                >
                    {hasBody ? (
                        expanded ? (
                            <ChevronDown className="h-3 w-3 shrink-0 text-muted-foreground" />
                        ) : (
                            <ChevronRight className="h-3 w-3 shrink-0 text-muted-foreground" />
                        )
                    ) : (
                        <span className="w-3 shrink-0" />
                    )}
                    <span className="truncate">{step.name}</span>
                </button>
                {step.collectionName && (
                    <span
                        className="shrink-0 rounded border bg-muted/40 px-1.5 py-0 text-[10px] text-muted-foreground"
                        title="Collection this step resolved against"
                        data-testid={`run-collection-${step.index}`}
                    >
                        {step.collectionName}
                    </span>
                )}
                {step.httpStatus != null && (
                    <span
                        className={`shrink-0 font-mono text-xs ${
                            step.httpStatus >= 200 && step.httpStatus < 400
                                ? "text-success"
                                : "text-destructive"
                        }`}
                        data-testid={`run-http-status-${step.index}`}
                    >
                        {step.httpStatus}
                    </span>
                )}
                {step.durationMs != null && (
                    <span className="shrink-0 text-xs text-muted-foreground">
                        {Math.round(step.durationMs)} ms
                    </span>
                )}
            </div>
            {step.error && (
                <div
                    className="ml-8 mt-0.5 text-xs text-destructive"
                    data-testid={`run-error-${step.index}`}
                >
                    {step.error}
                </div>
            )}
            {step.captured.length > 0 && (
                <div
                    className="ml-8 mt-0.5 flex flex-wrap gap-1"
                    data-testid={`run-captured-${step.index}`}
                >
                    {step.captured.map((c) => (
                        <span
                            key={`${c.targetVariable}:${c.source}`}
                            className="rounded border bg-muted/40 px-1.5 py-0 font-mono text-[11px] text-muted-foreground"
                            title={`Captured from ${c.source}${
                                c.scope === "run"
                                    ? " — run scope (visible to later steps, dies with the run)"
                                    : ""
                            }`}
                            data-scope={c.scope ?? "environment"}
                        >
                            {c.targetVariable}
                            {c.scope === "run" && (
                                <span className="ml-1 text-[9px] uppercase tracking-wide text-primary">
                                    run
                                </span>
                            )}
                        </span>
                    ))}
                </div>
            )}
            {expanded && hasBody && (
                <pre
                    className="ml-8 mt-1 max-h-48 overflow-auto whitespace-pre-wrap break-all rounded bg-muted/40 p-2 font-mono text-xs"
                    data-testid={`run-body-${step.index}`}
                >
                    {step.response!.responseBody}
                </pre>
            )}
        </div>
    );
}

function StepIcon({ status }: { status: ApiRunStepState["status"] }) {
    switch (status) {
        case "running":
            return (
                <Loader2
                    className="h-3.5 w-3.5 animate-spin text-primary"
                    aria-label="Running"
                />
            );
        case "completed":
            return (
                <CheckCircle2
                    className="h-3.5 w-3.5 text-success"
                    aria-label="Completed"
                />
            );
        case "failed":
            return (
                <XCircle
                    className="h-3.5 w-3.5 text-destructive"
                    aria-label="Failed"
                />
            );
        default:
            return (
                <Circle
                    className="h-3.5 w-3.5 text-muted-foreground"
                    aria-label="Pending"
                />
            );
    }
}

function clamp(height: number): number {
    return Math.max(MIN_HEIGHT, Math.min(MAX_HEIGHT, Math.round(height)));
}
