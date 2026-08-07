import { useNavigate } from "react-router";
import { useAgentConversationStore } from "@/lib/stores/agent-conversation";
import { useDemoMode, useToggleDemoMode } from "@/lib/hooks";
import { getCrossFeatureScenario } from "@/lib/demo-scenarios";
import { useNotification } from "@/components/layout/NotificationSystem";
import { Rocket } from "lucide-react";

export function CrossFeatureDemoButton({ className }: { className?: string }) {
  const navigate = useNavigate();
  const { data: demoMode } = useDemoMode();
  const toggleDemo = useToggleDemoMode();
  const addMessage = useAgentConversationStore((s) => s.addMessage);
  const { notify } = useNotification();

  const handleRun = () => {
    const enabled = demoMode?.isDemoMode ?? false;
    if (!enabled) {
      toggleDemo.mutate(true, {
        onSuccess: () => runScenario(),
        onError: (err: Error) => notify("error", "Demo mode failed", err.message),
      });
    } else {
      runScenario();
    }
  };

  const runScenario = () => {
    const scenario = getCrossFeatureScenario();
    for (const message of scenario.messages) {
      addMessage(message);
    }
    navigate("/agent?scenario=" + encodeURIComponent(scenario.id));
  };

  return (
    <button
      onClick={handleRun}
      disabled={toggleDemo.isPending}
      className={`inline-flex items-center gap-1.5 rounded-md border border-primary/50 bg-primary/10 px-3 py-1.5 text-sm font-medium text-primary hover:bg-primary/20 disabled:opacity-50 ${className ?? ""}`}
      data-testid="cross-feature-demo-button"
    >
      <Rocket className="h-3.5 w-3.5" /> Cross-feature demo
    </button>
  );
}
