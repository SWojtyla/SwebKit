import type { HttpRequestEntry, RequestAction, RequestActionKind, RequestActionSource } from "@/lib/types";
import { Trash2, Plus } from "lucide-react";

interface RequestActionsPanelProps {
  request: HttpRequestEntry;
  onChange: (request: HttpRequestEntry) => void;
}

const kinds: { value: RequestActionKind; label: string }[] = [
  { value: "CopyToClipboard", label: "Copy to Clipboard" },
  { value: "Delay", label: "Delay" },
];

const sources: { value: RequestActionSource; label: string }[] = [
  { value: "RequestUrl", label: "Request URL" },
  { value: "RequestMethod", label: "Request Method" },
  { value: "RequestBody", label: "Request Body" },
  { value: "ResponseStatusCode", label: "Response Status Code" },
  { value: "ResponseStatusText", label: "Response Status Text" },
  { value: "ResponseBody", label: "Response Body" },
  { value: "ResponseHeader", label: "Response Header" },
];

function needsSelector(kind: RequestActionKind, source: RequestActionSource): boolean {
  if (kind === "Delay") return false;
  return source === "ResponseHeader" || source === "RequestBody" || source === "ResponseBody";
}

function defaultAction(): RequestAction {
  return {
    id: crypto.randomUUID(),
    kind: "CopyToClipboard",
    name: "",
    isEnabled: true,
    source: "ResponseBody",
    selector: null,
    delayMs: 1000,
  };
}

function updateActions(
  request: HttpRequestEntry,
  phase: "preRequestActions" | "postRequestActions",
  actions: RequestAction[],
): HttpRequestEntry {
  return { ...request, [phase]: actions };
}

function updateAction(
  request: HttpRequestEntry,
  phase: "preRequestActions" | "postRequestActions",
  index: number,
  patch: Partial<RequestAction>,
): HttpRequestEntry {
  const actions = (request[phase] ?? []).map((a, i) => (i === index ? { ...a, ...patch } : a));
  return updateActions(request, phase, actions);
}

function removeAction(
  request: HttpRequestEntry,
  phase: "preRequestActions" | "postRequestActions",
  index: number,
): HttpRequestEntry {
  const actions = (request[phase] ?? []).filter((_, i) => i !== index);
  return updateActions(request, phase, actions);
}

function addAction(
  request: HttpRequestEntry,
  phase: "preRequestActions" | "postRequestActions",
): HttpRequestEntry {
  return updateActions(request, phase, [...(request[phase] ?? []), defaultAction()]);
}

function ActionSection({
  title,
  phase,
  request,
  onChange,
}: {
  title: string;
  phase: "preRequestActions" | "postRequestActions";
  request: HttpRequestEntry;
  onChange: (request: HttpRequestEntry) => void;
}) {
  const actions = request[phase] ?? [];
  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold">{title}</h3>
        <span className="text-xs text-muted-foreground">{actions.length} action(s)</span>
      </div>
      {actions.length === 0 && (
        <div className="rounded border border-dashed p-3 text-xs text-muted-foreground">
          No {title.toLowerCase()} configured.
        </div>
      )}
      {actions.map((action, i) => (
        <div
          key={action.id}
          className="flex flex-wrap items-start gap-2 rounded border p-2"
          data-testid={`${phase}-row-${i}`}
        >
          <input
            type="checkbox"
            checked={action.isEnabled}
            onChange={(e) => onChange(updateAction(request, phase, i, { isEnabled: e.target.checked }))}
            className="mt-2 h-4 w-4"
            data-testid={`${phase}-enabled-${i}`}
          />
          <input
            type="text"
            value={action.name}
            onChange={(e) => onChange(updateAction(request, phase, i, { name: e.target.value }))}
            placeholder="Name"
            className="min-w-[8rem] flex-1 rounded border bg-background px-2 py-1 text-sm"
            data-testid={`${phase}-name-${i}`}
          />
          <select
            value={action.kind}
            onChange={(e) =>
              onChange(updateAction(request, phase, i, { kind: e.target.value as RequestActionKind }))
            }
            className="rounded border bg-background px-2 py-1 text-sm"
            data-testid={`${phase}-kind-${i}`}
          >
            {kinds.map((k) => (
              <option key={k.value} value={k.value}>
                {k.label}
              </option>
            ))}
          </select>
          {action.kind === "CopyToClipboard" && (
            <>
              <select
                value={action.source}
                onChange={(e) =>
                  onChange(
                    updateAction(request, phase, i, {
                      source: e.target.value as RequestActionSource,
                      selector: null,
                    }),
                  )
                }
                className="rounded border bg-background px-2 py-1 text-sm"
                data-testid={`${phase}-source-${i}`}
              >
                {sources.map((s) => (
                  <option key={s.value} value={s.value}>
                    {s.label}
                  </option>
                ))}
              </select>
              {needsSelector(action.kind, action.source) && (
                <input
                  type="text"
                  value={action.selector ?? ""}
                  onChange={(e) =>
                    onChange(updateAction(request, phase, i, { selector: e.target.value || null }))
                  }
                  placeholder={
                    action.source === "ResponseHeader"
                      ? "Header name"
                      : "JSONPath (optional)"
                  }
                  className="min-w-[10rem] flex-[2] rounded border bg-background px-2 py-1 text-sm font-mono"
                  data-testid={`${phase}-selector-${i}`}
                />
              )}
            </>
          )}
          {action.kind === "Delay" && (
            <input
              type="number"
              min={0}
              value={action.delayMs}
              onChange={(e) =>
                onChange(updateAction(request, phase, i, { delayMs: Number(e.target.value) || 0 }))
              }
              className="w-24 rounded border bg-background px-2 py-1 text-sm"
              data-testid={`${phase}-delay-${i}`}
            />
          )}
          <button
            onClick={() => onChange(removeAction(request, phase, i))}
            className="ml-auto rounded p-1 text-destructive hover:bg-destructive/10"
            title="Remove action"
            data-testid={`${phase}-remove-${i}`}
          >
            <Trash2 className="h-4 w-4" />
          </button>
        </div>
      ))}
      <button
        onClick={() => onChange(addAction(request, phase))}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
        data-testid={`add-${phase}`}
      >
        <Plus className="h-3 w-3" /> Add action
      </button>
    </div>
  );
}

export function RequestActionsPanel({ request, onChange }: RequestActionsPanelProps) {
  return (
    <div className="space-y-6" data-testid="actions-tab">
      <ActionSection title="Pre-request actions" phase="preRequestActions" request={request} onChange={onChange} />
      <ActionSection title="Post-request actions" phase="postRequestActions" request={request} onChange={onChange} />
    </div>
  );
}
