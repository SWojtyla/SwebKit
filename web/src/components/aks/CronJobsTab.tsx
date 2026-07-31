import { useAksCronJobs, useAksSuspendCronJob } from "@/lib/hooks";
import { ResourceTable } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { CronJobInfo } from "@/lib/types";

interface CronJobsTabProps {
  ns: string;
  isMulti?: boolean;
}

export function CronJobsTab({ ns, isMulti }: CronJobsTabProps) {
  const { data: cronjobs, isLoading } = useAksCronJobs(ns);
  const ws = useAksWorkspace();
  const suspendMutation = useAksSuspendCronJob();

  const buildMenu = (cj: CronJobInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(cj.name) },
    { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("cronjob", cj.name, cj.namespace) },
    { label: "Trigger", icon: "▶", onClick: () => {}, disabled: true },
  ];

  const toggle = (cj: CronJobInfo) => {
    const next = !cj.suspend;
    const action = next ? "suspend" : "resume";
    ws.requestConfirm({
      message: `${action === "suspend" ? "Suspend" : "Resume"} cronjob "${cj.name}"?`,
      resourceName: cj.name,
      onConfirm: () => suspendMutation.mutate({ ns: cj.namespace, name: cj.name, suspend: next }),
    });
  };

  return (
    <ResourceTable
      data={cronjobs}
      isLoading={isLoading}
      isMulti={isMulti}
      testIdPrefix="cronjob"
      tableBodyTestId="cronjobs-table-body"
      emptyMessage="No cron jobs found"
      onRowContextMenu={(e, cj) => ws.showContextMenu(e, buildMenu(cj))}
      columns={[
        { header: "Schedule", cell: (cj) => <span className="font-mono text-xs">{cj.schedule ?? "—"}</span> },
        { header: "Suspend", cell: (cj) => (
          cj.suspend ? <span className="text-yellow-500">Yes</span> : <span className="text-green-500">No</span>
        )},
        { header: "Active", cell: (cj) => cj.activeCount },
        { header: "Last Schedule", cell: (cj) => (
          <span className="text-xs text-muted-foreground">{cj.lastScheduleTime ? new Date(cj.lastScheduleTime).toLocaleString() : "—"}</span>
        )},
        { header: "Last Success", cell: (cj) => (
          <span className="text-xs text-muted-foreground">{cj.lastSuccessfulTime ? new Date(cj.lastSuccessfulTime).toLocaleString() : "—"}</span>
        )},
        { header: "Actions", cell: (cj) => (
          <button
            onClick={() => toggle(cj)}
            disabled={suspendMutation.isPending}
            className="rounded border border-border px-2 py-1 text-xs hover:bg-accent/50"
          >
            {cj.suspend ? "Resume" : "Suspend"}
          </button>
        )},
      ]}
    />
  );
}
