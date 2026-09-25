import { useState } from "react";
import { Bot, Sparkles } from "lucide-react";
import { useAgentPanelStore } from "@/lib/stores/agent-panel";

/**
 * Dashboard natural-language entry into the global agent conversation. Queues
 * the prompt on `useAgentPanelStore` and docks the panel open — the panel owns
 * the actual `send()` (it's the single `useAgentChatStream` instance), so the
 * message lands in the real conversation and the user stays on this page
 * watching the reply stream in beside the dashboard.
 */
export function CockpitCommandBar() {
    const [command, setCommand] = useState("");
    const queuePrompt = useAgentPanelStore((s) => s.queuePrompt);

    const handleCommand = () => {
        const trimmed = command.trim();
        if (!trimmed) return;
        queuePrompt(trimmed);
        setCommand("");
    };

    return (
        <div
            className="glass-card mt-4 flex items-center gap-3 rounded-xl p-3"
            data-testid="cockpit-command-bar"
        >
            <Bot className="h-5 w-5 text-primary" />
            <input
                type="text"
                value={command}
                onChange={(e) => setCommand(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && handleCommand()}
                placeholder="Ask the agent to investigate, compare, or visualize something in your workspace"
                className="flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground"
                data-testid="cockpit-command-input"
            />
            <button
                onClick={handleCommand}
                disabled={!command.trim()}
                title={!command.trim() ? "Type a question first" : undefined}
                className="flex items-center gap-1 rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90 disabled:opacity-50"
                data-testid="cockpit-command-send"
            >
                <Sparkles className="h-3.5 w-3.5" />
                Ask
            </button>
        </div>
    );
}
