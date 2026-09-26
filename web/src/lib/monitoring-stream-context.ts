import { createContext, useContext } from "react";

/**
 * Shell-owned fan-out for the sidecar's multiplexed monitoring SSE stream
 * (`/api/monitoring/stream`). Previously every {@link useMonitoringStream} call opened its own
 * `EventSource`, so the dashboard alone held three parallel connections receiving identical
 * frames. `MonitoringStreamProvider` owns the single transport; consumers register listeners
 * here instead.
 */

/** One decoded stream frame. `seq` is a provider-local monotonically increasing id used for
 * replay cursors — it is not a server value. */
export interface MonitoringStreamFrame {
    seq: number;
    /** The envelope's `kind` field — e.g. `alertFired`, `proactiveInsightReady`,
     * `evaluationCompleted`, `proactiveInsightStatus`. */
    kind: string;
    event: unknown;
}

export type MonitoringStreamListener = (frame: MonitoringStreamFrame) => void;

export interface MonitoringStreamApi {
    /** Registers a listener until the returned unsubscribe runs. Frames buffered since the
     * app started are replayed first (oldest to newest, only those with `seq > afterSeq`),
     * so a page mounted later still sees an insight that fired while it wasn't mounted.
     * Passing the last delivered `seq` back in on resubscribe (StrictMode remount) avoids
     * double-delivery. */
    subscribe: (listener: MonitoringStreamListener, afterSeq: number) => () => void;
}

export const MonitoringStreamContext =
    createContext<MonitoringStreamApi | null>(null);

export function useMonitoringStreamApi(): MonitoringStreamApi {
    const ctx = useContext(MonitoringStreamContext);
    if (!ctx)
        throw new Error(
            "useMonitoringStream must be used inside <MonitoringStreamProvider>.",
        );
    return ctx;
}
