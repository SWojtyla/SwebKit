import { useSbPeekMessages, useSbPeekDlq } from "@/lib/hooks";
import type { SbEntityInfo, SbMessage } from "@/lib/types";

interface Props {
  nsId: string | null;
  entity: SbEntityInfo | null;
  viewMode: "active" | "dlq";
  selectedMessage: SbMessage | null;
  onSelectMessage: (message: SbMessage) => void;
}

export function MessageList({ nsId, entity, viewMode, selectedMessage, onSelectMessage }: Props) {
  const activeQuery = useSbPeekMessages(
    viewMode === "active" ? nsId : null,
    entity?.entityPath ?? null,
  );
  const dlqQuery = useSbPeekDlq(
    viewMode === "dlq" ? nsId : null,
    entity?.entityPath ?? null,
  );

  const messages = viewMode === "active" ? activeQuery.data : dlqQuery.data;
  const isLoading = viewMode === "active" ? activeQuery.isLoading : dlqQuery.isLoading;

  if (!entity) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
        Select an entity
      </div>
    );
  }

  if (isLoading) {
    return <div className="p-4 text-sm text-muted-foreground">Loading messages...</div>;
  }

  if (!messages || messages.length === 0) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
        No {viewMode === "dlq" ? "dead-lettered" : "active"} messages
      </div>
    );
  }

  return (
    <div className="overflow-auto">
      {messages.map((msg) => (
        <button
          key={`${msg.messageId}-${msg.sequenceNumber}`}
          onClick={() => onSelectMessage(msg)}
          className={`block w-full border-b px-3 py-2 text-left hover:bg-accent ${
            selectedMessage?.messageId === msg.messageId &&
            selectedMessage?.sequenceNumber === msg.sequenceNumber
              ? "bg-accent"
              : ""
          }`}
        >
          <div className="flex items-center justify-between gap-2">
            <span className="truncate text-sm font-medium">
              {msg.subject || msg.messageId}
            </span>
            {viewMode === "dlq" && msg.deliveryCount > 0 && (
              <span className="shrink-0 rounded bg-destructive/20 px-1.5 py-0.5 text-xs text-destructive">
                ×{msg.deliveryCount}
              </span>
            )}
          </div>
          <div className="mt-0.5 flex items-center gap-2 text-xs text-muted-foreground">
            <span>#{msg.sequenceNumber}</span>
            <span>·</span>
            <span>{new Date(msg.enqueuedAt).toLocaleTimeString()}</span>
          </div>
          {msg.deadLetterReason && viewMode === "dlq" && (
            <div className="mt-1 truncate text-xs text-destructive">
              {msg.deadLetterReason}
            </div>
          )}
        </button>
      ))}
    </div>
  );
}
