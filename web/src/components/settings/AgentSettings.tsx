import { useEffect, useRef, useState } from "react";
import {
    useProfile,
    useUpdateProfile,
    useUserSettings,
    useUpdateUserSettings,
} from "@/lib/hooks";
import { useTestAgentProfile } from "@/lib/hooks/useAgent";
import { useObservabilityResources } from "@/lib/hooks/useProfile";
import { DraftInput } from "./DraftInput";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { ProfileListLayout } from "./ProfileListLayout";
import type {
    AgentProfile,
    ProfileData,
    ObservabilityConfig,
    ObservabilityResource,
} from "@/lib/types";

const capabilityLabel: Record<AgentProfile["capability"], string> = {
    Unknown: "Not tested",
    ChatOnly: "Chat only (no tool calling)",
    ToolCalling: "Tool calling supported",
};

const providerLabel: Record<AgentProfile["provider"], string> = {
    LmStudio: "LM Studio",
    OpenAiCompatible: "OpenAI-compatible",
    Mistral: "Mistral AI",
    Acp: "ACP agent",
};

/** A profile is worth confirming removal of once it has real configured data — an untouched
 * "New Profile" placeholder can go without the extra click. */
function isConfigured(p: AgentProfile): boolean {
    return (
        p.baseUrl.trim() !== "" ||
        p.model.trim() !== "" ||
        p.credentialKey.trim() !== "" ||
        p.command.trim() !== ""
    );
}

/** Spawn defaults for known ACP agents — applied onto the current profile via "Load preset",
 * not selected as a provider of their own (the command is what actually makes each work). */
const ACP_PRESETS: { label: string; patch: Partial<AgentProfile> }[] = [
    {
        label: "Claude (claude-agent-acp via npx)",
        patch: {
            command: "npx",
            arguments: "-y @agentclientprotocol/claude-agent-acp",
            credentialEnvVar: "ANTHROPIC_API_KEY",
        },
    },
    {
        label: "Gemini CLI (gemini --acp)",
        patch: {
            command: "gemini",
            arguments: "--acp",
            credentialEnvVar: "GEMINI_API_KEY",
        },
    },
];

