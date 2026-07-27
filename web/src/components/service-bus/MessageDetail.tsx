import { useState, useMemo } from "react";
import { Copy, Check, AlertTriangle, Save, Pencil, RotateCcw, Clock, Search, X } from "lucide-react";
import {
  useSbCompleteMessages,
  useSbCompleteDlq,
  useSbResubmitDlq,
  useSbPurgeMessages,
  useSbSaveTemplate,
} from "@/lib/hooks";
import type { SbEntityInfo, SbMessage, SbMessageTemplate } from "@/lib/types";

interface Props {
  message: SbMessage | null;
  nsId: string | null;
  entity: SbEntityInfo | null;
  viewMode: "active" | "dlq";
  onClose?: () => void;
  onEditResubmit?: (message: SbMessage) => void;
  onReplay?: (message: SbMessage) => void;
  onSchedule?: (message: SbMessage) => void;
}

type DetailTab = "body" | "properties" | "system" | "dlq";

export function MessageDetail({ message, nsId, entity, viewMode, onClose, onEditResubmit, onReplay, onSchedule }: Props) {
  const completeMutation = useSbCompleteMessages();
  const completeDlqMutation = useSbCompleteDlq();
  const resubmitMutation = useSbResubmitDlq();
  const purgeMutation = useSbPurgeMessages();
  const [activeTab, setActiveTab] = useState<DetailTab>("body");
  const [copyFeedback, setCopyFeedback] = useState<string | null>(null);
  const [showPurgeConfirm, setShowPurgeConfirm] = useState(false);
  const [showSaveTemplate, setShowSaveTemplate] = useState(false);
  const [templateName, setTemplateName] = useState("");
  const [propFilter, setPropFilter] = useState("");
  const [copyPropKey, setCopyPropKey] = useState<string | null>(null);
  const saveTemplateMutation = useSbSaveTemplate();

  const tryFormatJson = (body: string): string => {
    try {
      return JSON.stringify(JSON.parse(body), null, 2);
    } catch {
      return body;
    }
  };

  const detectFormat = (body: string): "json" | "xml" | "text" => {
    const trimmed = body.trim();
    if (trimmed.startsWith("{") || trimmed.startsWith("[")) return "json";
    if (trimmed.startsWith("<")) return "xml";
    return "text";
  };

  const formatBytes = (bytes: number): string => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const bodyFormat = message ? detectFormat(message.body) : "text";
  const bodySize = message ? new TextEncoder().encode(message.body).length : 0;
  const bodyLineCount = message ? message.body.split("\n").length : 0;

  const filteredProps = useMemo(() => {
    if (!message) return [];
    const entries = Object.entries(message.applicationProperties);
    if (!propFilter.trim()) return entries;
    const q = propFilter.toLowerCase();
    return entries.filter(([k, v]) => k.toLowerCase().includes(q) || String(v).toLowerCase().includes(q));
  }, [message, propFilter]);

  if (!message) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground" data-testid="message-detail-empty">
        Select a message to view details
      </div>
    );
  }

  const copyProp = async (key: string, value: unknown) => {
    try {
      await navigator.clipboard.writeText(String(value));
      setCopyPropKey(key);
      setTimeout(() => setCopyPropKey(null), 2000);
    } catch {}
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

  const onSaveAsTemplate = () => {
    if (!templateName.trim()) return;
    const template: SbMessageTemplate = {
      id: crypto.randomUUID(),
      name: templateName.trim(),
      body: message.body,
      contentType: message.contentType,
      subject: message.subject,
      correlationId: message.correlationId,
      properties: Object.fromEntries(
        Object.entries(message.applicationProperties).map(([k, v]) => [k, String(v)]),
      ),
      createdAt: new Date().toISOString(),
    };
    saveTemplateMutation.mutate(template);
    setShowSaveTemplate(false);
    setTemplateName("");
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
            {onClose && (
              <button
                data-testid="message-detail-close"
                onClick={onClose}
                className="rounded-md border px-2 py-1.5 text-xs text-muted-foreground hover:bg-accent hover:text-foreground"
                title="Close message details"
              >
                <X className="h-3.5 w-3.5" />
              </button>
            )}
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
          <button
            data-testid="message-save-template"
            onClick={() => setShowSaveTemplate(true)}
            className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent"
            title="Save this message as a reusable template"
          >
            <Save className="h-3 w-3" /> Save as Template
          </button>
          {onEditResubmit && (
            <button
              data-testid="message-edit-resubmit"
              onClick={() => onEditResubmit(message)}
              className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent"
              title="Edit and resubmit this message"
            >
              <Pencil className="h-3 w-3" /> Edit & Resubmit
            </button>
          )}
          {onReplay && (
            <button
              data-testid="message-replay"
              onClick={() => onReplay(message)}
              className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent"
              title="Replay this message"
            >
              <RotateCcw className="h-3 w-3" /> Replay
            </button>
          )}
          {onSchedule && (
            <button
              data-testid="message-schedule"
              onClick={() => onSchedule(message)}
              className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent"
              title="Schedule this message for later delivery"
            >
              <Clock className="h-3 w-3" /> Schedule
            </button>
          )}
        </div>
      </div>

      {/* Save as template dialog */}
      {showSaveTemplate && (
        <div className="flex items-center gap-3 border-b bg-primary/5 px-4 py-3" data-testid="save-template-dialog">
          <Save className="h-5 w-5 shrink-0 text-primary" />
          <input
            type="text"
            data-testid="template-name-input"
            value={templateName}
            onChange={(e) => setTemplateName(e.target.value)}
            placeholder="Template name..."
            className="flex-1 rounded-md border bg-background px-3 py-1.5 text-sm"
            autoFocus
            onKeyDown={(e) => { if (e.key === "Enter") onSaveAsTemplate(); }}
          />
          <button
            data-testid="template-save-confirm"
            onClick={onSaveAsTemplate}
            disabled={!templateName.trim() || saveTemplateMutation.isPending}
            className="rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
          >
            Save
          </button>
          <button
            data-testid="template-save-cancel"
            onClick={() => { setShowSaveTemplate(false); setTemplateName(""); }}
            className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
          >
            Cancel
          </button>
        </div>
      )}

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
            <div className="mb-2 flex items-center gap-3 text-xs text-muted-foreground">
              <span data-testid="body-format">Format: {bodyFormat.toUpperCase()}</span>
              <span data-testid="body-size">Size: {formatBytes(bodySize)}</span>
              <span data-testid="body-lines">Lines: {bodyLineCount}</span>
              <button
                onClick={copyBody}
                className="flex items-center gap-1 rounded border px-2 py-0.5 hover:bg-accent"
                data-testid="body-copy-btn"
              >
                {copyFeedback === "body" ? <><Check className="h-3 w-3" /> Copied</> : <><Copy className="h-3 w-3" /> Copy</>}
              </button>
            </div>
            <pre data-testid="message-detail-body" className="max-h-[60vh] overflow-auto rounded-lg border bg-card p-3 text-xs">
              {bodyFormat === "json" ? (
                <JsonHighlight text={tryFormatJson(message.body)} />
              ) : (
                <span className="whitespace-pre-wrap break-all">{message.body}</span>
              )}
            </pre>
          </div>
        )}

        {activeTab === "properties" && (
          <div data-testid="detail-tab-content-properties">
            {Object.keys(message.applicationProperties).length > 0 ? (
              <>
                <div className="relative mb-2">
                  <Search className="absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
                  <input
                    type="text"
                    data-testid="prop-filter-input"
                    value={propFilter}
                    onChange={(e) => setPropFilter(e.target.value)}
                    placeholder="Filter properties..."
                    className="w-full rounded-md border bg-card py-1.5 pl-8 pr-7 text-xs"
                  />
                  {propFilter && (
                    <button
                      onClick={() => setPropFilter("")}
                      className="absolute right-1.5 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                    >
                      <X className="h-3.5 w-3.5" />
                    </button>
                  )}
                </div>
                <div className="rounded-lg border">
                  {filteredProps.map(([key, value]) => (
                    <div key={key} className="group flex items-start border-b px-3 py-1.5 text-xs last:border-0">
                      <span className="w-48 shrink-0 font-medium text-muted-foreground">{key}</span>
                      <span className="flex-1 break-all">{String(value)}</span>
                      <button
                        onClick={() => copyProp(key, value)}
                        className="ml-2 shrink-0 text-muted-foreground opacity-0 transition-opacity hover:text-foreground group-hover:opacity-100"
                        data-testid={`prop-copy-${key}`}
                        title="Copy value"
                      >
                        {copyPropKey === key ? <Check className="h-3 w-3" /> : <Copy className="h-3 w-3" />}
                      </button>
                    </div>
                  ))}
                  {filteredProps.length === 0 && (
                    <div className="px-3 py-2 text-xs text-muted-foreground">No properties match filter</div>
                  )}
                </div>
              </>
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

function JsonHighlight({ text }: { text: string }) {
  const tokens = useMemo(() => {
    const parts: { text: string; cls: string }[] = [];
    const regex = /("(\\u[a-zA-Z0-9]{4}|\\[^u]|[^\\"])*"(\s*:)?|\b(true|false|null)\b|-?\d+(?:\.\d*)?(?:[eE][+\-]?\d+)?)/g;
    let lastIndex = 0;
    let match: RegExpExecArray | null;
    while ((match = regex.exec(text)) !== null) {
      if (match.index > lastIndex) {
        parts.push({ text: text.slice(lastIndex, match.index), cls: "" });
      }
      let cls = "text-blue-400";
      if (/^"/.test(match[0])) {
        if (/:$/.test(match[0])) {
          cls = "text-purple-400";
        } else {
          cls = "text-green-400";
        }
      } else if (/true|false/.test(match[0])) {
        cls = "text-orange-400";
      } else if (/null/.test(match[0])) {
        cls = "text-muted-foreground";
      } else if (/-?\d/.test(match[0])) {
        cls = "text-cyan-400";
      }
      parts.push({ text: match[0], cls });
      lastIndex = match.index + match[0].length;
    }
    if (lastIndex < text.length) {
      parts.push({ text: text.slice(lastIndex), cls: "" });
    }
    return parts;
  }, [text]);

  return (
    <span>
      {tokens.map((tok, i) => (
        <span key={i} className={tok.cls}>{tok.text}</span>
      ))}
    </span>
  );
}
