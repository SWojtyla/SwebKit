import { useAksJobs } from "@/lib/hooks";
import type { JobInfo } from "@/lib/types";

interface JobsTabProps {
  ns: string;
  isMulti?: boolean;
  onContextMenu?: (e: React.MouseEvent, job: JobInfo) => void;
}

export function JobsTab({ ns, isMulti, onContextMenu }: JobsTabProps) {
  const { data: jobs, isLoading } = useAksJobs(ns);

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!jobs || jobs.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No jobs found</div>;

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            {isMulti && <th className="py-2 pr-4">Namespace</th>}
            <th className="py-2 pr-4">Status</th>
            <th className="py-2 pr-4">Active</th>
            <th className="py-2 pr-4">Succeeded</th>
            <th className="py-2 pr-4">Failed</th>
            <th className="py-2 pr-4">Completions</th>
            <th className="py-2 pr-4">Source</th>
          </tr>
        </thead>
        <tbody data-testid="jobs-table-body">
          {jobs.map((job) => (
            <tr key={`${job.namespace}/${job.name}`} data-testid={`job-row-${job.name}`} className="border-b last:border-0 hover:bg-accent/30" onContextMenu={(e) => onContextMenu?.(e, job)}>
              <td className="py-2 pr-4 font-medium">{job.name}</td>
              {isMulti && <td className="py-2 pr-4 text-xs text-muted-foreground">{job.namespace}</td>}
              <td className="py-2 pr-4">
                <span className={
                  job.status === "Completed" ? "text-green-500" :
                  job.status === "Failed" ? "text-red-500" :
                  "text-yellow-500"
                }>
                  {job.status}
                </span>
              </td>
              <td className="py-2 pr-4">{job.active}</td>
              <td className="py-2 pr-4 text-green-500">{job.succeeded}</td>
              <td className="py-2 pr-4 text-red-500">{job.failed}</td>
              <td className="py-2 pr-4 text-muted-foreground">{job.desiredCompletions ?? "—"}</td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">
                {job.sourceKind && job.sourceName ? `${job.sourceKind}/${job.sourceName}` : "—"}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
