import { useMemo, useState } from "react";
import { Trash2, Plus, RefreshCw } from "lucide-react";
import {
  useDevOpsConfig,
  useUpdateDevOpsConfig,
  usePatKeys,
  useSavePat,
  useDeletePat,
  useTestDevOpsConnection,
} from "@/lib/hooks";
import type { DevOpsConfig, ReleaseGroup, ReleaseGroupComponent, MergeStrategy } from "@/lib/types";

const emptyAliases = (): Record<string, string> => ({
  TST: "TST",
  STG: "STG",
  PRD: "PRD",
});

const defaultConfig: DevOpsConfig = {
  organization: "",
  authenticationMode: "Pat",
  patCredentialKey: "",
  pinnedProjects: [],
  pipelineGroups: [],
  releaseGroups: [],
  defaultStageAliases: emptyAliases(),
};

const emptyGroup = (): ReleaseGroup => ({
  id: crypto.randomUUID(),
  name: "",
  description: null,
  defaultMergeStrategy: "MergeCommit",
  stageAliases: emptyAliases(),
  components: [],
});

const emptyComponent = (): ReleaseGroupComponent => ({
  projectName: "",
  repositoryId: "",
  repositoryName: "",
  sourceBranch: "development",
  targetBranch: "main",
  pipelineId: 0,
  pipelineName: null,
  mergeStrategy: "MergeCommit",
  stageAliases: emptyAliases(),
  versionPrefix: null,
});

const mergeStrategies: MergeStrategy[] = ["FastForward", "MergeCommit", "Squash", "Rebase"];

