import { useCallback, useEffect, useRef, useState } from "react";
import { describeApiError, streamApiRun } from "../api";
import { useNotification } from "@/components/layout/notification-context";
import {
    cancelRunState,
    finalizeRunState,
    initialApiRunState,
    reduceApiRunEvent,
    startedApiRunState,
    type ApiRunState,
} from "../api-run-utils";
import { isAbortError } from "./useAgent";
import type { ApiRunEvent, ApiRunRequest } from "../types";

export interface ApiRunCallbacks {
    /** Every event as it streams in — the page uses it to mirror step responses
     *  into the response viewer. */
    onEvent?: (event: ApiRunEvent) => void;
    /** Terminal states: `done` and `aborted` events. */
    onFinished?: (state: ApiRunState) => void;
}

export interface ApiRun {
    state: ApiRunState;
    /**
     * Starts a run: POSTs the plan request and folds the SSE stream into
     * `state`. A previous run is aborted first — a run is singleton UI state,
     * not a queue. Resolves when the stream ends; errors land on `state.error`
     * (and a notification), never thrown back at the caller.
     */
    start: (req: ApiRunRequest, callbacks?: ApiRunCallbacks) => void;
    /** Client-side cancel — aborts the fetch; state settles to `aborted`. */
    abort: () => void;
}

/**
 * Run state machine for `POST /api/api-client/run`. Not a TanStack mutation —
 * the run is a long-lived stream, not a request/response — so it owns its
 * `ApiRunState` and reduces each SSE event into it, the same division of labor
 * as `useAgentChatStream` over `streamAgentChat`.
 */
export function useApiRun(): ApiRun {
    const { notify } = useNotification();
    const [state, setState] = useState<ApiRunState>(initialApiRunState);
    const abortRef = useRef<AbortController | null>(null);
    // Latest-state ref so event callbacks (fired from inside the streaming
    // promise, outside React) can hand the *final* state to `onFinished`
    // instead of a render snapshot.
    const stateRef = useRef(state);
    useEffect(() => {
        stateRef.current = state;
    }, [state]);

    const abort = useCallback(() => {
        abortRef.current?.abort();
    }, []);

    // A mounted component must never leak its stream — an abandoned run keeps
    // executing server-side until the process notices the dead socket.
    useEffect(() => () => abortRef.current?.abort(), []);

    const start = useCallback(
        (req: ApiRunRequest, callbacks?: ApiRunCallbacks) => {
            abortRef.current?.abort();
            const controller = new AbortController();
            abortRef.current = controller;

            const initial = startedApiRunState();
            stateRef.current = initial;
            setState(initial);

            // Per-run latest state — a superseded run's stream is dead to the
            // UI, but its caller still needs onFinished to fire (e.g. to clear
            // `sending` on the tab that launched it).
            const latest = { current: initial };
            const isCurrent = () => abortRef.current === controller;
            const publish = (next: ApiRunState) => {
                latest.current = next;
                if (isCurrent()) {
                    stateRef.current = next;
                    setState(next);
                }
            };
            const notifyFinished = () => {
                try {
                    callbacks?.onFinished?.(latest.current);
                } catch (err) {
                    console.error("api run onFinished callback failed", err);
                }
            };

            // Snapshot ownership *before* clearing the controller — publish()
            // gates UI writes on isCurrent(), which flips false as soon as
            // abortRef is nulled.
            const settle = (next: ApiRunState) => {
                const current = isCurrent();
                if (current) abortRef.current = null;
                latest.current = next;
                if (current) {
                    stateRef.current = next;
                    setState(next);
                }
            };

            const onEvent = (event: ApiRunEvent) => {
                // Reduce synchronously against the ref — functional setState
                // updaters run lazily at React's next flush, so a ref written
                // *inside* the updater lags the stream by a render. The end-of-
                // stream fallback in .then() reads this ref; if it were stale it
                // would fabricate a "done" over the real queued terminal event.
                if (!isCurrent()) return;
                const next = reduceApiRunEvent(stateRef.current, event);
                publish(next);
                try {
                    callbacks?.onEvent?.(event);
                } catch (err) {
                    // A listener failure must not kill the stream — the run
                    // continues and only the mirroring is lost.
                    console.error("api run onEvent callback failed", err);
                }
            };

            void streamApiRun(req, onEvent, controller.signal)
                .then(() => {
                    // The stream ended without a terminal event — treat as
                    // done-with-what-we-saw so the drawer never spins forever.
                    // `latest` is synchronously reduced per event, so a real
                    // `done`/`aborted` already applied is reflected here — this
                    // never overwrites a terminal state with a stale snapshot.
                    settle(finalizeRunState(latest.current));
                    notifyFinished();
                })
                .catch((err: unknown) => {
                    if (isAbortError(err)) {
                        // Client-side abort — the fetch rejects before the
                        // server can send its `aborted` frame, so the cancel is
                        // synthesized here.
                        settle(cancelRunState(latest.current));
                        notifyFinished();
                        return;
                    }
                    const message = describeApiError(err);
                    const current = isCurrent();
                    settle({
                        ...latest.current,
                        status: "error",
                        error: message,
                    });
                    if (current) {
                        notify("error", "Couldn't run requests", message);
                    }
                    notifyFinished();
                });
        },
        [notify],
    );

    return { state, start, abort };
}
