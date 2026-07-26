import { useAksStatefulSets } from "@/lib/hooks";
import type { StatefulSetInfo } from "@/lib/types";

interface StatefulSetsTabProps {
  ns: string;
  onContextMenu?: (e: React.MouseEvent, sts: StatefulSetInfo) => void;
}

export function StatefulSetsTab({ ns, onContextMenu }: StatefulSetsTabProps) {
  const { data: statefulsets, isLoading } = useAksStatefulSets(ns);

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!statefulsets || statefulsets.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No stateful sets found</div>;

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            <th className="py-2 pr-4">Ready</th>
            <th className="py-2 pr-4">Current Rev</th>
            <th className="py-2 pr-4">Update Rev</th>
          </tr>
        </thead>
        <tbody data-testid="statefulsets-table-body">
          {statefulsets.map((sts) => (
            <tr key={sts.name} data-testid={`statefulset-row-${sts.name}`} className="border-b last:border-0 hover:bg-accent/30" onContextMenu={(e) => onContextMenu?.(e, sts)}>
              <td className="py-2 pr-4 font-medium">{sts.name}</td>
              <td className="py-2 pr-4">
                <span className={sts.readyReplicas === sts.replicas ? "text-green-500" : "text-yellow-500"}>
                  {sts.readyReplicas}/{sts.replicas}
                </span>
              </td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">{sts.currentRevision ?? "—"}</td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">{sts.updateRevision ?? "—"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