export function AgentSettings() {
    const { data: settings, isLoading } = useUserSettings();
    const updateSettings = useUpdateUserSettings();
    const testProfile = useTestAgentProfile();
    const [testingId, setTestingId] = useState<string | null>(null);
    const [pendingRemoveId, setPendingRemoveId] = useState<string | null>(null);
    const { data: profile } = useProfile();
    const updateProfileData = useUpdateProfile();
    const resources = useObservabilityResources();
    // In-progress DraftInput text per profile id — "Test connection" merges this so
    // it exercises what is typed, not a possibly-stale saved copy (the blur commit's
    // PUT can't have landed by the time the button's click handler runs).
    const draftsRef = useRef<Record<string, Partial<AgentProfile>>>({});

    if (isLoading || !settings) {
        return <div className="text-muted-foreground">Loading...</div>;
    }

    const agent = settings.agent;

    const update = (patch: Partial<typeof agent>) => {
        updateSettings.mutate((prev) => ({
            ...prev,
            agent: { ...prev.agent, ...patch },
        }));
    };

    const updateProfile = (index: number, patch: Partial<AgentProfile>) => {
        const profiles = [...agent.profiles];
        profiles[index] = { ...profiles[index], ...patch };
        update({ profiles });
    };

    const trackDraft = (id: string, patch: Partial<AgentProfile>) => {
        draftsRef.current[id] = { ...draftsRef.current[id], ...patch };
    };

    const removeProfileAt = (index: number) => {
        const profiles = agent.profiles.filter((_, idx) => idx !== index);
        const nextActive =
            agent.activeProfileId === agent.profiles[index].id
                ? (profiles[0]?.id ?? "")
                : agent.activeProfileId;
        // `isEnabled` tracks the active profile rather than a standalone toggle:
        // enabled iff a profile is active.
        update({
            profiles,
            activeProfileId: nextActive,
            isEnabled: nextActive !== "",
        });
    };

    const requestRemoveProfile = (index: number) => {
        if (isConfigured(agent.profiles[index])) {
            setPendingRemoveId(agent.profiles[index].id);
        } else {
            removeProfileAt(index);
        }
    };

    const runTest = (index: number) => {
        const profile = {
            ...agent.profiles[index],
            ...draftsRef.current[agent.profiles[index].id],
        };
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
                        ? {
                              contextWindowTokens:
                                  result.detectedContextWindowTokens,
                          }
                        : {}),
                });
            },
            onError: (err) => {
                setTestingId(null);
                updateProfile(index, {
                    capability: "Unknown",
                    lastTestDiagnostic:
                        err instanceof Error ? err.message : "Test failed",
                });
            },
        });
    };

    return (
        <div className="space-y-4">
            <section>
                <h2 className="mb-1 text-lg font-semibold">AI Agent</h2>
                <p className="text-xs text-muted-foreground">
                    The agent is enabled whenever a provider profile is marked
                    active — there is no separate on/off switch.
                </p>
            </section>

            <section>
                <h3 className="mb-3 text-base font-semibold">
                    Provider Profiles
                </h3>
                <ProfileListLayout
                    items={agent.profiles}
                    getKey={(p) => p.id}
                    getTitle={(p) => p.displayName}
                    getSubtitle={(p) =>
                        `${providerLabel[p.provider]}${p.provider === "Acp" ? (p.command ? ` — ${p.command}` : "") : p.baseUrl ? ` — ${p.baseUrl}` : ""}`
                    }
                    isActive={(p) => p.id === agent.activeProfileId}
                    testIdPrefix="agent"
                    emptyMessage='No provider profiles yet. Click "Add Profile" to create one.'
                    renderEditor={(p, i) => (
                        <div className="space-y-2 rounded-lg border p-3">
                            <div className="flex items-center justify-between">
                                <DraftInput
                                    type="text"
                                    value={p.displayName}
                                    onCommit={(v) =>
                                        updateProfile(i, { displayName: v })
                                    }
                                    className="flex-1 rounded-md border bg-card px-3 py-1.5 text-sm"
                                    placeholder="Profile name"
                                />
                                <button
                                    onClick={() => requestRemoveProfile(i)}
                                    className="ml-2 text-sm text-destructive hover:opacity-80"
                                    data-testid={`agent-profile-remove-${p.id}`}
                                >
                                    Remove
                                </button>
                            </div>
                            <select
                                value={p.provider}
                                onChange={(e) =>
                                    updateProfile(i, {
                                        provider: e.target
                                            .value as AgentProfile["provider"],
                                    })
                                }
                                className="w-full rounded-md border bg-card px-2 py-1.5 text-sm"
                            >
                                <option value="LmStudio">
                                    LM Studio (local)
                                </option>
                                <option value="OpenAiCompatible">
                                    OpenAI-compatible
                                </option>
                                <option value="Mistral">Mistral AI</option>
                                <option value="Acp">
                                    External agent (ACP)
                                </option>
                            </select>
                            {p.provider === "Acp" ? (
                                <AcpProfileFields
                                    profile={p}
                                    profileId={p.id}
                                    onUpdate={(patch) =>
                                        updateProfile(i, patch)
                                    }
                                    onDraft={(patch) => trackDraft(p.id, patch)}
                                />
                            ) : (
                                <>
                                    <DraftInput
                                        type="text"
                                        value={p.baseUrl}
                                        onCommit={(v) =>
                                            updateProfile(i, { baseUrl: v })
                                        }
                                        onDraftChange={(v) =>
                                            trackDraft(p.id, { baseUrl: v })
                                        }
                                        className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                                        placeholder="Base URL (e.g. http://localhost:1234/v1)"
                                        data-testid={`agent-profile-base-url-${p.id}`}
                                    />
                                    <DraftInput
                                        type="text"
                                        value={p.model}
                                        onCommit={(v) =>
                                            updateProfile(i, { model: v })
                                        }
                                        onDraftChange={(v) =>
                                            trackDraft(p.id, { model: v })
                                        }
                                        className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                                        placeholder="Model name"
                                    />
                                    {p.provider !== "LmStudio" && (
                                        <div>
                                            <DraftInput
                                                type="text"
                                                value={p.credentialKey}
                                                onCommit={(v) =>
                                                    updateProfile(i, {
                                                        credentialKey: v,
                                                    })
                                                }
                                                onDraftChange={(v) =>
                                                    trackDraft(p.id, {
                                                        credentialKey: v,
                                                    })
                                                }
                                                className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                                                placeholder="Credential key (resolved via the OS credential store)"
                                            />
                                            <p className="mt-1 text-xs text-muted-foreground">
                                                Looked up in your OS credential
                                                store — save the provider's
                                                actual API key there under this
                                                key (not typed here).
                                            </p>
                                        </div>
                                    )}
                                </>
                            )}
                            {/* Temperature and max output tokens are deliberately not exposed here — those are
                generation parameters the provider (LM Studio, etc.) already controls, and
                duplicating them here would just create two different, silently-conflicting
                settings. Timeout stays: it's this app's own HTTP client patience, not something
                the provider has a say in. For ACP profiles neither applies — the agent owns its
                own context window and a fixed HTTP timeout would cut long agent turns short. */}
                            {p.provider !== "Acp" && (
                                <div className="flex gap-3">
                                    <div className="w-32">
                                        <label className="mb-1 block text-xs text-muted-foreground">
                                            Timeout (s)
                                        </label>
                                        <DraftInput
                                            type="number"
                                            min="1"
                                            value={String(p.timeoutSeconds)}
                                            onCommit={(v) =>
                                                updateProfile(i, {
                                                    timeoutSeconds:
                                                        parseInt(v) || 60,
                                                })
                                            }
                                            onDraftChange={(v) =>
                                                trackDraft(p.id, {
                                                    timeoutSeconds:
                                                        parseInt(v) || 60,
                                                })
                                            }
                                            className="w-full rounded-md border bg-card px-2 py-1.5 text-sm"
                                        />
                                    </div>
                                    <div className="w-40">
                                        <label className="mb-1 block text-xs text-muted-foreground">
                                            Context window (tokens)
                                        </label>
                                        <DraftInput
                                            type="number"
                                            min="1"
                                            value={
                                                p.contextWindowTokens != null
                                                    ? String(
                                                          p.contextWindowTokens,
                                                      )
                                                    : ""
                                            }
                                            onCommit={(v) =>
                                                updateProfile(i, {
                                                    contextWindowTokens: v
                                                        ? parseInt(v) || null
                                                        : null,
                                                })
                                            }
                                            onDraftChange={(v) =>
                                                trackDraft(p.id, {
                                                    contextWindowTokens: v
                                                        ? parseInt(v) || null
                                                        : null,
                                                })
                                            }
                                            placeholder="Auto/unknown"
                                            className="w-full rounded-md border bg-card px-2 py-1.5 text-sm"
                                            data-testid={`agent-profile-context-window-${p.id}`}
                                        />
                                    </div>
                                </div>
                            )}
                            <div className="flex items-center justify-between gap-2 pt-1">
                                <label className="flex items-center gap-2 text-sm">
                                    <input
                                        type="radio"
                                        checked={agent.activeProfileId === p.id}
                                        onChange={() =>
                                            update({
                                                activeProfileId: p.id,
                                                isEnabled: true,
                                            })
                                        }
                                    />
                                    Active profile
                                </label>
                                <button
                                    onClick={() => runTest(i)}
                                    disabled={testingId === p.id}
                                    className="rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                                    data-testid={`agent-profile-test-${p.id}`}
                                >
                                    {testingId === p.id
                                        ? "Testing…"
                                        : "Test connection"}
                                </button>
                            </div>
                            <div
                                className="text-xs text-muted-foreground"
                                data-testid={`agent-profile-capability-${p.id}`}
                            >
                                {capabilityLabel[p.capability]}
                                {p.lastTestDiagnostic &&
                                    ` — ${p.lastTestDiagnostic}`}
                                {p.provider !== "Acp" &&
                                    (p.contextWindowTokens
                                        ? ` · ${p.contextWindowTokens.toLocaleString()}-token window`
                                        : " · unknown context window (using a 4,096-token conservative default)")}
                            </div>
                            {pendingRemoveId === p.id && (
                                <ConfirmBar
                                    message={`Remove "${p.displayName}"? This deletes its configuration from your profile.`}
                                    confirmLabel="Remove"
                                    onConfirm={() => {
                                        removeProfileAt(i);
                                        setPendingRemoveId(null);
                                    }}
                                    onCancel={() => setPendingRemoveId(null)}
                                    testId={`agent-profile-remove-confirm-${p.id}`}
                                />
                            )}
                        </div>
                    )}
                />
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
                            command: "",
                            arguments: "",
                            workingDirectory: "",
                            environmentVariables: {},
                            credentialEnvVar: "",
                            requireToolApproval: false,
                            enableFileSystem: false,
                            enableTerminal: false,
                        };
                        update({
                            profiles: [...agent.profiles, newProfile],
                            activeProfileId:
                                agent.activeProfileId || newProfile.id,
                            // Enabled is derived from having an active profile — a
                            // first profile turning active flips it on.
                            isEnabled: true,
                        });
                    }}
                    className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
                    data-testid="agent-add-profile"
                >
                    Add Profile
                </button>
            </section>

            {profile && (
                <ObservabilitySettings
                    profile={profile}
                    resources={resources}
                    // Merge against `prev.config.observabilityConfig` (read fresh inside the updater,
                    // same as every other field in this hook) rather than a whole replacement object —
                    // `handleManualId`/`handleManualName` fire from two separate `DraftInput`s, and each
                    // closes over whatever `config` was current at its own last render. Without this
                    // merge, committing the ID field then the Name field in quick succession (the
                    // common "fill both, tab through") had the second commit's stale closure silently
                    // blank out whatever the first had just set.
                    onUpdate={(patch) =>
                        updateProfileData.mutate((prev) => ({
                            ...prev,
                            config: {
                                ...prev.config,
                                observabilityConfig: {
                                    ...(prev.config.observabilityConfig ?? {
                                        selectedResourceId: null,
                                        selectedResourceName: null,
                                    }),
                                    ...patch,
                                },
                            },
                        }))
                    }
                />
            )}
        </div>
    );
}

