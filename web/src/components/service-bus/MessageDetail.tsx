import { useState } from "react";
import { Copy, Check, AlertTriangle } from "lucide-react";
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

type DetailTab = "body" | "properties" | "system" | "dlq";

export function MessageDetail({ message, nsId, entity, viewMode }: Props) {
  const completeMutation = useSbCompleteMessages();
  const completeDlqMutation = useSbCompleteDlq();
  const resubmitMutation = useSbResubmitDlq();
  const purgeMutation = useSbPurgeMessages();
  const [activeTab, setActiveTab] = useState<DetailTab>("body");
  const [copyFeedback, setCopyFeedback] = useState<string | null>(null);
  const [showPurgeConfirm, setShowPurgeConfirm] = useState(false);

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

  const copyToClipboard = async (text: string, feedbackKey: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setCopyFeedback(feedbackKey);
      setTimeout(() => setCopyFeedback(null), 2000);
    } catch {
      // Fallback for environments without clipboard API
    }
  };

  const copyBody = () => copyToClipboard(message.body, "body");

  const copyFullMessage = () => {
    const full = {
      messageId: message.messageId,
      correlationId: message.correlationId,
      subject: message.subject,
      contentType: message.contentType,
      body: message.body,
      applicationProperties: message.applicationProperties,
      systemProperties: message.systemProperties,
      enqueuedAt: message.enqueuedAt,
      deliveryCount: message.deliveryCount,
      sequenceNumber: message.sequenceNumber,
      sessionId: message.sessionId,
      deadLetterReason: message.deadLetterReason,
      deadLetterErrorDescription: message.deadLetterErrorDescription,
    };
    copyToClipboard(JSON.stringify(full, null, 2), "full");
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
    purgeMutation.mutate({
      nsId,
      entityPath: entity.entityPath,
      deadLetter: viewMode === "dlq",
    });
    setShowPurgeConfirm(false);
  };

  const tabs: { id: DetailTab; label: string; visible: boolean }[] = [
    { id: "body", label: "Body", visible: true },
    { id: "properties", label: "Properties", visible: true },
    { id: "system", label: "System", visible: true },
    { id: "dlq", label: "DLQ Info", visible: !!message.deadLetterReason },
  ];

  const visibleTabs = tabs.filter((t) => t.visible);

  return (
    <div className="flex h-full flex-col" data-testid="message-detail">
      {/* Header */}
      <div className="border-b px-4 py-3">
        <div className="flex items-start justify-between">
          <div className="min-w-0 flex-1">
            <h2 className="truncate text-lg font-semibold" data-testid="message-detail-subject">
              {message.subject || message.messageId}
            </h2>
            <p className="mt-0.5 text-xs text-muted-foreground" data-testid="message-detail-meta">
              Message ID: {message.messageId} · Seq: #{message.sequenceNumber}
            </p>
          </div>
          <div className="flex shrink-0 gap-2">
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
              onClick={() => setShowPurgeConfirm(true)}
              disabled={purgeMutation.isPending}
              className="rounded-md border border-destructive px-3 py-1.5 text-xs text-destructive hover:bg-destructive/10 disabled:opacity-50"
            >
              Purge All
            </button>
          </div>
        </div>

        {/* Action buttons row */}
        <div className="mt-2 flex items-center gap-2">
          <button
            data-testid="message-copy-body"
            onClick={copyBody}
            className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent"
            title="Copy message body to clipboard"
          >
            {copyFeedback === "body" ? (
              <><Check className="h-3 w-3" /> Copied!</>
            ) : (
              <><Copy className="h-3 w-3" /> Copy Body</>
            )}
          </button>
          <button
            data-testid="message-copy-full"
            onClick={copyFullMessage}
            className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent"
            title="Copy full message (all properties + body) as JSON"
          >
            {copyFeedback === "full" ? (
              <><Check className="h-3 w-3" /> Copied!</>
            ) : (
              <><Copy className="h-3 w-3" /> Copy Full Message</>
            )}
          </button>
        </div>
      </div>

      {/* Purge confirmation dialog */}
      {showPurgeConfirm && (
        <div className="flex items-center gap-3 border-b bg-destructive/10 px-4 py-3" data-testid="purge-confirm">
          <AlertTriangle className="h-5 w-5 shrink-0 text-destructive" />
          <span className="flex-1 text-sm">
            Purge all {viewMode === "dlq" ? "dead-lettered" : "active"} messages from <strong>{entity?.entityPath}</strong>?
            This cannot be undone.
          </span>
          <button
            data-testid="purge-confirm-yes"
            onClick={onPurge}
            disabled={purgeMutation.isPending}
            className="rounded-md bg-destructive px-3 py-1.5 text-xs text-destructive-foreground hover:opacity-90 disabled:opacity-50"
          >
            Purge
          </button>
          <button
            data-testid="purge-confirm-cancel"
            onClick={() => setShowPurgeConfirm(false)}
            className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
          >
            Cancel
          </button>
        </div>
      )}

      {/* Tabs */}
      <div className="flex border-b">
        {visibleTabs.map((tab) => (
          <button
            key={tab.id}
            data-testid={`detail-tab-${tab.id}`}
            onClick={() => setActiveTab(tab.id)}
            className={`px-4 py-2 text-sm font-medium ${
              activeTab === tab.id
                ? "border-b-2 border-primary text-foreground"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div className="flex-1 overflow-auto p-4">
        {activeTab === "body" && (
          <div data-testid="detail-tab-content-body">
            <pre data-testid="message-detail-body" className="max-h-[60vh] overflow-auto rounded-lg border bg-card p-3 text-xs">
              {tryFormatJson(message.body)}
            </pre>
          </div>
        )}

        {activeTab === "properties" && (
          <div data-testid="detail-tab-content-properties">
            {Object.keys(message.applicationProperties).length > 0 ? (
              <div className="rounded-lg border">
                {Object.entries(message.applicationProperties).map(([key, value]) => (
                  <div key={key} className="flex border-b px-3 py-1.5 text-xs last:border-0">
                    <span className="w-48 font-medium text-muted-foreground">{key}</span>
                    <span className="flex-1 break-all">{String(value)}</span>
                  </div>
                ))}
              </div>
            ) : (
              <span className="text-sm text-muted-foreground">No application properties</span>
            )}
          </div>
        )}

        {activeTab === "system" && (
          <div data-testid="detail-tab-content-system" className="grid grid-cols-2 gap-x-6 gap-y-3 text-sm">
            <Field label="Message ID" value={message.messageId} />
            <Field label="Correlation ID" value={message.correlationId} />
            <Field label="Subject" value={message.subject} />
            <Field label="Content Type" value={message.contentType} />
            <Field label="Delivery Count" value={String(message.deliveryCount)} />
            <Field label="Enqueued At" value={new Date(message.enqueuedAt).toLocaleString()} />
            <Field label="Sequence Number" value={message.sequenceNumber != null ? String(message.sequenceNumber) : null} />
            <Field label="Session ID" value={message.sessionId} />
            <Field label="Partition Key" value={message.systemProperties?.partitionKey ?? null} />
            <Field label="Expires At" value={message.systemProperties?.expiresAt ? new Date(message.systemProperties.expiresAt).toLocaleString() : null} />
            {message.systemProperties?.lockedUntil && (
              <Field label="Locked Until" value={new Date(message.systemProperties.lockedUntil).toLocaleString()} />
            )}
            <Field label="Enqueued Seq #" value={message.systemProperties?.enqueuedSequenceNumber ?? null} />
          </div>
        )}

        {activeTab === "dlq" && message.deadLetterReason && (
          <div data-testid="detail-tab-content-dlq" className="space-y-3">
            <div className="rounded-lg border border-destructive/30 bg-destructive/5 p-4">
              <div className="flex items-center gap-2">
                <AlertTriangle className="h-4 w-4 text-destructive" />
                <span className="text-sm font-semibold text-destructive">Dead-Letter Reason</span>
              </div>
              <p className="mt-1 text-sm">{message.deadLetterReason}</p>
              {message.deadLetterErrorDescription && (
                <p className="mt-2 text-xs text-muted-foreground">{message.deadLetterErrorDescription}</p>
              )}
            </div>
          </div>
        )}
      </div>
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
