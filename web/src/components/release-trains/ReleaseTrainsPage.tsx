import { useEffect, useMemo, useState } from "react";
import {
  Train,
  RefreshCw,
  Play,
  Check,
  Trash2,
  Plus,
  AlertTriangle,
  Copy,
  CheckCircle2,
  XCircle,
  Clock,
  Bug,
} from "lucide-react";
import {
  useReleaseTrains,
  useReleaseTrain,
  useCreateReleaseTrain,
  usePreflightReleaseTrain,
  useExecuteReleaseTrain,
  useRefreshReleaseTrain,
  useCompleteReleaseTrain,
  useDeleteReleaseTrain,
  useAdvanceDemoReleaseTrain,
  useRetryReleaseTrain,
  useDriftReleaseTrain,
  useUpdateReleaseTrainRemarks,
  useDevOpsConfig,
  useProfile,
} from "@/lib/hooks";
import { useNotification } from "@/components/layout/NotificationSystem";
import {
  formatReleaseTrainMarkdown,
  formatReleaseTrainPlain,
  formatReleaseTrainRichTable,
} from "@/lib/releaseTrainFormatter";
import type { ReleaseGroup, ReleaseTrainRecord, ReleaseTrainComponent, ReleaseTrainStage } from "@/lib/types";

const stageSlots = ["TST", "STG", "PRD"] as const;

function stageForSlot(component: ReleaseTrainComponent, slot: string): ReleaseTrainStage | undefined {
  return component.stages.find((s) => s.slot === slot);
}

function stageBadge(stage: ReleaseTrainStage | undefined) {
  if (!stage) return <span className="text-muted-foreground">—</span>;
  const color =
    stage.state === "completed"
      ? stage.result === "succeeded"
        ? "text-success"
        : stage.result === "failed"
          ? "text-destructive"
          : "text-muted-foreground"
      : stage.state === "inProgress"
        ? "text-info"
        : stage.state === "cancelling"
          ? "text-warning"
          : "text-muted-foreground";
  const text = `${stage.stageName}: ${stage.state}${stage.result ? ` / ${stage.result}` : ""}`;
  return <span className={`text-xs ${color}`}>{text}</span>;
}

export function ReleaseTrainsPage() {
  const { data: trains = [], isLoading, refetch } = useReleaseTrains();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const { data: selectedTrain } = useReleaseTrain(selectedId);

  useEffect(() => {
    if (selectedId && !trains.some((t) => t.id === selectedId)) {
      setSelectedId(null);
    }
  }, [trains, selectedId]);

  return (
    <div className="flex h-full flex-col" data-testid="release-trains-page">
      <div className="border-b px-6 py-3">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Train className="h-5 w-5" />
            <h1 className="text-2xl font-bold" data-testid="release-trains-title">Release Trains</h1>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => refetch()}
              className="rounded-md border px-3 py-1.5 text-sm hover:bg-accent"
              data-testid="release-trains-refresh-list"
            >
              <RefreshCw className="mr-1 inline h-4 w-4" /> Refresh
            </button>
            <ReleaseTrainWizardDialog />
          </div>
        </div>
      </div>

      <div className="flex flex-1 overflow-hidden">
        <div className="w-72 border-r overflow-auto" data-testid="release-trains-list">
          {isLoading && <div className="p-4 text-sm text-muted-foreground">Loading...</div>}
          {trains.length === 0 && !isLoading && (
            <div className="p-4 text-sm text-muted-foreground" data-testid="release-trains-empty">
              No release trains yet. Start one with the + button above.
            </div>
          )}
          {trains.map((train) => (
            <button
              key={train.id}
              onClick={() => setSelectedId(train.id)}
              className={`flex w-full flex-col border-b px-4 py-3 text-left hover:bg-accent ${selectedId === train.id ? "bg-accent" : ""}`}
              data-testid={`release-train-item-${train.id}`}
            >
              <span className="truncate font-medium">{train.name}</span>
              <span className="text-xs text-muted-foreground">{train.label ?? train.status}</span>
              <div className="mt-1 flex items-center gap-1">
                <StatusIcon status={train.status} />
                <span className="text-xs capitalize">{train.status}</span>
              </div>
            </button>
          ))}
        </div>

        <div className="flex-1 overflow-auto p-6" data-testid="release-train-detail">
          {selectedTrain ? <ReleaseTrainDetail train={selectedTrain} /> : <div className="text-muted-foreground">Select a release train to view details.</div>}
        </div>
      </div>
    </div>
  );
}

