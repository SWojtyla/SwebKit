import { useState } from "react";
import { useUserSettings, useUpdateUserSettings } from "@/lib/hooks";
import { useTestAgentProfile } from "@/lib/hooks/useAgent";
import type { AgentProfile } from "@/lib/types";

const capabilityLabel: Record<AgentProfile["capability"], string> = {
  Unknown: "Not tested",
  ChatOnly: "Chat only (no tool calling)",
  ToolCalling: "Tool calling supported",
};

export function AgentSettings() {
  const { data: settings, isLoading } = useUserSettings();
  const updateSettings = useUpdateUserSettings();
  const testProfile = useTestAgentProfile();
  const [testingId, setTestingId] = useState<string | null>(null);

  if (isLoading || !settings) {
    return <div className="text-muted-foreground">Loading...</div>;
  }

  const agent = settings.agent;

  const update = (patch: Partial<typeof agent>) => {
    updateSettings.mutate({
      ...settings,
      agent: { ...agent, ...patch },
    });
  };

  const updateProfile = (index: number, patch: Partial<AgentProfile>) => {
    const profiles = [...agent.profiles];
    profiles[index] = { ...profiles[index], ...patch };
    update({ profiles });
  };

  const runTest = (index: number) => {
    const profile = agent.profiles[index];
    setTestingId(profile.id);
    testProfile.mutate(profile.id, {
      onSuccess: (result) => {
        setTestingId(null);
        updateProfile(index, {
          capability: result.capability,
          lastTestDiagnostic: result.diagnostic,
        });
      },
      onError: (err) => {
        setTestingId(null);
        updateProfile(index, {
          capability: "Unknown",
          lastTestDiagnostic: err instanceof Error ? err.message : "Test failed",
        });
      },
    });
  };

  return (
    <div className="space-y-4">
      <section>
        <h2 className="mb-3 text-lg font-semibold">AI Agent</h2>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={agent.isEnabled}
            onChange={(e) => update({ isEnabled: e.target.checked })}
          />
          Enable AI Agent
        </label>
      </section>

      <section>
        <h3 className="mb-3 text-base font-semibold">Provider Profiles</h3>
        {agent.profiles.map((p, i) => (
          <div key={p.id} className="mb-3 space-y-2 rounded-lg border p-3">
            <div className="flex items-center justify-between">
              <input
                type="text"
                value={p.displayName}
                onChange={(e) => updateProfile(i, { displayName: e.target.value })}
                className="flex-1 rounded-md border bg-card px-3 py-1.5 text-sm"
                placeholder="Profile name"
              />
              <button
                onClick={() => {
                  const profiles = agent.profiles.filter((_, idx) => idx !== i);
                  update({
                    profiles,
                    activeProfileId:
                      agent.activeProfileId === p.id
                        ? (profiles[0]?.id ?? "")
                        : agent.activeProfileId,
                  });
                }}
                className="ml-2 text-sm text-destructive hover:opacity-80"
              >
                Remove
              </button>
            </div>
            <select
              value={p.provider}
              onChange={(e) =>
                updateProfile(i, { provider: e.target.value as AgentProfile["provider"] })
              }
              className="w-full rounded-md border bg-card px-2 py-1.5 text-sm"
            >
              <option value="LmStudio">LM Studio (local)</option>
              <option value="OpenAiCompatible">OpenAI-compatible</option>
              <option value="Mistral">Mistral AI</option>
            </select>
            <input
              type="text"
              value={p.baseUrl}
              onChange={(e) => updateProfile(i, { baseUrl: e.target.value })}
              className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
              placeholder="Base URL (e.g. http://localhost:1234/v1)"
              data-testid={`agent-profile-base-url-${i}`}
            />
            <input
              type="text"
              value={p.model}
              onChange={(e) => updateProfile(i, { model: e.target.value })}
              className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
              placeholder="Model name"
            />
            {p.provider !== "LmStudio" && (
              <input
                type="text"
                value={p.credentialKey}
                onChange={(e) => updateProfile(i, { credentialKey: e.target.value })}
                className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                placeholder="Credential key (resolved via the OS credential store)"
              />
            )}
            <div className="grid grid-cols-3 gap-2">
              <div>
                <label className="mb-1 block text-xs text-muted-foreground">Temperature</label>
                <input
                  type="number"
                  step="0.1"
                  min="0"
                  max="2"
                  value={p.temperature}
                  onChange={(e) =>
                    updateProfile(i, { temperature: parseFloat(e.target.value) || 0 })
                  }
                  className="w-full rounded-md border bg-card px-2 py-1.5 text-sm"
                />
              </div>
              <div>
                <label className="mb-1 block text-xs text-muted-foreground">Max tokens</label>
                <input
                  type="number"
                  min="1"
                  value={p.maxTokens}
                  onChange={(e) =>
                    updateProfile(i, { maxTokens: parseInt(e.target.value) || 2048 })
                  }
                  className="w-full rounded-md border bg-card px-2 py-1.5 text-sm"
                />
              </div>
              <div>
                <label className="mb-1 block text-xs text-muted-foreground">Timeout (s)</label>
                <input
                  type="number"
                  min="1"
                  value={p.timeoutSeconds}
                  onChange={(e) =>
                    updateProfile(i, { timeoutSeconds: parseInt(e.target.value) || 60 })
                  }
                  className="w-full rounded-md border bg-card px-2 py-1.5 text-sm"
                />
              </div>
            </div>
            <div className="flex items-center justify-between gap-2 pt-1">
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="radio"
                  checked={agent.activeProfileId === p.id}
                  onChange={() => update({ activeProfileId: p.id })}
                />
                Active profile
              </label>
              <button
                onClick={() => runTest(i)}
                disabled={testingId === p.id}
                className="rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                data-testid={`agent-profile-test-${i}`}
              >
                {testingId === p.id ? "Testing…" : "Test connection"}
              </button>
            </div>
            <div className="text-xs text-muted-foreground" data-testid={`agent-profile-capability-${i}`}>
              {capabilityLabel[p.capability]}
              {p.lastTestDiagnostic && ` — ${p.lastTestDiagnostic}`}
            </div>
          </div>
        ))}
        <button
          onClick={() => {
            const newProfile: AgentProfile = {
              id: crypto.randomUUID(),
              provider: "LmStudio",
              displayName: "New Profile",
              baseUrl: "http://localhost:1234/v1",
              model: "",
              credentialKey: "",
              temperature: 0.7,
              maxTokens: 2048,
              timeoutSeconds: 120,
              capability: "Unknown",
              lastTestDiagnostic: null,
              requiresApiKey: false,
            };
            update({
              profiles: [...agent.profiles, newProfile],
              activeProfileId: agent.activeProfileId || newProfile.id,
            });
          }}
          className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
          data-testid="agent-add-profile"
        >
          Add Profile
        </button>
      </section>

      <section>
        <h3 className="mb-3 text-base font-semibold">History</h3>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="mb-1 block text-sm font-medium">Max History Messages</label>
            <input
              type="number"
              value={agent.maxHistoryMessages}
              onChange={(e) =>
                update({ maxHistoryMessages: parseInt(e.target.value) || 20 })
              }
              className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
            />
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium">Warning Threshold (%)</label>
            <input
              type="number"
              value={agent.historyWarningThresholdPercent}
              onChange={(e) =>
                update({
                  historyWarningThresholdPercent: parseInt(e.target.value) || 75,
                })
              }
              className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
            />
          </div>
        </div>
      </section>
    </div>
  );
}
