import { useState } from "react";
import { X, Upload, Send } from "lucide-react";
import { useSbBatchSend } from "@/lib/hooks";
import type { SbEntityInfo, SbMessage, ServiceBusNamespace } from "@/lib/types";

interface Props {
  nsId: string | null;
  namespaces: ServiceBusNamespace[];
  entity: SbEntityInfo | null;
  onClose: () => void;
}

export function BatchSendPanel({ nsId, namespaces, entity, onClose }: Props) {
  const batchSendMutation = useSbBatchSend();
  const [targetNsId, setTargetNsId] = useState(nsId ?? "");
  const [targetEntityPath, setTargetEntityPath] = useState(entity?.entityPath ?? "");
  const [input, setInput] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [preview, setPreview] = useState<SbMessage[] | null>(null);

  const parseCsv = (text: string): SbMessage[] => {
    const lines = text.trim().split("\n");
    if (lines.length < 2) return [];

    const header = lines[0].split(",").map((h) => h.trim());
    const messages: SbMessage[] = [];

    for (let i = 1; i < lines.length; i++) {
      const values = lines[i].split(",").map((v) => v.trim());
      const row: Record<string, string> = {};
      header.forEach((h, j) => {
        row[h] = values[j] ?? "";
      });

      messages.push({
        messageId: crypto.randomUUID(),
        body: row.body ?? row.Body ?? "",
        subject: row.subject ?? row.Subject ?? null,
        correlationId: row.correlationId ?? row.CorrelationId ?? null,
        contentType: row.contentType ?? row.ContentType ?? "application/json",
        applicationProperties: {},
        systemProperties: null,
        deadLetterReason: null,
        deadLetterErrorDescription: null,
        enqueuedAt: new Date().toISOString(),
        deliveryCount: 0,
        lockToken: null,
        sequenceNumber: null,
        sessionId: row.sessionId ?? row.SessionId ?? null,
      });
    }

    return messages;
  };

  const parseJson = (text: string): SbMessage[] => {
    const parsed = JSON.parse(text);
    if (!Array.isArray(parsed)) {
      return [parsed];
    }
    return parsed.map((item: Partial<SbMessage>) => ({
      messageId: item.messageId ?? crypto.randomUUID(),
      body: item.body ?? "",
      subject: item.subject ?? null,
      correlationId: item.correlationId ?? null,
      contentType: item.contentType ?? "application/json",
      applicationProperties: item.applicationProperties ?? {},
      systemProperties: null,
      deadLetterReason: null,
      deadLetterErrorDescription: null,
      enqueuedAt: new Date().toISOString(),
      deliveryCount: 0,
      lockToken: null,
      sequenceNumber: null,
      sessionId: item.sessionId ?? null,
    }));
  };

  const onPreview = () => {
    setError(null);
    if (!input.trim()) {
      setError("Paste CSV or JSON data first");
      return;
    }
    try {
      const trimmed = input.trim();
      const messages = trimmed.startsWith("[") || trimmed.startsWith("{")
        ? parseJson(trimmed)
        : parseCsv(trimmed);
      if (messages.length === 0) {
        setError("No messages found in input");
        return;
      }
      setPreview(messages);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to parse input");
    }
  };

  const onSend = async () => {
    setError(null);
    if (!targetNsId) {
      setError("Select a target namespace");
      return;
    }
    if (!targetEntityPath) {
      setError("Enter a target entity");
      return;
    }
    if (!preview || preview.length === 0) {
      setError("Preview messages first");
      return;
    }

    try {
      await batchSendMutation.mutateAsync({
        nsId: targetNsId,
        entityPath: targetEntityPath,
        messages: preview,
      });
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to batch send");
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" data-testid="batch-overlay">
      <div className="flex max-h-[90vh] w-[700px] flex-col rounded-lg border bg-card shadow-lg" data-testid="batch-send-panel">
        {/* Header */}
        <div className="flex items-center justify-between border-b px-4 py-3">
          <div className="flex items-center gap-2">
            <Upload className="h-4 w-4 text-primary" />
            <h2 className="text-sm font-semibold">Batch Send Messages</h2>
          </div>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground" data-testid="batch-close">
            <X className="h-4 w-4" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-auto p-4 space-y-3">
          {error && (
            <div className="rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-xs text-destructive" data-testid="batch-error">
              {error}
            </div>
          )}

          {/* Target selectors */}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1 block text-xs font-medium text-muted-foreground">Target Namespace</label>
              <select
                data-testid="batch-target-ns"
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
                data-testid="batch-target-entity"
                value={targetEntityPath}
                onChange={(e) => setTargetEntityPath(e.target.value)}
                placeholder="queue or topic name"
                className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
              />
            </div>
          </div>

          {/* Input */}
          <div>
            <label className="mb-1 block text-xs font-medium text-muted-foreground">
              Paste CSV (header: body,subject,correlationId) or JSON array of messages
            </label>
            <textarea
              data-testid="batch-input"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              rows={6}
              placeholder={"body,subject,correlationId\n{\"key\":\"value\"},Order Update,corr-123\n\nOr JSON:\n[{\"body\": \"...\", \"subject\": \"...\"}]"}
              className="w-full rounded-md border bg-background px-2 py-1.5 font-mono text-xs"
            />
          </div>

          {/* Preview */}
          {preview && (
            <div data-testid="batch-preview">
              <div className="mb-1 text-xs font-medium text-muted-foreground">
                Preview: {preview.length} message{preview.length !== 1 ? "s" : ""}
              </div>
              <div className="max-h-40 overflow-auto rounded-md border">
                {preview.slice(0, 10).map((msg, i) => (
                  <div key={i} className="border-b px-2 py-1 text-xs">
                    <span className="font-medium">#{i + 1}</span>
                    {msg.subject && <span className="ml-2 text-muted-foreground">{msg.subject}</span>}
                    <span className="ml-2 truncate text-muted-foreground">{msg.body.slice(0, 60)}</span>
                  </div>
                ))}
                {preview.length > 10 && (
                  <div className="px-2 py-1 text-xs text-muted-foreground">
                    ... and {preview.length - 10} more
                  </div>
                )}
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-2 border-t px-4 py-3">
          <button
            onClick={onPreview}
            className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
            data-testid="batch-preview-btn"
          >
            Preview
          </button>
          <button
            onClick={onSend}
            disabled={!preview || batchSendMutation.isPending}
            className="flex items-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
            data-testid="batch-send-btn"
          >
            <Send className="h-3 w-3" />
            Send {preview ? `${preview.length}` : ""}
          </button>
        </div>
      </div>
    </div>
  );
}
