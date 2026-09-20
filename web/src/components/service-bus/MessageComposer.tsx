import { useState } from "react";
import { Send, Calendar, FileText, Save, RotateCcw } from "lucide-react";
import { useSbSendMessage, useSbScheduleMessage, useSbSaveTemplate } from "@/lib/hooks";
import { useNotification } from "@/components/layout/NotificationSystem";
import type { SbEntityInfo, SbMessage, SbMessageTemplate, ServiceBusNamespace } from "@/lib/types";
import { TemplatePicker } from "./TemplatePicker";
import { EntityPathInput } from "./EntityPathInput";
import { MessageBodyEditor } from "./MessageBodyEditor";
import { sendableEntityPath } from "./resendHelpers";

export type ComposerMode = "compose" | "replay" | "edit" | "schedule";

interface Props {
  mode: ComposerMode;
  nsId: string | null;
  namespaces: ServiceBusNamespace[];
  entity: SbEntityInfo | null;
  sourceMessage?: SbMessage | null;
  /** Pre-fills the composer from a template picked in the template manager. */
  initialTemplate?: SbMessageTemplate | null;
  onClose: () => void;
}

interface PropertyRow {
  key: string;
  value: string;
}

/**
 * Composer content — rendered inside a resizable `SidePanel` by the page, so
 * it has no overlay or header of its own. The Message ID is deliberately
 * visible and editable: replay/edit used to silently reuse the source ID,
 * which broker duplicate detection then dropped without a trace. Every mode
 * now starts from a fresh GUID; the original can be restored explicitly.
 */