interface ObservabilitySettingsProps {
    profile: ProfileData;
    resources: {
        data?: ObservabilityResource[];
        isLoading: boolean;
        error: Error | null;
        refetch: () => void;
    };
    /** A partial patch, merged onto the current `observabilityConfig` — see the call site's
     * comment for why this can't be a whole replacement object. */
    onUpdate: (config: Partial<ObservabilityConfig>) => void;
}

function ObservabilitySettings({
    profile,
    resources,
    onUpdate,
}: ObservabilitySettingsProps) {
    const config = profile.config.observabilityConfig;
    const selectedResourceId = config?.selectedResourceId ?? null;
    const selectedResource = resources.data?.find(
        (r) => r.resourceId === selectedResourceId,
    );

    useEffect(() => {
        if (resources.data?.length === 1 && !selectedResourceId) {
            const only = resources.data[0];
            onUpdate({
                selectedResourceId: only.resourceId,
                selectedResourceName: only.name,
            });
        }
    }, [resources.data, selectedResourceId, onUpdate]);

    const handleSelect = (resourceId: string) => {
        if (resourceId === "" || resourceId === "__manual__") {
            onUpdate({ selectedResourceId: null, selectedResourceName: null });
            return;
        }
        const resource = resources.data?.find(
            (r) => r.resourceId === resourceId,
        );
        onUpdate({
            selectedResourceId: resourceId,
            selectedResourceName: resource?.name ?? null,
        });
    };

    const handleManualId = (value: string) => {
        onUpdate({ selectedResourceId: value || null });
    };

    const handleManualName = (value: string) => {
        onUpdate({ selectedResourceName: value || null });
    };

    return (
        <section>
            <h3 className="mb-1 text-base font-semibold">
                Application Insights (optional)
            </h3>
            <p className="mb-3 text-xs text-muted-foreground">
                Feeds the agent's <code>get_metrics</code>/
                <code>query_logs</code> tools with your telemetry as extra
                context when relevant — no credential here, auth is via your
                Azure CLI/VS login.
            </p>
            <div className="space-y-2">
                <div className="flex items-center gap-2">
                    <select
                        value={selectedResourceId ?? ""}
                        onChange={(e) => handleSelect(e.target.value)}
                        className="flex-1 rounded-md border bg-card px-2 py-1.5 text-sm"
                        data-testid="observability-resource-select"
                        disabled={resources.isLoading}
                    >
                        <option value="">None / manual entry</option>
                        {resources.data?.map((r) => (
                            <option key={r.resourceId} value={r.resourceId}>
                                {r.name} ({r.subscriptionName})
                            </option>
                        ))}
                    </select>
                    <button
                        type="button"
                        onClick={() => resources.refetch()}
                        disabled={resources.isLoading}
                        className="rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
                        data-testid="observability-refresh"
                    >
                        {resources.isLoading ? "Loading…" : "Refresh"}
                    </button>
                </div>

                {!selectedResource && (
                    <>
                        <DraftInput
                            type="text"
                            value={config?.selectedResourceId ?? ""}
                            onCommit={handleManualId}
                            className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                            placeholder="Resource ID (/subscriptions/.../components/your-app-insights)"
                            data-testid="observability-resource-id"
                        />
                        <DraftInput
                            type="text"
                            value={config?.selectedResourceName ?? ""}
                            onCommit={handleManualName}
                            className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                            placeholder="Display name (optional, e.g. Prod App Insights)"
                            data-testid="observability-resource-name"
                        />
                    </>
                )}

                {resources.error && (
                    <div
                        className="text-xs text-destructive"
                        data-testid="observability-error"
                    >
                        {resources.error.message.includes("401") ||
                        resources.error.message
                            .toLowerCase()
                            .includes("unauthorized")
                            ? "Azure credentials not found. Run az login to discover resources."
                            : resources.error.message}
                    </div>
                )}
            </div>
        </section>
    );
}

