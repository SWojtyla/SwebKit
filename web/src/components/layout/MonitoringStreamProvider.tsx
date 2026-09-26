import { useCallback, useEffect, useMemo, useRef, type ReactNode } from "react";
import { SIDECAR_BASE_URL } from "@/lib/api";
import {
    MonitoringStreamContext,
    type MonitoringStreamApi,
    type MonitoringStreamFrame,
    type MonitoringStreamListener,
} from "@/lib/monitoring-stream-context";
import type {
    AlertEvaluatedEvent,
    ProactiveInsightStatusEvent,
} from "@/lib/api";

/** Buffer caps per frame kind — generous enough that a late-mounting page still gets every
 * event it would have seen live, without an unbounded list while the app sits open. */
const ALERT_HISTORY_CAP = 200; // matches MonitoringPage's liveEvents cap
const INSIGHT_CAP = 50;
const OTHER_CAP = 100;

/** evaluationCompleted fires per rule per tick and insight statuses repeat per firing — keeping
 * every one would drown the buffer, so only the newest per entity survives for replay. */
function dedupeKey(frame: MonitoringStreamFrame): string | null {
    if (frame.kind === "evaluationCompleted")
        return `eval|${(frame.event as AlertEvaluatedEvent).ruleId}`;
    if (frame.kind === "proactiveInsightStatus") {
        const e = frame.event as ProactiveInsightStatusEvent;
        return `status|${e.ruleId}|${e.firedAt}`;
    }
    return null;
}

function pushBuffered(buffer: MonitoringStreamFrame[], frame: MonitoringStreamFrame) {
    const key = dedupeKey(frame);
    if (key !== null) {
        const idx = buffer.findIndex((f) => dedupeKey(f) === key);
        if (idx >= 0) buffer.splice(idx, 1);
    }
    buffer.push(frame);

    const cap =
        frame.kind === "alertFired"
            ? ALERT_HISTORY_CAP
            : frame.kind === "proactiveInsightReady"
              ? INSIGHT_CAP
              : OTHER_CAP;
    let count = 0;
    for (const f of buffer) if (f.kind === frame.kind) count++;
    if (count > cap) {
        const idx = buffer.findIndex((f) => f.kind === frame.kind);
        if (idx >= 0) buffer.splice(idx, 1);
    }
}

/**
 * Owns the app's single monitoring SSE connection and fans frames out to subscribers.
 * Mounted once above `AppLayout` so the always-mounted layout, the dashboard, and the
 * monitoring page all share one transport instead of one EventSource each.
 */
export function MonitoringStreamProvider({ children }: { children: ReactNode }) {
    const listenersRef = useRef(new Set<MonitoringStreamListener>());
    const bufferRef = useRef<MonitoringStreamFrame[]>([]);
    const seqRef = useRef(0);

    useEffect(() => {
        const es = new EventSource(`${SIDECAR_BASE_URL}/api/monitoring/stream`);
        es.onmessage = (msg) => {
            try {
                const parsed = JSON.parse(msg.data) as {
                    kind?: unknown;
                    event?: unknown;
                };
                if (typeof parsed?.kind !== "string") return;
                const frame: MonitoringStreamFrame = {
                    seq: ++seqRef.current,
                    kind: parsed.kind,
                    event: parsed.event,
                };
                pushBuffered(bufferRef.current, frame);
                for (const listener of listenersRef.current) listener(frame);
            } catch {
                /* ignore malformed frames */
            }
        };
        return () => es.close();
    }, []);

    const subscribe = useCallback<MonitoringStreamApi["subscribe"]>(
        (listener, afterSeq) => {
            // Replay first, then register — EventSource callbacks can't interleave with this
            // synchronous block, so nothing is missed or doubled between the two steps.
            for (const frame of bufferRef.current)
                if (frame.seq > afterSeq) listener(frame);
            listenersRef.current.add(listener);
            return () => {
                listenersRef.current.delete(listener);
            };
        },
        [],
    );

    const api = useMemo<MonitoringStreamApi>(() => ({ subscribe }), [subscribe]);

    return (
        <MonitoringStreamContext.Provider value={api}>
            {children}
        </MonitoringStreamContext.Provider>
    );
}
