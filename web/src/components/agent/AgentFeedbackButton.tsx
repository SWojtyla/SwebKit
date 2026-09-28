import { useState } from "react";
import { Check, ThumbsDown } from "lucide-react";
import { useSubmitAgentFeedback } from "@/lib/hooks/useAgent";
import { isFeedbackEligible } from "./agent-feedback";
import type { ChatMessage } from "@/lib/types";

/**
 * Thumbs-down control on completed assistant messages (agent-colleague item 5). One click posts
 * `{ exchangeId, sentiment: "down" }` to /api/agent/feedback — the sidecar resolves the id
 * against its exchange ring buffer and flushes the redacted exchange to agent-feedback.json, so
 * nothing about the transcript is echoed back from the client.
 *
 * Renders nothing when the message isn't rateable (`isFeedbackEligible` — e.g. errors, stopped
 * streams, or messages older than the exchangeId mechanism), and latches into a "recorded"
 * state after a successful submit.
 */
export function AgentFeedbackButton({
    message,
    testId,
}: {
    message: ChatMessage;
    testId: string;
}) {
    const submit = useSubmitAgentFeedback();
    const [sent, setSent] = useState(false);

    if (!isFeedbackEligible(message)) return null;

    return (
        <button
            type="button"
            aria-label={sent ? "Feedback recorded" : "Mark this answer as unhelpful"}
            title={
                sent
                    ? "Feedback recorded"
                    : "Mark this answer as unhelpful — saves the exchange for review"
            }
            disabled={sent || submit.isPending}
            onClick={() =>
                submit.mutate(
                    { exchangeId: message.exchangeId! },
                    { onSuccess: () => setSent(true) },
                )
            }
            className="mt-1 inline-flex items-center gap-1 rounded px-1 py-0.5 text-xs text-muted-foreground hover:bg-accent hover:text-foreground disabled:opacity-60"
            data-testid={sent ? `${testId}-sent` : testId}
        >
            {sent ? (
                <>
                    <Check className="h-3 w-3" /> Noted
                </>
            ) : (
                <ThumbsDown className="h-3 w-3" />
            )}
        </button>
    );
}
