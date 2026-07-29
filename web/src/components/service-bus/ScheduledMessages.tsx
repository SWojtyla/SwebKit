import { X, Clock, Trash2, RefreshCw } from "lucide-react";
import { useSbScheduledMessages, useSbCancelScheduled } from "@/lib/hooks";

interface Props {
  nsId: string;
  entityPath: string;
  onClose: () => void;
}

export function ScheduledMessages({ nsId, entityPath, onClose }: Props) {
  const { data: entries, isLoading, refetch } = useSbScheduledMessages(nsId, entityPath);
  const cancelMutation = useSbCancelScheduled();

  const sorted = (entries ?? []).slice().sort(
    (a, b) => new Date(a.scheduledEnqueueTime).getTime() - new Date(b.scheduledEnqueueTime).getTime(),
  );

  const now = Date.now();

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" data-testid="scheduled-overlay">
      <div className="flex max-h-[80vh] w-[600px] flex-col rounded-lg border bg-card shadow-lg" data-testid="scheduled-messages-panel">
        {/* Header */}
        <div className="flex items-center justify-between border-b px-4 py-3">
          <div className="flex items-center gap-2">
            <Clock className="h-4 w-4 text-primary" />
            <h2 className="text-sm font-semibold" data-testid="scheduled-title">
              Scheduled Messages — {entityPath}
            </h2>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => refetch()}
              className="rounded p-1 text-muted-foreground hover:bg-accent"
              title="Refresh"
              data-testid="scheduled-refresh"
            >
              <RefreshCw className="h-3.5 w-3.5" />
            </button>
            <button onClick={onClose} className="text-muted-foreground hover:text-foreground" data-testid="scheduled-close">
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-auto">
          {isLoading ? (
            <div className="p-4 text-sm text-muted-foreground">Loading...</div>
          ) : sorted.length === 0 ? (
            <div className="flex flex-col items-center justify-center p-8 text-center" data-testid="scheduled-empty">
              <Clock className="mb-2 h-8 w-8 text-muted-foreground/50" />
              <div className="text-sm font-medium">No scheduled messages</div>
              <div className="mt-1 text-xs text-muted-foreground">
                Use the Schedule action in the composer to schedule a message for future delivery.
              </div>
            </div>
          ) : (
            <table className="w-full text-sm" data-testid="scheduled-table">
              <thead>
                <tr className="border-b text-xs text-muted-foreground">
                  <th className="px-3 py-2 text-left font-medium">Enqueue At</th>
                  <th className="px-3 py-2 text-left font-medium">Message ID</th>
                  <th className="px-3 py-2 text-left font-medium">Subject</th>
                  <th className="px-3 py-2 text-left font-medium">Seq #</th>
                  <th className="px-3 py-2 text-left font-medium">Status</th>
                  <th className="px-3 py-2"></th>
                </tr>
              </thead>
              <tbody>
                {sorted.map((entry) => {
                  const enqueueTime = new Date(entry.scheduledEnqueueTime).getTime();
                  const isPast = enqueueTime < now;
                  return (
                    <tr key={entry.id} className="border-b last:border-0" data-testid={`scheduled-row-${entry.id}`}>
                      <td className="px-3 py-2 font-mono text-xs">
                        {new Date(entry.scheduledEnqueueTime).toLocaleString()}
                      </td>
                      <td className="px-3 py-2 font-mono text-xs text-muted-foreground">
                        {entry.messageId ? (entry.messageId.length > 16 ? entry.messageId.slice(0, 16) + "…" : entry.messageId) : "—"}
                      </td>
                      <td className="px-3 py-2 text-xs">{entry.subject ?? "—"}</td>
                      <td className="px-3 py-2 font-mono text-xs">{entry.sequenceNumber}</td>
                      <td className="px-3 py-2 text-xs">
                        {isPast ? (
                          <span className="rounded bg-muted px-1.5 py-0.5 text-muted-foreground">Enqueued</span>
                        ) : (
                          <span className="rounded bg-primary/10 px-1.5 py-0.5 text-primary">Scheduled</span>
                        )}
                      </td>
                      <td className="px-3 py-2">
                        {!isPast && (
                          <button
                            onClick={() =>
                              cancelMutation.mutate({
                                nsId,
                                entityPath,
                                sequenceNumber: entry.sequenceNumber,
                              })
                            }
                            disabled={cancelMutation.isPending}
                            className="rounded p-1 text-muted-foreground hover:bg-destructive/10 hover:text-destructive"
                            title="Cancel scheduled message"
                            data-testid={`scheduled-cancel-${entry.id}`}
                          >
                            <Trash2 className="h-3.5 w-3.5" />
                          </button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
        </div>

        {/* Footer */}
        <div className="border-t px-4 py-2 text-xs text-muted-foreground">
          {sorted.length} scheduled message{sorted.length !== 1 ? "s" : ""}
        </div>
      </div>
    </div>
  );
}
