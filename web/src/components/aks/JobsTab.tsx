import { useCallback, type MouseEvent } from "react";
import { useAksJobs } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { JobInfo } from "@/lib/types";

interface JobsTabProps {
  ns: string;
  isMulti?: boolean;
}

const columns: Column<JobInfo>[] = [
  { header: "Status", cell: (job) => (
    <span className={
      job.status === "Completed" ? "text-success" :
      job.status === "Failed" ? "text-destructive" :
      "text-warning"
    }>
      {job.status}
    </span>
  )},
  { header: "Active", cell: (job) => job.active },
  { header: "Succeeded", cell: (job) => <span className="text-success">{job.succeeded}</span> },
  { header: "Failed", cell: (job) => <span className="text-destructive">{job.failed}</span> },
  { header: "Completions", cell: (job) => <span className="text-muted-foreground">{job.desiredCompletions ?? "—"}</span> },
  { header: "Source", cell: (job) => (
    <span className="text-xs text-muted-foreground">
      {job.sourceKind && job.sourceName ? `${job.sourceKind}/${job.sourceName}` : "—"}
    </span>
  )},
];

export function JobsTab({ ns, isMulti }: JobsTabProps) {
  const { data: jobs, isLoading } = useAksJobs(ns);
  const ws = useAksWorkspace();

  const buildMenu = useCallback((job: JobInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(job.name) },
    { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("job", job.name, job.namespace) },
  ], [ws]);

  const handleRowContextMenu = useCallback(
    (e: MouseEvent<HTMLTableRowElement>, job: JobInfo) => ws.showContextMenu(e, buildMenu(job)),
    [ws, buildMenu],
  );

  return (
    <ResourceTable
      data={jobs}
      isLoading={isLoading}
      isMulti={isMulti}
      testIdPrefix="job"
      tableBodyTestId="jobs-table-body"
      emptyMessage="No jobs found"
      onRowContextMenu={handleRowContextMenu}
      columns={columns}
    />
  );
}