export function DevOpsSettings() {
  const config = useDevOpsConfig();
  const updateConfig = useUpdateDevOpsConfig();
  const { data: patKeys, refetch: refetchPatKeys } = usePatKeys();
  const savePat = useSavePat();
  const deletePat = useDeletePat();
  const testConnection = useTestDevOpsConnection();

  const [draft, setDraft] = useState<DevOpsConfig | undefined>(config ?? undefined);
  const [patName, setPatName] = useState("");
  const [patValue, setPatValue] = useState("");

  // Re-sync when the saved config arrives or changes outside this tab.
  const working = useMemo(() => draft ?? config ?? defaultConfig, [config, draft]);

  const update = (patch: Partial<DevOpsConfig>) => setDraft((prev) => ({ ...(prev ?? config ?? defaultConfig), ...patch }));

  const updateAlias = (key: string, value: string) => {
    setDraft((prev) => {
      const current = prev ?? config ?? defaultConfig;
      return { ...current, defaultStageAliases: { ...current.defaultStageAliases, [key]: value } };
    });
  };

  const updateGroup = (id: string, patch: Partial<ReleaseGroup>) => {
    setDraft((prev) => {
      const current = prev ?? config ?? defaultConfig;
      return { ...current, releaseGroups: current.releaseGroups.map((g) => (g.id === id ? { ...g, ...patch } : g)) };
    });
  };

  const updateGroupAlias = (groupId: string, key: string, value: string) => {
    setDraft((prev) => {
      const current = prev ?? config ?? defaultConfig;
      return {
        ...current,
        releaseGroups: current.releaseGroups.map((g) =>
          g.id === groupId ? { ...g, stageAliases: { ...g.stageAliases, [key]: value } } : g
        ),
      };
    });
  };

  const addGroup = () =>
    setDraft((prev) => {
      const current = prev ?? config ?? defaultConfig;
      return { ...current, releaseGroups: [...current.releaseGroups, emptyGroup()] };
    });

  const removeGroup = (id: string) =>
    setDraft((prev) => {
      const current = prev ?? config ?? defaultConfig;
      return { ...current, releaseGroups: current.releaseGroups.filter((g) => g.id !== id) };
    });

  const addComponent = (groupId: string) => {
    setDraft((prev) => {
      const current = prev ?? config ?? defaultConfig;
      return {
        ...current,
        releaseGroups: current.releaseGroups.map((g) =>
          g.id === groupId ? { ...g, components: [...g.components, emptyComponent()] } : g
        ),
      };
    });
  };

  const updateComponent = (groupId: string, index: number, patch: Partial<ReleaseGroupComponent>) => {
    setDraft((prev) => {
      const current = prev ?? config ?? defaultConfig;
      return {
        ...current,
        releaseGroups: current.releaseGroups.map((g) =>
          g.id === groupId
            ? { ...g, components: g.components.map((c, i) => (i === index ? { ...c, ...patch } : c)) }
            : g
        ),
      };
    });
  };

  const removeComponent = (groupId: string, index: number) => {
    setDraft((prev) => {
      const current = prev ?? config ?? defaultConfig;
      return {
        ...current,
        releaseGroups: current.releaseGroups.map((g) =>
          g.id === groupId ? { ...g, components: g.components.filter((_, i) => i !== index) } : g
        ),
      };
    });
  };

  const handleSavePat = () => {
    if (!patName.trim() || !patValue.trim()) return;
    savePat.mutate(
      { key: patName.trim(), pat: patValue.trim() },
      {
        onSuccess: () => {
          setPatValue("");
          setPatName("");
          void refetchPatKeys();
        },
      },
    );
  };

  const handleSaveConfig = () => {
    updateConfig.mutate(working, {
      onSuccess: () => setDraft(undefined),
    });
  };

  return (
    <div className="space-y-8" data-testid="devops-settings">
      <section>
        <h2 className="mb-3 text-lg font-semibold">Azure DevOps connection</h2>
        <div className="grid gap-4 md:grid-cols-2">
          <div>
            <label className="mb-1 block text-sm font-medium">Organization</label>
            <input
              type="text"
              value={working.organization}
              onChange={(e) => update({ organization: e.target.value })}
              placeholder="dev.azure.com/{org}"
              className="w-full rounded border bg-background px-2 py-1.5 text-sm"
              data-testid="devops-organization"
            />
          </div>

          <div>
            <label className="mb-1 block text-sm font-medium">Authentication mode</label>
            <select
              value={working.authenticationMode}
              onChange={(e) => update({ authenticationMode: e.target.value as DevOpsConfig["authenticationMode"] })}
              className="w-full rounded border bg-background px-2 py-1.5 text-sm"
              data-testid="devops-auth-mode"
            >
              <option value="Pat">Personal Access Token (PAT)</option>
              <option value="Entra">Microsoft Entra (interactive)</option>
            </select>
          </div>
        </div>

        {working.authenticationMode === "Pat" && (
          <div className="mt-4 space-y-3 rounded border p-3">
            <h3 className="text-sm font-medium">PAT credentials</h3>
            <div className="flex gap-2">
              <input
                type="text"
                value={patName}
                onChange={(e) => setPatName(e.target.value)}
                placeholder="Key name (e.g. default)"
                className="w-40 rounded border bg-background px-2 py-1.5 text-sm"
                data-testid="pat-key-name"
              />
              <input
                type="password"
                value={patValue}
                onChange={(e) => setPatValue(e.target.value)}
                placeholder="Paste PAT here — it is never returned to the UI"
                className="flex-1 rounded border bg-background px-2 py-1.5 text-sm"
                data-testid="pat-value"
              />
              <button
                onClick={handleSavePat}
                disabled={savePat.isPending || !patName.trim() || !patValue.trim()}
                className="rounded border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
                data-testid="pat-save"
              >
                Save PAT
              </button>
            </div>

            <div className="space-y-1">
              <label className="block text-sm font-medium">Active PAT key</label>
              <select
                value={working.patCredentialKey}
                onChange={(e) => update({ patCredentialKey: e.target.value })}
                className="w-full rounded border bg-background px-2 py-1.5 text-sm md:w-72"
                data-testid="pat-active-key"
              >
                <option value="">Select a saved PAT key</option>
                {(patKeys ?? []).map((k) => (
                  <option key={k} value={k}>
                    {k}
                  </option>
                ))}
              </select>
              {working.patCredentialKey && (
                <button
                  onClick={() =>
                    deletePat.mutate(working.patCredentialKey, { onSuccess: () => update({ patCredentialKey: "" }) })
                  }
                  disabled={deletePat.isPending}
                  className="mt-1 flex items-center gap-1 text-xs text-destructive hover:underline"
                  data-testid="pat-delete"
                >
                  <Trash2 className="h-3 w-3" /> Remove selected key
                </button>
              )}
            </div>
          </div>
        )}

        <div className="mt-4 flex gap-2">
          <button
            onClick={() => testConnection.mutate()}
            disabled={testConnection.isPending}
            className="flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
            data-testid="devops-test-connection"
          >
            <RefreshCw className={`h-4 w-4 ${testConnection.isPending ? "animate-spin" : ""}`} />
            Test connection
          </button>
          <button
            onClick={handleSaveConfig}
            disabled={updateConfig.isPending}
            className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
            data-testid="devops-save"
          >
            Save DevOps settings
          </button>
        </div>
      </section>

      <section>
        <h2 className="mb-3 text-lg font-semibold">Default stage aliases</h2>
        <p className="mb-3 text-sm text-muted-foreground">
          Map semantic slots (TST/STG/PRD) to the ADO stage/environment names used by your pipelines.
        </p>
        <div className="grid gap-3 sm:grid-cols-3">
          {Object.entries(working.defaultStageAliases).map(([slot, alias]) => (
            <div key={slot}>
              <label className="mb-1 block text-sm font-medium">{slot}</label>
              <input
                type="text"
                value={alias}
                onChange={(e) => updateAlias(slot, e.target.value)}
                className="w-full rounded border bg-background px-2 py-1.5 text-sm"
                data-testid={`devops-alias-${slot}`}
              />
            </div>
          ))}
        </div>
      </section>

      <section>
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-lg font-semibold">Release groups</h2>
          <button
            onClick={addGroup}
            className="flex items-center gap-1 rounded-md border px-2 py-1 text-sm hover:bg-accent"
            data-testid="devops-add-group"
          >
            <Plus className="h-4 w-4" /> Add group
          </button>
        </div>

        <div className="space-y-4">
          {working.releaseGroups.map((group) => (
            <div key={group.id} className="rounded border p-3">
              <div className="mb-3 grid gap-3 md:grid-cols-3">
                <input
                  type="text"
                  value={group.name}
                  onChange={(e) => updateGroup(group.id, { name: e.target.value })}
                  placeholder="Group name"
                  className="rounded border bg-background px-2 py-1.5 text-sm"
                  data-testid={`group-name-${group.id}`}
                />
                <input
                  type="text"
                  value={group.description ?? ""}
                  onChange={(e) => updateGroup(group.id, { description: e.target.value || null })}
                  placeholder="Description"
                  className="rounded border bg-background px-2 py-1.5 text-sm"
                  data-testid={`group-desc-${group.id}`}
                />
                <select
                  value={group.defaultMergeStrategy}
                  onChange={(e) => updateGroup(group.id, { defaultMergeStrategy: e.target.value as MergeStrategy })}
                  className="rounded border bg-background px-2 py-1.5 text-sm"
                  data-testid={`group-strategy-${group.id}`}
                >
                  {mergeStrategies.map((s) => (
                    <option key={s} value={s}>
                      {s}
                    </option>
                  ))}
                </select>
              </div>

              <div className="mb-3 grid gap-2 sm:grid-cols-3">
                {Object.entries(group.stageAliases).map(([slot, alias]) => (
                  <div key={slot}>
                    <label className="text-xs text-muted-foreground">{slot} alias</label>
                    <input
                      type="text"
                      value={alias}
                      onChange={(e) => updateGroupAlias(group.id, slot, e.target.value)}
                      className="w-full rounded border bg-background px-2 py-1 text-sm"
                      data-testid={`group-alias-${group.id}-${slot}`}
                    />
                  </div>
                ))}
              </div>

              <h4 className="mb-2 text-sm font-medium">Components</h4>
              <div className="space-y-2">
                {group.components.map((component, idx) => (
                  <div key={idx} className="grid gap-2 rounded bg-muted/40 p-2 sm:grid-cols-7">
                    <input
                      type="text"
                      value={component.projectName}
                      onChange={(e) => updateComponent(group.id, idx, { projectName: e.target.value })}
                      placeholder="Project"
                      className="rounded border bg-background px-2 py-1 text-sm"
                      data-testid={`component-project-${group.id}-${idx}`}
                    />
                    <input
                      type="text"
                      value={component.repositoryName}
                      onChange={(e) => updateComponent(group.id, idx, { repositoryName: e.target.value })}
                      placeholder="Repo name"
                      className="rounded border bg-background px-2 py-1 text-sm"
                      data-testid={`component-repo-${group.id}-${idx}`}
                    />
                    <input
                      type="text"
                      value={component.repositoryId}
                      onChange={(e) => updateComponent(group.id, idx, { repositoryId: e.target.value })}
                      placeholder="Repo ID"
                      className="rounded border bg-background px-2 py-1 text-sm"
                      data-testid={`component-repo-id-${group.id}-${idx}`}
                    />
                    <input
                      type="text"
                      value={component.sourceBranch}
                      onChange={(e) => updateComponent(group.id, idx, { sourceBranch: e.target.value })}
                      placeholder="Source branch"
                      className="rounded border bg-background px-2 py-1 text-sm"
                      data-testid={`component-source-${group.id}-${idx}`}
                    />
                    <input
                      type="text"
                      value={component.targetBranch}
                      onChange={(e) => updateComponent(group.id, idx, { targetBranch: e.target.value })}
                      placeholder="Target branch"
                      className="rounded border bg-background px-2 py-1 text-sm"
                      data-testid={`component-target-${group.id}-${idx}`}
                    />
                    <input
                      type="number"
                      value={component.pipelineId || ""}
                      onChange={(e) => updateComponent(group.id, idx, { pipelineId: parseInt(e.target.value, 10) || 0 })}
                      placeholder="Pipeline ID"
                      className="rounded border bg-background px-2 py-1 text-sm"
                      data-testid={`component-pipeline-${group.id}-${idx}`}
                    />
                    <div className="flex items-center gap-1">
                      <input
                        type="text"
                        value={component.versionPrefix ?? ""}
                        onChange={(e) => updateComponent(group.id, idx, { versionPrefix: e.target.value || null })}
                        placeholder="Version prefix"
                        className="flex-1 rounded border bg-background px-2 py-1 text-sm"
                        data-testid={`component-prefix-${group.id}-${idx}`}
                      />
                      <button
                        onClick={() => removeComponent(group.id, idx)}
                        className="rounded p-1 text-destructive hover:bg-accent"
                        data-testid={`component-remove-${group.id}-${idx}`}
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </div>
                  </div>
                ))}
              </div>
              <button
                onClick={() => addComponent(group.id)}
                className="mt-2 flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground"
                data-testid={`group-add-component-${group.id}`}
              >
                <Plus className="h-3 w-3" /> Add component
              </button>

              <div className="mt-3 flex justify-end">
                <button
                  onClick={() => removeGroup(group.id)}
                  className="flex items-center gap-1 text-xs text-destructive hover:underline"
                  data-testid={`group-remove-${group.id}`}
                >
                  <Trash2 className="h-3 w-3" /> Remove group
                </button>
              </div>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}
