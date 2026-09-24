import { useCallback, useEffect, useRef, useState } from "react";
import { parseLogLine, type LogEntry } from "@/lib/log-window";

/// Collects log lines off one or more SSE streams and hands them to React at a fixed
/// ~10 fps instead of once per line.
///
/// A busy pod emits far faster than the browser can paint. Rendering per line saturates
/// the render queue and freezes the UI — see "Never render per received message" in
/// `docs/pitfalls/react-frontend.md`,
/// re-stated as a hard constraint by the archived logs feature ("do not render per log
/// line"). `PodLogView` had its own buffer for this reason; `MultiPodLogView` did not,
/// and called `setLogs` on every message of every pod.
///
/// Lines are parsed on the way in, so the timestamp prefix is split off exactly once
/// rather than on every render.

const FLUSH_INTERVAL_MS = 100;

export interface UseLogBufferResult {
  /** The flushed entries. Changes at most once per flush interval. */
  entries: LogEntry[];
  /** Lines that arrived while the view was frozen, and so are not on screen yet. */
  pending: number;
  /** Appends one raw line, splitting any timestamp prefix. */
  push: (rawLine: string, pod?: string) => void;
  clear: () => void;
  resetPending: () => void;
}

export function useLogBuffer(options: {
  /** Oldest entries are dropped past this, so a long tail cannot exhaust memory. */
  maxBuffer: number;
  /** While true, arrivals still buffer but count towards `pending`. */
  frozen: boolean;
}): UseLogBufferResult {
  const { maxBuffer, frozen } = options;

  const bufferRef = useRef<LogEntry[]>([]);
  const seqRef = useRef(0);
  const pendingRef = useRef(0);
  const frozenRef = useRef(frozen);
  frozenRef.current = frozen;

  const [entries, setEntries] = useState<LogEntry[]>([]);
  const [pending, setPending] = useState(0);

  const push = useCallback(
    (rawLine: string, pod?: string) => {
      const { ts, text } = parseLogLine(rawLine);
      bufferRef.current.push({ text, ts, pod, seq: seqRef.current++ });

      if (bufferRef.current.length > maxBuffer) {
        bufferRef.current = bufferRef.current.slice(-maxBuffer);
      }
      if (frozenRef.current) {
        pendingRef.current += 1;
      }
    },
    [maxBuffer],
  );

  const clear = useCallback(() => {
    bufferRef.current = [];
    pendingRef.current = 0;
    setEntries([]);
    setPending(0);
  }, []);

  const resetPending = useCallback(() => {
    pendingRef.current = 0;
    setPending(0);
  }, []);

  // The flush. Compared by length and pending count rather than deep equality: the
  // buffer is append-and-trim only, so a changed length is the only way its contents
  // can differ from what was last handed over.
  useEffect(() => {
    const id = setInterval(() => {
      if (bufferRef.current.length !== entries.length || pendingRef.current !== pending) {
        setEntries([...bufferRef.current]);
        setPending(pendingRef.current);
      }
    }, FLUSH_INTERVAL_MS);
    return () => clearInterval(id);
  }, [entries.length, pending]);

  return { entries, pending, push, clear, resetPending };
}
