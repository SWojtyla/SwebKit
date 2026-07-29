import { useUserSettings, useUpdateUserSettings } from "@/lib/hooks";

export function AgentSettings() {
  const { data: settings, isLoading } = useUserSettings();
  const updateSettings = useUpdateUserSettings();

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
                onChange={(e) => {
                  const profiles = [...agent.profiles];
                  profiles[i] = { ...p, displayName: e.target.value };
                  update({ profiles });
                }}
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
                        ? profiles[0]?.id ?? ""
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
              onChange={(e) => {
                const profiles = [...agent.profiles];
                profiles[i] = { ...p, provider: e.target.value };
                update({ profiles });
              }}
              className="w-full rounded-md border bg-card px-2 py-1.5 text-sm"
            >
              <option value="Mistral">Mistral AI</option>
              <option value="LmStudio">LM Studio</option>
              <option value="OpenAI">OpenAI-compatible</option>
            </select>
            <input
              type="text"
              value={p.endpointUrl}
              onChange={(e) => {
                const profiles = [...agent.profiles];
                profiles[i] = { ...p, endpointUrl: e.target.value };
                update({ profiles });
              }}
              className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
              placeholder="Endpoint URL"
            />
            <input
              type="text"
              value={p.model}
              onChange={(e) => {
                const profiles = [...agent.profiles];
                profiles[i] = { ...p, model: e.target.value };
                update({ profiles });
              }}
              className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
              placeholder="Model name"
            />
            <label className="flex items-center gap-2 text-sm">
              <input
                type="radio"
                checked={agent.activeProfileId === p.id}
                onChange={() => update({ activeProfileId: p.id })}
              />
              Active profile
            </label>
          </div>
        ))}
        <button
          onClick={() => {
            const newProfile = {
              id: crypto.randomUUID(),
              provider: "LmStudio",
              displayName: "New Profile",
              endpointUrl: "http://localhost:1234/v1",
              model: "",
              credentialKey: "",
            };
            update({
              profiles: [...agent.profiles, newProfile],
              activeProfileId: agent.activeProfileId || newProfile.id,
            });
          }}
          className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
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