function isRunningStatus(status: string) {
  return status === "CreatingTags" || status === "CreatingPullRequests" || status === "RunningPipelines";
}

function StatusIcon({ status }: { status: string }) {
  if (status === "Completed") return <CheckCircle2 className="h-4 w-4 text-success" />;
  if (status === "Failed" || status === "Cancelled") return <XCircle className="h-4 w-4 text-destructive" />;
  if (status === "Monitoring") return <Clock className="h-4 w-4 text-info" />;
  if (isRunningStatus(status)) return <RefreshCw className="h-4 w-4 animate-spin text-info" />;
  return <Clock className="h-4 w-4 text-muted-foreground" />;
}

function ReleaseTrainDetail({ train }: { train: ReleaseTrainRecord }) {
  const preflight = usePreflightReleaseTrain();
  const execute = useExecuteReleaseTrain();
  const refresh = useRefreshReleaseTrain();
  const complete = useCompleteReleaseTrain();
  const remove = useDeleteReleaseTrain();
  const advanceDemo = useAdvanceDemoReleaseTrain();
  const retry = useRetryReleaseTrain();
  const drift = useDriftReleaseTrain();
  const updateRemarks = useUpdateReleaseTrainRemarks();
  const { notify } = useNotification();
  const [demoComponent, setDemoComponent] = useState("");
  const [overallRemarks, setOverallRemarks] = useState(train.overallRemarks ?? "");

  useEffect(() => {
    setOverallRemarks(train.overallRemarks ?? "");
  }, [train.overallRemarks]);

  const [richTable, plain, markdown] = useMemo(
    () => [formatReleaseTrainRichTable(train), formatReleaseTrainPlain(train), formatReleaseTrainMarkdown(train)],
    [train],
  );

  const copy = (text: string, label: string) => {
    void navigator.clipboard.writeText(text);
    notify("success", `${label} copied to clipboard`);
  };

  const componentOptions = train.components.map((c) => c.componentName);

  const canRetry = train.components.some(
    (c) =>
      c.status === "Blocked" ||
      c.status === "Failed" ||
      c.status === "TstFailed" ||
      c.status === "StgFailed" ||
      c.status === "PrdFailed",
  );

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-xl font-semibold" data-testid="release-train-name">{train.name}</h2>
          {train.label && <p className="text-sm text-muted-foreground">{train.label}</p>}
          <div className="mt-2 flex items-center gap-2 text-sm">
            <StatusIcon status={train.status} />
            <span className="capitalize">{train.status}</span>
            <span className="text-muted-foreground">· {new Date(train.createdAt).toLocaleString()}</span>
          </div>
          {train.driftWarnings?.length ? (
            <div className="mt-2 flex items-center gap-2 rounded bg-warning/10 p-2 text-xs text-warning" data-testid="release-train-drift">
              <AlertTriangle className="h-4 w-4" />
              {train.driftWarnings.join("; ")}
            </div>
          ) : null}
        </div>

        <div className="flex flex-wrap gap-2">
          <button
            onClick={() => refresh.mutate(train.id)}
            disabled={refresh.isPending}
            className="rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
            data-testid="release-train-refresh"
          >
            <RefreshCw className={`mr-1 inline h-4 w-4 ${refresh.isPending ? "animate-spin" : ""}`} /> Refresh
          </button>
          <button
            onClick={() => preflight.mutate(train.id)}
            disabled={preflight.isPending}
            className="rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
            data-testid="release-train-preflight"
          >
            <Play className="mr-1 inline h-4 w-4" /> Preflight
          </button>
          <button
            onClick={() => execute.mutate(train.id)}
            disabled={execute.isPending || isRunningStatus(train.status)}
            className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
            data-testid="release-train-execute"
          >
            <Play className="mr-1 inline h-4 w-4" /> Execute
          </button>
          <button
            onClick={() => retry.mutate(train.id)}
            disabled={retry.isPending || !canRetry}
            className="rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
            data-testid="release-train-retry"
          >
            <RefreshCw className={`mr-1 inline h-4 w-4 ${retry.isPending ? "animate-spin" : ""}`} /> Retry
          </button>
          <button
            onClick={() => complete.mutate(train.id)}
            disabled={complete.isPending || train.status !== "Monitoring"}
            className="rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
            data-testid="release-train-complete"
          >
            <Check className="mr-1 inline h-4 w-4" /> Complete
          </button>
          <button
            onClick={() => remove.mutate(train.id)}
            disabled={remove.isPending}
            className="rounded-md border px-3 py-1.5 text-sm text-destructive hover:bg-accent disabled:opacity-50"
            data-testid="release-train-delete"
          >
            <Trash2 className="mr-1 inline h-4 w-4" /> Archive
          </button>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2 rounded border p-3 text-sm">
        <Bug className="h-4 w-4 text-muted-foreground" />
        <span className="text-muted-foreground">Demo component:</span>
        <select
          value={demoComponent}
          onChange={(e) => setDemoComponent(e.target.value)}
          className="rounded border bg-background px-2 py-1 text-sm"
          data-testid="demo-fail-component"
        >
          <option value="">No component selected</option>
          {componentOptions.map((c) => (
            <option key={c} value={c}>{c}</option>
          ))}
        </select>
        <button
          onClick={() => advanceDemo.mutate({ id: train.id, failComponent: demoComponent || undefined })}
          disabled={advanceDemo.isPending}
          className="rounded border px-3 py-1 text-sm hover:bg-accent disabled:opacity-50"
          data-testid="release-train-advance-demo"
        >
          Advance demo
        </button>
        <button
          onClick={() => drift.mutate({ id: train.id, componentName: demoComponent || undefined })}
          disabled={drift.isPending || !demoComponent}
          className="rounded border px-3 py-1 text-sm text-warning hover:bg-accent disabled:opacity-50"
          data-testid="release-train-drift"
        >
          <AlertTriangle className="mr-1 inline h-4 w-4" /> Inject drift
        </button>
      </div>

      <section>
        <h3 className="mb-3 text-sm font-semibold uppercase text-muted-foreground">Components</h3>
        <div className="overflow-x-auto rounded border">
          <table className="w-full text-sm">
            <thead className="bg-muted text-left">
              <tr>
                <th className="px-3 py-2">Component</th>
                <th className="px-3 py-2">Version</th>
                <th className="px-3 py-2">Tag</th>
                <th className="px-3 py-2">PR</th>
                <th className="px-3 py-2">TST</th>
                <th className="px-3 py-2">STG</th>
                <th className="px-3 py-2">PRD</th>
                <th className="px-3 py-2">Remarks</th>
              </tr>
            </thead>
            <tbody>
              {train.components.map((component) => (
                <tr key={component.id} className="border-t">
                  <td className="px-3 py-2 font-medium">{component.componentName}</td>
                  <td className="px-3 py-2">{component.version}</td>
                  <td className="px-3 py-2">{component.tagName ?? "—"}</td>
                  <td className="px-3 py-2">
                    {component.pullRequestUrl ? (
                      <a href={component.pullRequestUrl} target="_blank" rel="noreferrer" className="text-info hover:underline">{component.pullRequestId}</a>
                    ) : (
                      component.pullRequestId?.toString() ?? "—"
                    )}
                  </td>
                  {stageSlots.map((slot) => (
                    <td key={slot} className="px-3 py-2">{stageBadge(stageForSlot(component, slot))}</td>
                  ))}
                  <td className="px-3 py-2">
                    <input
                      type="text"
                      value={component.remarks ?? ""}
                      onChange={(e) => {
                        const next: Record<string, string> = { [component.componentName]: e.target.value };
                        updateRemarks.mutate({ id: train.id, componentRemarks: next });
                      }}
                      placeholder="Add remark"
                      className="w-32 rounded border bg-background px-2 py-1 text-xs"
                      data-testid={`component-remarks-${component.componentName}`}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section>
        <h3 className="mb-2 text-sm font-semibold uppercase text-muted-foreground">Overall remarks</h3>
        <div className="flex gap-2">
          <textarea
            value={overallRemarks}
            onChange={(e) => setOverallRemarks(e.target.value)}
            className="min-h-[80px] flex-1 rounded border bg-background px-3 py-2 text-sm"
            data-testid="release-train-overall-remarks"
          />
          <button
            onClick={() => updateRemarks.mutate({ id: train.id, overallRemarks })}
            disabled={updateRemarks.isPending}
            className="self-start rounded-md border px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
            data-testid="release-train-save-remarks"
          >
            Save
          </button>
        </div>
      </section>

      <section>
        <h3 className="mb-3 text-sm font-semibold uppercase text-muted-foreground">Handoff draft</h3>
        <div className="flex flex-wrap gap-2">
          <button onClick={() => copy(richTable, "Confluence table")} className="rounded-md border px-3 py-1.5 text-sm hover:bg-accent" data-testid="copy-rich-table">
            <Copy className="mr-1 inline h-4 w-4" /> Copy Confluence table
          </button>
          <button onClick={() => copy(plain, "Plain text")} className="rounded-md border px-3 py-1.5 text-sm hover:bg-accent" data-testid="copy-plain">
            <Copy className="mr-1 inline h-4 w-4" /> Copy plain text
          </button>
          <button onClick={() => copy(markdown, "Markdown")} className="rounded-md border px-3 py-1.5 text-sm hover:bg-accent" data-testid="copy-markdown">
            <Copy className="mr-1 inline h-4 w-4" /> Copy Markdown
          </button>
        </div>
      </section>
    </div>
  );
}

function ReleaseTrainWizardDialog() {
  const { data: profile } = useProfile();
  const config = useDevOpsConfig();
  const [open, setOpen] = useState(false);
  const [groupId, setGroupId] = useState("");
  const [name, setName] = useState("");
  const [label, setLabel] = useState("");
  const [overallRemarks, setOverallRemarks] = useState("");
  const [components, setComponents] = useState<{ componentName: string; version: string; remarks: string }[]>([]);
  const create = useCreateReleaseTrain();
  const { notify } = useNotification();

  const groups = config?.releaseGroups ?? [];
  const selectedGroup = groups.find((g) => g.id === groupId);

  const pickGroup = (id: string) => {
    setGroupId(id);
    const group = groups.find((g) => g.id === id);
    setComponents(
      group?.components.map((c) => ({ componentName: c.repositoryName || c.repositoryId, version: "", remarks: "" })) ?? [],
    );
  };

  const submit = () => {
    if (!profile || !groupId || !name.trim() || components.length === 0) {
      notify("error", "Please complete all required fields");
      return;
    }
    create.mutate(
      {
        profileId: profile?.config?.name ?? "default",
        groupId,
        name: name.trim(),
        label: label.trim() || null,
        overallRemarks: overallRemarks.trim() || null,
        components: components.map((c) => ({ ...c, version: c.version.trim(), remarks: c.remarks.trim() || null })),
      },
      {
        onSuccess: () => {
          setOpen(false);
          setGroupId("");
          setName("");
          setLabel("");
          setOverallRemarks("");
          setComponents([]);
        },
      },
    );
  };

  return (
    <>
      <button
        onClick={() => setOpen(true)}
        className="flex items-center gap-1 rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:bg-primary/90"
        data-testid="release-trains-new"
      >
        <Plus className="h-4 w-4" /> New train
      </button>
      {open && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4" data-testid="release-train-wizard">
          <div className="max-h-[90vh] w-full max-w-3xl overflow-auto rounded-lg bg-background p-6 shadow-lg">
            <h2 className="mb-4 text-lg font-semibold">Create release train</h2>

            <div className="grid gap-4 md:grid-cols-2">
              <div>
                <label className="mb-1 block text-sm font-medium">Release group</label>
                <select
                  value={groupId}
                  onChange={(e) => pickGroup(e.target.value)}
                  className="w-full rounded border bg-background px-2 py-1.5 text-sm"
                  data-testid="wizard-group"
                >
                  <option value="">Select a group</option>
                  {groups.map((g: ReleaseGroup) => (
                    <option key={g.id} value={g.id}>{g.name}</option>
                  ))}
                </select>
              </div>

              <div>
                <label className="mb-1 block text-sm font-medium">Train name</label>
                <input
                  type="text"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="e.g. Sprint 42 release"
                  className="w-full rounded border bg-background px-2 py-1.5 text-sm"
                  data-testid="wizard-name"
                />
              </div>

              <div>
                <label className="mb-1 block text-sm font-medium">Label</label>
                <input
                  type="text"
                  value={label}
                  onChange={(e) => setLabel(e.target.value)}
                  placeholder="release/2.4"
                  className="w-full rounded border bg-background px-2 py-1.5 text-sm"
                  data-testid="wizard-label"
                />
              </div>

              <div>
                <label className="mb-1 block text-sm font-medium">Overall remarks</label>
                <input
                  type="text"
                  value={overallRemarks}
                  onChange={(e) => setOverallRemarks(e.target.value)}
                  className="w-full rounded border bg-background px-2 py-1.5 text-sm"
                  data-testid="wizard-overall-remarks"
                />
              </div>
            </div>

            {selectedGroup && (
              <div className="mt-4 space-y-2">
                <h3 className="text-sm font-medium">Component versions</h3>
                {components.map((comp, idx) => (
                  <div key={idx} className="grid gap-2 sm:grid-cols-3">
                    <span className="self-center text-sm font-medium">{comp.componentName}</span>
                    <input
                      type="text"
                      value={comp.version}
                      onChange={(e) => setComponents((prev) => prev.map((c, i) => (i === idx ? { ...c, version: e.target.value } : c)))}
                      placeholder="Version (e.g. 2.4.0)"
                      className="rounded border bg-background px-2 py-1.5 text-sm"
                      data-testid={`wizard-version-${idx}`}
                    />
                    <input
                      type="text"
                      value={comp.remarks}
                      onChange={(e) => setComponents((prev) => prev.map((c, i) => (i === idx ? { ...c, remarks: e.target.value } : c)))}
                      placeholder="Remarks"
                      className="rounded border bg-background px-2 py-1.5 text-sm"
                      data-testid={`wizard-remarks-${idx}`}
                    />
                  </div>
                ))}
              </div>
            )}

            <div className="mt-6 flex justify-end gap-2">
              <button onClick={() => setOpen(false)} className="rounded-md border px-4 py-2 text-sm hover:bg-accent" data-testid="wizard-cancel">
                Cancel
              </button>
              <button
                onClick={submit}
                disabled={create.isPending || !groupId || !name.trim() || components.some((c) => !c.version.trim())}
                className="rounded-md bg-primary px-4 py-2 text-sm text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
                data-testid="wizard-create"
              >
                {create.isPending ? "Creating..." : "Create train"}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
