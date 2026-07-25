import { useState } from "react";
import {
  useSbCompleteMessages,
  useSbCompleteDlq,
  useSbResubmitDlq,
  useSbPurgeMessages,
} from "@/lib/hooks";
import type { SbEntityInfo, SbMessage } from "@/lib/types";

interface Props {
  message: SbMessage | null;
  nsId: string | null;
  entity: SbEntityInfo | null;
  viewMode: "active" | "dlq";
}

export function MessageDetail({ message, nsId, entity, viewMode }: Props) {
  const completeMutation = useSbCompleteMessages();
  const completeDlqMutation = useSbCompleteDlq();
  const resubmitMutation = useSbResubmitDlq();
  const purgeMutation = useSbPurgeMessages();
  const [showAdvanced, setShowAdvanced] = useState(false);

  if (!message) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground" data-testid="message-detail-empty">
        Select a message to view details
      </div>
    );
  }

  const tryFormatJson = (body: string): string => {
    try {
      return JSON.stringify(JSON.parse(body), null, 2);
    } catch {
      return body;
    }
  };

  const onComplete = () => {
    if (!nsId || !entity || !message.sequenceNumber) return;
    completeMutation.mutate({
      nsId,
      entityPath: entity.entityPath,
      sequenceNumbers: [message.sequenceNumber],
    });
  };

  const onCompleteDlq = () => {
    if (!nsId || !entity || !message.sequenceNumber) return;
    completeDlqMutation.mutate({
      nsId,
      entityPath: entity.entityPath,
      sequenceNumbers: [String(message.sequenceNumber)],
    });
  };

  const onResubmit = () => {
    if (!nsId || !entity || !message.sequenceNumber) return;
    resubmitMutation.mutate({
      nsId,
      entityPath: entity.entityPath,
      sequenceNumbers: [String(message.sequenceNumber)],
    });
  };

  const onPurge = () => {
    if (!nsId || !entity) return;
    if (!confirm(`Purge all ${viewMode === "dlq" ? "dead-lettered" : "active"} messages from ${entity.entityPath}?`)) return;
    purgeMutation.mutate({
      nsId,
      entityPath: entity.entityPath,
      deadLetter: viewMode === "dlq",
    });
  };

  return (
    <div className="p-4" data-testid="message-detail">
      {/* Header */}
      <div className="mb-4 flex items-start justify-between">
        <div>
          <h2 className="text-lg font-semibold" data-testid="message-detail-subject">{message.subject || message.messageId}</h2>
          <p className="mt-0.5 text-xs text-muted-foreground" data-testid="message-detail-meta">
            Message ID: {message.messageId} · Seq: #{message.sequenceNumber}
          </p>
        </div>
        <div className="flex gap-2">
          {viewMode === "active" && (
            <button
              data-testid="message-complete-button"
              onClick={onComplete}
              disabled={completeMutation.isPending}
              className="rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
            >
              Complete
            </button>
          )}
          {viewMode === "dlq" && (
            <>
              <button
                data-testid="message-resubmit-button"
                onClick={onResubmit}
                disabled={resubmitMutation.isPending}
                className="rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
              >
                Resubmit
              </button>
              <button
                data-testid="message-complete-dlq-button"
                onClick={onCompleteDlq}
                disabled={completeDlqMutation.isPending}
                className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
              >
                Complete DLQ
              </button>
            </>
          )}
          <button
            data-testid="message-purge-button"
            onClick={onPurge}
            disabled={purgeMutation.isPending}
            className="rounded-md border border-destructive px-3 py-1.5 text-xs text-destructive hover:bg-destructive/10 disabled:opacity-50"
          >
            Purge All
          </button>
        </div>
      </div>

      {/* Metadata grid */}
      <div className="mb-4 grid grid-cols-2 gap-x-6 gap-y-2 text-sm">
        <Field label="Subject" value={message.subject} />
        <Field label="Correlation ID" value={message.correlationId} />
        <Field label="Session ID" value={message.sessionId} />
        <Field label="Content Type" value={message.contentType} />
        <Field label="Enqueued At" value={new Date(message.enqueuedAt).toLocaleString()} />
        <Field label="Delivery Count" value={String(message.deliveryCount)} />
        {message.deadLetterReason && (
          <Field label="DLQ Reason" value={message.deadLetterReason} />
        )}
        {message.deadLetterErrorDescription && (
          <Field label="DLQ Description" value={message.deadLetterErrorDescription} />
        )}
      </div>

      {/* Body */}
      <div className="mb-4">
        <h3 className="mb-2 text-sm font-semibold">Body</h3>
        <pre data-testid="message-detail-body" className="max-h-96 overflow-auto rounded-lg border bg-card p-3 text-xs">
          {tryFormatJson(message.body)}
        </pre>
      </div>

      {/* Application properties */}
      {Object.keys(message.applicationProperties).length > 0 && (
        <div className="mb-4">
          <h3 className="mb-2 text-sm font-semibold">Application Properties</h3>
          <div className="rounded-lg border">
            {Object.entries(message.applicationProperties).map(([key, value]) => (
              <div key={key} className="flex border-b px-3 py-1.5 text-xs last:border-0">
                <span className="w-40 font-medium text-muted-foreground">{key}</span>
                <span className="flex-1">{String(value)}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* System properties (collapsible) */}
      {message.systemProperties && (
        <div>
          <button
            onClick={() => setShowAdvanced(!showAdvanced)}
            className="mb-2 text-sm font-semibold text-muted-foreground hover:text-foreground"
          >
            {showAdvanced ? "▼" : "▶"} System Properties
          </button>
          {showAdvanced && (
            <div className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm">
              <Field label="Expires At" value={message.systemProperties.expiresAt ? new Date(message.systemProperties.expiresAt).toLocaleString() : null} />
              <Field label="Locked Until" value={message.systemProperties.lockedUntil ? new Date(message.systemProperties.lockedUntil).toLocaleString() : null} />
              <Field label="Enqueued Seq #" value={message.systemProperties.enqueuedSequenceNumber} />
              <Field label="Partition Key" value={message.systemProperties.partitionKey} />
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function Field({ label, value }: { label: string; value: string | null | undefined }) {
  return (
    <div>
      <span className="text-xs text-muted-foreground">{label}</span>
      <p className="text-sm">{value || "—"}</p>
    </div>
  );
}