export function MessageComposer({ mode, nsId, namespaces, entity, sourceMessage, initialTemplate, onClose }: Props) {
  const sendMutation = useSbSendMessage();
  const scheduleMutation = useSbScheduleMessage();
  const saveTemplateMutation = useSbSaveTemplate();
  const { notify } = useNotification();

  const isSchedule = mode === "schedule";

  const [targetNsId, setTargetNsId] = useState(nsId ?? "");
  const [targetEntityPath, setTargetEntityPath] = useState(entity ? sendableEntityPath(entity) : "");
  const [body, setBody] = useState(sourceMessage?.body ?? initialTemplate?.body ?? "");
  const [subject, setSubject] = useState(sourceMessage?.subject ?? initialTemplate?.subject ?? "");
  const [correlationId, setCorrelationId] = useState(
    sourceMessage?.correlationId ?? initialTemplate?.correlationId ?? "",
  );
  const [sessionId, setSessionId] = useState(sourceMessage?.sessionId ?? "");
  const [contentType, setContentType] = useState(
    sourceMessage?.contentType ?? initialTemplate?.contentType ?? "application/json",
  );
  const [messageId, setMessageId] = useState<string>(() => crypto.randomUUID());
  const [scheduledTime, setScheduledTime] = useState(() => {
    const d = new Date(Date.now() + 5 * 60 * 1000);
    return d.toISOString().slice(0, 16);
  });
  const [properties, setProperties] = useState<PropertyRow[]>(() => {
    const source = sourceMessage?.applicationProperties
      ? Object.entries(sourceMessage.applicationProperties).map(([key, value]) => ({ key, value: String(value) }))
      : Object.entries(initialTemplate?.properties ?? {}).map(([key, value]) => ({ key, value }));
    return source.length > 0 ? source : [{ key: "", value: "" }];
  });
  const [error, setError] = useState<string | null>(null);
  const [showTemplatePicker, setShowTemplatePicker] = useState(false);
  const [showSaveTemplate, setShowSaveTemplate] = useState(false);
  const [templateName, setTemplateName] = useState("");

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

  const onSaveAsTemplate = () => {
    if (!templateName.trim()) return;
    const appProps: Record<string, string> = {};
    for (const prop of properties) {
      if (prop.key.trim()) appProps[prop.key.trim()] = prop.value;
    }
    saveTemplateMutation.mutate(
      {
        id: crypto.randomUUID(),
        name: templateName.trim(),
        body,
        contentType: contentType || null,
        subject: subject || null,
        correlationId: correlationId || null,
        properties: appProps,
        createdAt: new Date().toISOString(),
      },
      {
        onSuccess: () => {
          notify("success", `Template "${templateName.trim()}" saved`);
          setShowSaveTemplate(false);
          setTemplateName("");
        },
      },
    );
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

  return (
    <div className="flex h-full flex-col" data-testid="message-composer">
      {/* Scrollable content — the footer stays pinned so Send is always visible. */}
      <div className="flex-1 space-y-3 overflow-auto p-4">
        {error && (
          <div
            className="rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-xs text-destructive"
            data-testid="composer-error"
          >
            {error}
          </div>
        )}

        {/* Template actions */}
        <div className="flex items-center justify-end gap-3">
          <button
            type="button"
            onClick={() => setShowTemplatePicker(true)}
            className="flex items-center gap-1 text-xs text-primary hover:underline"
            data-testid="composer-load-template"
          >
            <FileText className="h-3 w-3" />
            Load Template
          </button>
          <button
            type="button"
            onClick={() => setShowSaveTemplate((v) => !v)}
            className="flex items-center gap-1 text-xs text-primary hover:underline"
            data-testid="composer-save-template"
            title="Save the current fields as a reusable template"
          >
            <Save className="h-3 w-3" />
            Save as Template
          </button>
        </div>

        {showSaveTemplate && (
          <div className="flex items-center gap-2 rounded-md border bg-muted/20 p-2" data-testid="composer-save-template-row">
            <input
              type="text"
              data-testid="composer-template-name"
              value={templateName}
              onChange={(e) => setTemplateName(e.target.value)}
              placeholder="Template name..."
              className="flex-1 rounded-md border bg-background px-2 py-1.5 text-sm"
              autoFocus
              onKeyDown={(e) => {
                if (e.key === "Enter") onSaveAsTemplate();
              }}
            />
            <button
              type="button"
              data-testid="composer-template-save"
              onClick={onSaveAsTemplate}
              disabled={!templateName.trim() || saveTemplateMutation.isPending}
              title={saveTemplateMutation.isPending ? "Saving…" : !templateName.trim() ? "Name the template first" : undefined}
              className="rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
            >
              Save
            </button>
            <button
              type="button"
              data-testid="composer-template-cancel"
              onClick={() => {
                setShowSaveTemplate(false);
                setTemplateName("");
              }}
              className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
            >
              Cancel
            </button>
          </div>
        )}

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
            <EntityPathInput
              nsId={targetNsId || null}
              value={targetEntityPath}
              onChange={setTargetEntityPath}
              testId="composer-target-entity"
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

        {/* Message ID — visible by design: replay/edit previously reused the
            source ID and duplicate detection silently dropped the resend. */}
        <div>
          <label className="mb-1 block text-xs font-medium text-muted-foreground">Message ID</label>
          <div className="flex items-center gap-1.5">
            <input
              type="text"
              data-testid="composer-message-id"
              value={messageId}
              onChange={(e) => setMessageId(e.target.value)}
              spellCheck={false}
              className="flex-1 rounded-md border bg-background px-2 py-1.5 font-mono text-xs"
            />
            <button
              type="button"
              data-testid="composer-regenerate-id"
              onClick={() => setMessageId(crypto.randomUUID())}
              title="Generate a new Message ID"
              className="rounded-md border px-2 py-1.5 text-xs hover:bg-accent"
            >
              <RotateCcw className="h-3 w-3" />
            </button>
            {sourceMessage && messageId !== sourceMessage.messageId && (
              <button
                type="button"
                data-testid="composer-restore-id"
                onClick={() => setMessageId(sourceMessage.messageId)}
                title={`Reuse the original Message ID (${sourceMessage.messageId}) — may be dropped by duplicate detection`}
                className="rounded-md border px-2 py-1.5 text-xs text-muted-foreground hover:bg-accent hover:text-foreground"
              >
                Restore original
              </button>
            )}
          </div>
        </div>

        {/* Body */}
        <div className="flex min-h-0 flex-1 flex-col">
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
          <MessageBodyEditor value={body} contentType={contentType} onChange={setBody} mirrorTestId="composer-body" />
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
          title={isPending ? "Sending…" : undefined}
          className="flex items-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
          data-testid="composer-send"
        >
          {isSchedule ? (
            <>
              <Calendar className="h-3 w-3" /> Schedule
            </>
          ) : (
            <>
              <Send className="h-3 w-3" /> Send
            </>
          )}
        </button>
      </div>

      {/* Template picker */}
      {showTemplatePicker && (
        <TemplatePicker onSelect={loadTemplate} onClose={() => setShowTemplatePicker(false)} />
      )}
    </div>
  );
}
