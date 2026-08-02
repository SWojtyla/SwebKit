import { useState } from "react";
import { useAgentChat, usePendingApprovals } from "@/lib/hooks";
import { PendingActionCard } from "@/components/agent/PendingActionCard";

interface GenerateApiRequestPanelProps {
  requestId: string;
  onClose: () => void;
}

/**
 * The API Client's dedicated "generate a request" flow (ai-augmented-app technical-plan.md
 * Module 6) — a focused single-purpose prompt, not the full ContextualAssistant chat UI, since
 * this is meant to be the highest-frequency "do" action in the app. Always runs in Ask & do mode
 * (no toggle shown — generating/editing a request is the whole point of this panel) and always
 * targets the request currently open in the editor as an update. The model is nudged toward the
 * right tool via the message text itself rather than a new system-prompt-hint mechanism, since one
 * extra sentence achieves the same effect without adding a bespoke backend hook for this one flow.
 *
 * Creating a brand-new request in a different collection isn't handled here (there's no collection
 * picker in this compact flow) — describe that from the global /agent page instead, where the model
 * can ask which collection or use search_api_requests to disambiguate.
 */
export function GenerateApiRequestPanel({ requestId, onClose }: GenerateApiRequestPanelProps) {
  const [description, setDescription] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const chat = useAgentChat(`api-client-generate-${requestId}`);
  const pendingApprovals = usePendingApprovals();

  const handleGenerate = () => {
    const text = description.trim();
    if (!text || chat.isPending) return;

    setSubmitted(true);
    chat.mutate({
      message:
        `Generate/update this API request: ${text}. Use the propose_api_request_change tool ` +
        `with operation "update" and request_id "${requestId}" (the request currently open in the ` +
        `editor) — do not create a new request.`,
      context: { featureArea: "ApiClient", selection: { requestId } },
      mode: "ask_and_do",
    });
  };

  return (
    <div className="absolute right-3 top-14 z-40 w-96 rounded-lg border bg-card p-3 shadow-lg" data-testid="generate-api-request-panel">
      <div className="mb-2 flex items-center justify-between">
        <h3 className="text-sm font-semibold">Generate request with AI</h3>
        <button onClick={onClose} className="rounded p-1 text-sm hover:bg-accent" data-testid="generate-api-request-close">
          ✕
        </button>
      </div>

      {!submitted ? (
        <>
          <textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                handleGenerate();
              }
            }}
            placeholder="Describe the request you want, e.g. 'POST to /auth/login with a JSON body containing username and password'"
            rows={3}
            autoFocus
            className="w-full resize-none rounded-md border bg-background px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            data-testid="generate-api-request-input"
          />
          <button
            onClick={handleGenerate}
            disabled={!description.trim() || chat.isPending}
            className="mt-2 w-full rounded-md bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
            data-testid="generate-api-request-submit"
          >
            {chat.isPending ? "Generating…" : "Generate"}
          </button>
        </>
      ) : chat.isPending ? (
        <p className="text-sm text-muted-foreground">Generating…</p>
      ) : pendingApprovals.data && pendingApprovals.data.length > 0 ? (
        <div className="space-y-2">
          {pendingApprovals.data.map((action) => (
            <PendingActionCard key={action.id} action={action} onApplied={onClose} />
          ))}
        </div>
      ) : (
        <p className="text-sm text-muted-foreground" data-testid="generate-api-request-no-proposal">
          {chat.data?.text ?? "No change was proposed. Try describing the request differently."}
        </p>
      )}
    </div>
  );
}
