import { useState } from "react";
import { X, Send, Calendar, RotateCcw, FileText } from "lucide-react";
import { useSbSendMessage, useSbScheduleMessage } from "@/lib/hooks";
import type { SbEntityInfo, SbMessage, SbMessageTemplate, ServiceBusNamespace } from "@/lib/types";
import { TemplatePicker } from "./TemplatePicker";

export type ComposerMode = "compose" | "replay" | "edit" | "schedule";

interface Props {
  mode: ComposerMode;
  nsId: string | null;
  namespaces: ServiceBusNamespace[];
  entity: SbEntityInfo | null;
  sourceMessage?: SbMessage | null;
  onClose: () => void;
}

interface PropertyRow {
  key: string;
  value: string;
}

export function MessageComposer({ mode, nsId, namespaces, entity, sourceMessage, onClose }: Props) {
  const sendMutation = useSbSendMessage();
  const scheduleMutation = useSbScheduleMessage();

  const isSchedule = mode === "schedule";
  const isReplay = mode === "replay" || mode === "edit";

  const [targetNsId, setTargetNsId] = useState(nsId ?? "");
  const [targetEntityPath, setTargetEntityPath] = useState(entity?.entityPath ?? "");
  const [body, setBody] = useState(sourceMessage?.body ?? "");
  const [subject, setSubject] = useState(sourceMessage?.subject ?? "");
  const [correlationId, setCorrelationId] = useState(sourceMessage?.correlationId ?? "");
  const [sessionId, setSessionId] = useState(sourceMessage?.sessionId ?? "");
  const [contentType, setContentType] = useState(sourceMessage?.contentType ?? "application/json");
  const [messageId] = useState(
    mode === "compose" ? crypto.randomUUID() : sourceMessage?.messageId ?? crypto.randomUUID(),
  );
  const [scheduledTime, setScheduledTime] = useState(() => {
    const d = new Date(Date.now() + 5 * 60 * 1000);
    return d.toISOString().slice(0, 16);
  });
  const [properties, setProperties] = useState<PropertyRow[]>(() => {
    if (sourceMessage?.applicationProperties) {
      return Object.entries(sourceMessage.applicationProperties).map(([key, value]) => ({
        key,
        value: String(value),
      }));
    }
    return [{ key: "", value: "" }];
  });
  const [error, setError] = useState<string | null>(null);
  const [showTemplatePicker, setShowTemplatePicker] = useState(false);

  const updateProperty = (index: number, field: "key" | "value", value: string) => {
    setProperties((prev) => prev.map((row, i) => (i === index ? { ...row, [field]: value } : row)));
  };

  const addProperty = () => {
    setProperties((prev) => [...prev, { key: "", value: "" }]);
  };

  const removeProperty = (index: number) => {
    setProperties((prev) => prev.filter((_, i) => i !== index));
  };

  const formatBody = () => {
    try {
      const parsed = JSON.parse(body);
      setBody(JSON.stringify(parsed, null, 2));
    } catch {
      // Not valid JSON, leave as-is
    }
  };

  const loadTemplate = (template: SbMessageTemplate) => {
    setBody(template.body);
    setSubject(template.subject ?? "");
    setCorrelationId(template.correlationId ?? "");
    setContentType(template.contentType ?? "application/json");
    const props = Object.entries(template.properties ?? {});
    setProperties(props.length > 0 ? props.map(([key, value]) => ({ key, value })) : [{ key: "", value: "" }]);
    setShowTemplatePicker(false);
  };

  const buildMessage = (): SbMessage => {
    const appProps: Record<string, unknown> = {};
    for (const prop of properties) {
      if (prop.key.trim()) {
        appProps[prop.key.trim()] = prop.value;
      }
    }
    return {
      messageId,
      correlationId: correlationId || null,
      subject: subject || null,
      contentType: contentType || null,
      body,
      applicationProperties: appProps,
      systemProperties: null,
      deadLetterReason: null,
      deadLetterErrorDescription: null,
      enqueuedAt: new Date().toISOString(),
      deliveryCount: 0,
      lockToken: null,
      sequenceNumber: null,
      sessionId: sessionId || null,
    };
  };

  const onSend = async () => {
    setError(null);
    if (!targetNsId) {
      setError("Select a target namespace");
      return;
    }
    if (!targetEntityPath) {
      setError("Select a target entity");
      return;
    }
    if (!body.trim()) {
      setError("Message body cannot be empty");
      return;
    }

    const message = buildMessage();

    try {
      if (isSchedule) {
        const scheduledEnqueueTime = new Date(scheduledTime).toISOString();
        await scheduleMutation.mutateAsync({
          nsId: targetNsId,
          entityPath: targetEntityPath,
          message,
          scheduledEnqueueTime,
        });
      } else {
        await sendMutation.mutateAsync({
          nsId: targetNsId,
          entityPath: targetEntityPath,
          message,
        });
      }
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to send message");
    }
  };

  const isPending = sendMutation.isPending || scheduleMutation.isPending;
  const title = isSchedule ? "Schedule Message" : isReplay ? "Replay Message" : "Compose Message";

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" data-testid="composer-overlay">
      <div className="flex max-h-[90vh] w-[600px] flex-col rounded-lg border bg-card shadow-lg" data-testid="message-composer">
        {/* Header */}
        <div className="flex items-center justify-between border-b px-4 py-3">
          <div className="flex items-center gap-2">
            {isSchedule ? (
              <Calendar className="h-4 w-4 text-primary" />
            ) : isReplay ? (
              <RotateCcw className="h-4 w-4 text-primary" />
            ) : (
              <Send className="h-4 w-4 text-primary" />
            )}
            <h2 className="text-sm font-semibold" data-testid="composer-title">{title}</h2>
          </div>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground" data-testid="composer-close">
            <X className="h-4 w-4" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-auto p-4 space-y-3">
          {error && (
            <div className="rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-xs text-destructive" data-testid="composer-error">
              {error}
            </div>
          )}

          {/* Load template */}
          <div className="flex justify-end">
            <button
              type="button"
              onClick={() => setShowTemplatePicker(true)}
              className="flex items-center gap-1 text-xs text-primary hover:underline"
              data-testid="composer-load-template"
            >
              <FileText className="h-3 w-3" />
              Load Template
            </button>
          </div>

          {/* Target selectors */}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1 block text-xs font-medium text-muted-foreground">Target Namespace</label>
              <select
                data-testid="composer-target-ns"
                value={targetNsId}
                onChange={(e) => setTargetNsId(e.target.value)}
                className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
              >
                <option value="">Select namespace...</option>
                {namespaces.map((ns) => (
                  <option key={ns.id} value={ns.id}>
                    {ns.alias || ns.fullyQualifiedNamespace}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-muted-foreground">Target Entity</label>
              <input
                type="text"
                data-testid="composer-target-entity"
                value={targetEntityPath}
                onChange={(e) => setTargetEntityPath(e.target.value)}
                placeholder="queue or topic name"
                className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
              />
            </div>
          </div>

          {/* Schedule time */}
          {isSchedule && (
            <div>
              <label className="mb-1 block text-xs font-medium text-muted-foreground">Scheduled Enqueue Time</label>
              <input
                type="datetime-local"
                data-testid="composer-scheduled-time"
                value={scheduledTime}
                onChange={(e) => setScheduledTime(e.target.value)}
                className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
              />
            </div>
          )}

          {/* Message fields */}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1 block text-xs font-medium text-muted-foreground">Subject</label>
              <input
                type="text"
                data-testid="composer-subject"
                value={subject}
                onChange={(e) => setSubject(e.target.value)}
                placeholder="Message subject"
                className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
              />
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-muted-foreground">Correlation ID</label>
              <input
                type="text"
                data-testid="composer-correlation-id"
                value={correlationId}
                onChange={(e) => setCorrelationId(e.target.value)}
                placeholder="Correlation ID"
                className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
              />
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-muted-foreground">Session ID</label>
              <input
                type="text"
                data-testid="composer-session-id"
                value={sessionId}
                onChange={(e) => setSessionId(e.target.value)}
                placeholder="Session ID (optional)"
                className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
              />
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-muted-foreground">Content Type</label>
              <input
                type="text"
                data-testid="composer-content-type"
                value={contentType}
                onChange={(e) => setContentType(e.target.value)}
                placeholder="application/json"
                className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
              />
            </div>
          </div>

          {/* Body */}
          <div>
            <div className="mb-1 flex items-center justify-between">
              <label className="text-xs font-medium text-muted-foreground">Body</label>
              <button
                type="button"
                onClick={formatBody}
                data-testid="composer-format-json"
                className="text-xs text-primary hover:underline"
              >
                Format JSON
              </button>
            </div>
            <textarea
              data-testid="composer-body"
              value={body}
              onChange={(e) => setBody(e.target.value)}
              rows={8}
              placeholder='{"key": "value"}'
              className="w-full rounded-md border bg-background px-2 py-1.5 font-mono text-xs"
            />
          </div>

          {/* Application properties */}
          <div>
            <div className="mb-1 flex items-center justify-between">
              <label className="text-xs font-medium text-muted-foreground">Application Properties</label>
              <button
                type="button"
                onClick={addProperty}
                data-testid="composer-add-property"
                className="text-xs text-primary hover:underline"
              >
                + Add Property
              </button>
            </div>
            <div className="space-y-1">
              {properties.map((prop, i) => (
                <div key={i} className="flex items-center gap-1.5" data-testid={`composer-property-row-${i}`}>
                  <input
                    type="text"
                    value={prop.key}
                    onChange={(e) => updateProperty(i, "key", e.target.value)}
                    placeholder="Key"
                    className="w-32 rounded border bg-background px-2 py-1 text-xs"
                    data-testid={`composer-property-key-${i}`}
                  />
                  <input
                    type="text"
                    value={prop.value}
                    onChange={(e) => updateProperty(i, "value", e.target.value)}
                    placeholder="Value"
                    className="flex-1 rounded border bg-background px-2 py-1 text-xs"
                    data-testid={`composer-property-value-${i}`}
                  />
                  <button
                    type="button"
                    onClick={() => removeProperty(i)}
                    className="rounded px-1.5 py-0.5 text-xs text-muted-foreground hover:bg-accent"
                    data-testid={`composer-property-remove-${i}`}
                  >
                    ✕
                  </button>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-2 border-t px-4 py-3">
          <button
            onClick={onClose}
            className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
            data-testid="composer-cancel"
          >
            Cancel
          </button>
          <button
            onClick={onSend}
            disabled={isPending}
            className="flex items-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
            data-testid="composer-send"
          >
            {isSchedule ? (
              <><Calendar className="h-3 w-3" /> Schedule</>
            ) : (
              <><Send className="h-3 w-3" /> Send</>
            )}
          </button>
        </div>
      </div>

      {/* Template picker */}
      {showTemplatePicker && (
        <TemplatePicker
          onSelect={loadTemplate}
          onClose={() => setShowTemplatePicker(false)}
        />
      )}
    </div>
  );
}
