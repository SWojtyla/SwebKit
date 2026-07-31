import { useAksJobs } from "@/lib/hooks";
import { ResourceTable } from "./shared/ResourceTable";
import { useAksWorkspace } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { JobInfo } from "@/lib/types";

interface JobsTabProps {
  ns: string;
  isMulti?: boolean;
}

export function JobsTab({ ns, isMulti }: JobsTabProps) {
  const { data: jobs, isLoading } = useAksJobs(ns);
  const ws = useAksWorkspace();

  const buildMenu = (job: JobInfo): ContextMenuItem[] => [
    { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(job.name) },
    { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml("job", job.name, job.namespace) },
  ];

  return (
    <ResourceTable
      data={jobs}
      isLoading={isLoading}
      isMulti={isMulti}
      testIdPrefix="job"
      tableBodyTestId="jobs-table-body"
      emptyMessage="No jobs found"
      onRowContextMenu={(e, job) => ws.showContextMenu(e, buildMenu(job))}
      columns={[
        { header: "Status", cell: (job) => (
          <span className={
            job.status === "Completed" ? "text-green-500" :
            job.status === "Failed" ? "text-red-500" :
            "text-yellow-500"
          }>
            {job.status}
          </span>
        )},
        { header: "Active", cell: (job) => job.active },
        { header: "Succeeded", cell: (job) => <span className="text-green-500">{job.succeeded}</span> },
        { header: "Failed", cell: (job) => <span className="text-red-500">{job.failed}</span> },
        { header: "Completions", cell: (job) => <span className="text-muted-foreground">{job.desiredCompletions ?? "—"}</span> },
        { header: "Source", cell: (job) => (
          <span className="text-xs text-muted-foreground">
            {job.sourceKind && job.sourceName ? `${job.sourceKind}/${job.sourceName}` : "—"}
          </span>
        )},
      ]}
    />
  );
}