/** Fields shown for an ACP (external agent subprocess) profile — the provider spawns a child
 * process speaking JSON-RPC over stdio instead of calling an HTTP endpoint, so this swaps the
 * base-url/model fields for spawn configuration. */
function AcpProfileFields({
    profile,
    profileId,
    onUpdate,
    onDraft,
}: {
    profile: AgentProfile;
    profileId: string;
    onUpdate: (patch: Partial<AgentProfile>) => void;
    onDraft: (patch: Partial<AgentProfile>) => void;
}) {
    return (
        <>
            <select
                value=""
                onChange={(e) => {
                    const preset = ACP_PRESETS[parseInt(e.target.value)];
                    if (preset) onUpdate(preset.patch);
                }}
                className="w-full rounded-md border bg-card px-2 py-1.5 text-sm"
                data-testid={`agent-profile-acp-preset-${profileId}`}
            >
                <option value="">Load a preset…</option>
                {ACP_PRESETS.map((preset, pi) => (
                    <option key={preset.label} value={pi}>
                        {preset.label}
                    </option>
                ))}
            </select>
            <DraftInput
                type="text"
                value={profile.command}
                onCommit={(v) => onUpdate({ command: v })}
                onDraftChange={(v) => onDraft({ command: v })}
                className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                placeholder="Command (e.g. npx, gemini, claude-agent-acp)"
                data-testid={`agent-profile-acp-command-${profileId}`}
            />
            <DraftInput
                type="text"
                value={profile.arguments}
                onCommit={(v) => onUpdate({ arguments: v })}
                onDraftChange={(v) => onDraft({ arguments: v })}
                className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                placeholder="Arguments (e.g. -y @agentclientprotocol/claude-agent-acp)"
                data-testid={`agent-profile-acp-arguments-${profileId}`}
            />
            <DraftInput
                type="text"
                value={profile.workingDirectory}
                onCommit={(v) => onUpdate({ workingDirectory: v })}
                onDraftChange={(v) => onDraft({ workingDirectory: v })}
                className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                placeholder="Working directory (optional — defaults to the app's)"
            />
            <div className="flex gap-3">
                <div className="flex-1">
                    <DraftInput
                        type="text"
                        value={profile.credentialKey}
                        onCommit={(v) => onUpdate({ credentialKey: v })}
                        onDraftChange={(v) => onDraft({ credentialKey: v })}
                        className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                        placeholder="Credential key (optional)"
                    />
                </div>
                <div className="flex-1">
                    <DraftInput
                        type="text"
                        value={profile.credentialEnvVar}
                        onCommit={(v) => onUpdate({ credentialEnvVar: v })}
                        onDraftChange={(v) => onDraft({ credentialEnvVar: v })}
                        className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                        placeholder="Injected as env var (e.g. ANTHROPIC_API_KEY)"
                    />
                </div>
            </div>
            <p className="text-xs text-muted-foreground">
                Most agents authenticate through their own CLI login — the
                credential pair above is only needed to inject an API key the
                agent doesn't already have.
            </p>
            <EnvVarsEditor
                value={profile.environmentVariables}
                onCommit={(env) => onUpdate({ environmentVariables: env })}
                testId={`agent-profile-acp-env-${profileId}`}
            />
            <label className="flex items-center gap-2 text-sm">
                <input
                    type="checkbox"
                    checked={profile.requireToolApproval}
                    onChange={(e) =>
                        onUpdate({ requireToolApproval: e.target.checked })
                    }
                    data-testid={`agent-profile-acp-approval-${profileId}`}
                />
                Ask before the agent runs a tool call
            </label>
            <p className="text-xs text-muted-foreground">
                Off by default — the agent's permission prompts are
                auto-approved. SwebKit's own mutating tools still only create
                proposals you confirm separately. Filesystem and terminal access
                stay disabled either way.
            </p>
        </>
    );
}

