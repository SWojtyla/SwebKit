import { useState } from "react";
import { X, RotateCcw, Check, AlertTriangle } from "lucide-react";
import { useSbPeekDlq, useSbResubmitDlq } from "@/lib/hooks";
import type { SbEntityInfo } from "@/lib/types";

interface Props {
  nsId: string;
  entity: SbEntityInfo;
  onClose: () => void;
}

// Batch replay only resubmits dead-lettered messages back onto the same
// entity — that's the only operation the sidecar's `/resubmit` endpoint
// actually supports (it reads from `{entityPath}/$DeadLetterQueue` and writes
// back to `{entityPath}`). There is no source-Active or cross-entity/
// cross-namespace replay endpoint, so this panel doesn't offer either.
export function BatchReplayPanel({ nsId, entity, onClose }: Props) {
  const [selectedSeqs, setSelectedSeqs] = useState<Set<string>>(new Set());
  const [done, setDone] = useState(false);
  const [replaying, setReplaying] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const { data: messages } = useSbPeekDlq(nsId, entity.entityPath);
  const resubmitMutation = useSbResubmitDlq();

  const toggleSelect = (seq: string) => {
    setSelectedSeqs((prev) => {
      const next = new Set(prev);
      if (next.has(seq)) next.delete(seq);
      else next.add(seq);
      return next;
    });
  };

  const selectAll = () => {
    if (messages) setSelectedSeqs(new Set(messages.map((m) => String(m.sequenceNumber))));
  };

  const selectNone = () => setSelectedSeqs(new Set());

  const handleReplay = async () => {
    if (selectedSeqs.size === 0) return;
    setReplaying(true);
    setError(null);
    try {
      const seqs = Array.from(selectedSeqs);
      await resubmitMutation.mutateAsync({
        nsId,
        entityPath: entity.entityPath,
        sequenceNumbers: seqs,
      });
      setDone(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Replay failed");
    } finally {
      setReplaying(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" data-testid="batch-replay-overlay">
      <div className="w-2/3 max-w-3xl rounded-lg border bg-card shadow-xl" data-testid="batch-replay-panel">
        <div className="flex items-center justify-between border-b px-4 py-3">
          <div>
            <h2 className="text-sm font-semibold">Batch Replay</h2>
            <p className="text-xs text-muted-foreground">
              Resubmit dead-lettered messages on <strong>{entity.entityPath}</strong>
            </p>
          </div>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground" data-testid="batch-replay-close">
            <X className="h-4 w-4" />
          </button>
        </div>

        {done ? (
          <div className="flex flex-col items-center justify-center py-12" data-testid="batch-replay-done">
            <Check className="h-8 w-8 text-green-500" />
            <p className="mt-2 text-sm">Successfully replayed {selectedSeqs.size} messages</p>
            <button onClick={onClose} className="mt-4 rounded-md border px-4 py-1.5 text-xs hover:bg-accent">
              Close
            </button>
          </div>
        ) : (
          <>
            {error && (
              <div className="flex items-center gap-2 border-b bg-destructive/10 px-4 py-2 text-xs text-destructive" data-testid="batch-replay-error">
                <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
                {error}
              </div>
            )}

            <div className="border-b px-4 py-2 flex items-center gap-2">
              <button onClick={selectAll} className="text-xs text-primary hover:underline" data-testid="batch-replay-select-all">
                Select all
              </button>
              <button onClick={selectNone} className="text-xs text-muted-foreground hover:underline" data-testid="batch-replay-select-none">
                Select none
              </button>
              <span className="ml-auto text-xs text-muted-foreground" data-testid="batch-replay-count">
                {selectedSeqs.size} selected
              </span>
            </div>

            <div className="max-h-80 overflow-auto" data-testid="batch-replay-message-list">
              {messages?.map((msg) => {
                const seq = String(msg.sequenceNumber);
                const isSelected = selectedSeqs.has(seq);
                return (
                  <label
                    key={seq}
                    className={`flex items-center gap-2 border-b px-3 py-2 cursor-pointer hover:bg-accent ${isSelected ? "bg-accent" : ""}`}
                    data-testid={`batch-replay-msg-${seq}`}
                  >
                    <input
                      type="checkbox"
                      checked={isSelected}
                      onChange={() => toggleSelect(seq)}
                    />
                    <div className="flex-1 min-w-0">
                      <div className="truncate text-sm font-medium">{msg.subject || msg.messageId}</div>
                      <div className="text-xs text-muted-foreground">#{msg.sequenceNumber} · {new Date(msg.enqueuedAt).toLocaleString()}</div>
                    </div>
                    {msg.deadLetterReason && (
                      <span className="text-xs text-destructive truncate max-w-32">{msg.deadLetterReason}</span>
                    )}
                  </label>
                );
              })}
              {(!messages || messages.length === 0) && (
                <div className="py-8 text-center text-sm text-muted-foreground">No dead-lettered messages to replay</div>
              )}
            </div>

            <div className="flex justify-end gap-2 px-4 py-3">
              <button onClick={onClose} className="rounded-md border px-4 py-1.5 text-xs hover:bg-accent">
                Cancel
              </button>
              <button
                onClick={handleReplay}
                disabled={selectedSeqs.size === 0 || replaying}
                className="flex items-center gap-1 rounded-md bg-primary px-4 py-1.5 text-xs text-primary-foreground disabled:opacity-50"
                data-testid="batch-replay-execute"
              >
                <RotateCcw className="h-3.5 w-3.5" />
                {replaying ? "Replaying..." : `Replay ${selectedSeqs.size} messages`}
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
