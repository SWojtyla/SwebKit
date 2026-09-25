import {
    AlertCircle,
    CheckCircle2,
    MessageSquareText,
    PanelRightOpen,
    XCircle,
} from "lucide-react";
import {
    useHealth,
    usePendingApprovals,
} from "@/lib/hooks";
import { useAgentConversationStore } from "@/lib/stores/agent-conversation";
import { useAgentPanelStore } from "@/lib/stores/agent-panel";

/**
 * Single strip under the command bar: sidecar connectivity, pending agent
 * approvals, and a peek at the latest assistant reply from the shared global
 * conversation — the "pinned agent" presence without duplicating a chat UI.
 */
export function AgentStatusStrip() {
    const { data: health } = useHealth();
    const pendingApprovals = usePendingApprovals();
    const messages = useAgentConversationStore((s) => s.messages);
    const setPanelOpen = useAgentPanelStore((s) => s.setOpen);

    const sidecarOk = health?.status === "ok";
    const pendingCount = pendingApprovals.data?.length ?? 0;

    const lastAssistant = [...messages]
        .reverse()
        .find((m) => m.role === "assistant" && m.content);

    return (
        <div className="mt-4 space-y-3">
            <div
                className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm"
                data-testid="sidecar-status-bar"
            >
                <span className="flex items-center gap-2">
                    {sidecarOk ? (
                        <CheckCircle2 className="h-4 w-4 text-success" />
                    ) : (
                        <XCircle className="h-4 w-4 text-destructive" />
                    )}
                    <span data-testid="sidecar-status-text">
                        Backend sidecar:{" "}
                        {sidecarOk ? "Connected" : "Disconnected"}
                    </span>
                    {health?.version && (
                        <span className="text-xs text-muted-foreground">
                            v{health.version}
                        </span>
                    )}
                </span>

                <button
                    type="button"
                    onClick={() => setPanelOpen(true)}
                    className="flex min-w-0 items-center gap-2 text-muted-foreground transition-colors hover:text-foreground"
                    data-testid="agent-peek"
                >
                    <MessageSquareText className="h-4 w-4 shrink-0 text-primary" />
                    {lastAssistant ? (
                        <span className="truncate text-xs">
                            {lastAssistant.content.slice(0, 140)}
                        </span>
                    ) : (
                        <span className="text-xs">
                            Ask the agent anything — it sees your workspace
                        </span>
                    )}
                    <PanelRightOpen className="h-3.5 w-3.5 shrink-0" />
                </button>
            </div>

            {pendingCount > 0 && (
                <button
                    type="button"
                    onClick={() => setPanelOpen(true)}
                    className="flex w-full items-center gap-3 rounded-xl border border-warning/30 bg-warning/10 p-3 text-left transition-all hover:bg-warning/20"
                    data-testid="pending-approvals-banner"
                >
                    <AlertCircle className="h-5 w-5 text-warning" />
                    <span className="text-sm font-medium">
                        {pendingCount} pending approval
                        {pendingCount > 1 ? "s" : ""} awaiting your review
                    </span>
                    <span className="ml-auto text-xs text-muted-foreground">
                        Review now →
                    </span>
                </button>
            )}
        </div>
    );
}
