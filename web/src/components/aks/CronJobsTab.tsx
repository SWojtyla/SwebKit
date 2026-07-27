import { useAksCronJobs } from "@/lib/hooks";
import type { CronJobInfo } from "@/lib/types";

interface CronJobsTabProps {
  ns: string;
  isMulti?: boolean;
  onContextMenu?: (e: React.MouseEvent, cj: CronJobInfo) => void;
}

export function CronJobsTab({ ns, isMulti, onContextMenu }: CronJobsTabProps) {
  const { data: cronjobs, isLoading } = useAksCronJobs(ns);

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!cronjobs || cronjobs.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No cron jobs found</div>;

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            {isMulti && <th className="py-2 pr-4">Namespace</th>}
            <th className="py-2 pr-4">Schedule</th>
            <th className="py-2 pr-4">Suspend</th>
            <th className="py-2 pr-4">Active</th>
            <th className="py-2 pr-4">Last Schedule</th>
            <th className="py-2 pr-4">Last Success</th>
          </tr>
        </thead>
        <tbody data-testid="cronjobs-table-body">
          {cronjobs.map((cj) => (
            <tr key={`${cj.namespace}/${cj.name}`} data-testid={`cronjob-row-${cj.name}`} className="border-b last:border-0 hover:bg-accent/30" onContextMenu={(e) => onContextMenu?.(e, cj)}>
              <td className="py-2 pr-4 font-medium">{cj.name}</td>
              {isMulti && <td className="py-2 pr-4 text-xs text-muted-foreground">{cj.namespace}</td>}
              <td className="py-2 pr-4 font-mono text-xs">{cj.schedule ?? "—"}</td>
              <td className="py-2 pr-4">
                {cj.suspend ? <span className="text-yellow-500">Yes</span> : <span className="text-green-500">No</span>}
              </td>
              <td className="py-2 pr-4">{cj.activeCount}</td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">
                {cj.lastScheduleTime ? new Date(cj.lastScheduleTime).toLocaleString() : "—"}
              </td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">
                {cj.lastSuccessfulTime ? new Date(cj.lastSuccessfulTime).toLocaleString() : "—"}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