/** Edits a Record<string,string> as KEY=VALUE lines — one per line, `#` lines ignored. */
function EnvVarsEditor({
    value,
    onCommit,
    testId,
}: {
    value: Record<string, string>;
    onCommit: (env: Record<string, string>) => void;
    testId: string;
}) {
    const [draft, setDraft] = useState(() => serializeEnvVars(value));

    return (
        <textarea
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            onBlur={() => onCommit(parseEnvVars(draft))}
            rows={2}
            placeholder={"Extra env vars, one per line:\nKEY=VALUE"}
            className="w-full rounded-md border bg-card px-3 py-1.5 font-mono text-xs"
            data-testid={testId}
        />
    );
}

export function serializeEnvVars(env: Record<string, string>): string {
    return Object.entries(env)
        .map(([k, v]) => `${k}=${v}`)
        .join("\n");
}

export function parseEnvVars(text: string): Record<string, string> {
    const env: Record<string, string> = {};
    for (const line of text.split("\n")) {
        const trimmed = line.trim();
        if (!trimmed || trimmed.startsWith("#")) continue;
        const eq = trimmed.indexOf("=");
        if (eq <= 0) continue;
        env[trimmed.slice(0, eq).trim()] = trimmed.slice(eq + 1);
    }
    return env;
}
