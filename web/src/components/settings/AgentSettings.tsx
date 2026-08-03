import { useState } from "react";
import { useProfile, useUpdateProfile, useUserSettings, useUpdateUserSettings } from "@/lib/hooks";
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
  const { data: profile } = useProfile();
  const updateProfileData = useUpdateProfile();

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
    testProfile.mutate(profile, {
      onSuccess: (result) => {
        setTestingId(null);
        updateProfile(index, {
          capability: result.capability,
          lastTestDiagnostic: result.diagnostic,
          // Only overwrite when the provider actually advertised one — never clear a value the
          // user already set by hand just because this provider's /v1/models doesn't report it.
          ...(result.detectedContextWindowTokens != null
            ? { contextWindowTokens: result.detectedContextWindowTokens }
            : {}),
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
            {/* Temperature and max output tokens are deliberately not exposed here — those are
                generation parameters the provider (LM Studio, etc.) already controls, and
                duplicating them here would just create two different, silently-conflicting
                settings. Timeout stays: it's this app's own HTTP client patience, not something
                the provider has a say in. */}
            <div className="flex gap-3">
              <div className="w-32">
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
              <div className="w-40">
                <label className="mb-1 block text-xs text-muted-foreground">
                  Context window (tokens)
                </label>
                <input
                  type="number"
                  min="1"
                  value={p.contextWindowTokens ?? ""}
                  onChange={(e) =>
                    updateProfile(i, {
                      contextWindowTokens: e.target.value ? parseInt(e.target.value) || null : null,
                    })
                  }
                  placeholder="Auto/unknown"
                  className="w-full rounded-md border bg-card px-2 py-1.5 text-sm"
                  data-testid={`agent-profile-context-window-${i}`}
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
              {p.contextWindowTokens
                ? ` · ${p.contextWindowTokens.toLocaleString()}-token window`
                : " · unknown context window (using a 4,096-token conservative default)"}
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
              timeoutSeconds: 120,
              capability: "Unknown",
              lastTestDiagnostic: null,
              requiresApiKey: false,
              contextWindowTokens: null,
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

      {profile && (
        <section>
          <h3 className="mb-1 text-base font-semibold">Application Insights (optional)</h3>
          <p className="mb-3 text-xs text-muted-foreground">
            Feeds the agent's <code>get_metrics</code>/<code>query_logs</code> tools with your
            telemetry as extra context when relevant — there's no dedicated Observability page or
            log/metric browser in this app, only agent-tool access. Authenticates via your Azure
            CLI/VS login (no credential to enter here).
          </p>
          <div className="space-y-2">
            <input
              type="text"
              value={profile.config.observabilityConfig?.selectedResourceId ?? ""}
              onChange={(e) =>
                updateProfileData.mutate({
                  ...profile,
                  config: {
                    ...profile.config,
                    observabilityConfig: {
                      selectedResourceId: e.target.value || null,
                      selectedResourceName: profile.config.observabilityConfig?.selectedResourceName ?? null,
                    },
                  },
                })
              }
              className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
              placeholder="Resource ID (/subscriptions/.../components/your-app-insights)"
              data-testid="observability-resource-id"
            />
            <input
              type="text"
              value={profile.config.observabilityConfig?.selectedResourceName ?? ""}
              onChange={(e) =>
                updateProfileData.mutate({
                  ...profile,
                  config: {
                    ...profile.config,
                    observabilityConfig: {
                      selectedResourceId: profile.config.observabilityConfig?.selectedResourceId ?? null,
                      selectedResourceName: e.target.value || null,
                    },
                  },
                })
              }
              className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
              placeholder="Display name (optional, e.g. Prod App Insights)"
              data-testid="observability-resource-name"
            />
          </div>
        </section>
      )}
    </div>
  );
}
