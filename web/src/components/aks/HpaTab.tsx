import { useAksHpas } from "@/lib/hooks";

export function HpaTab({ ns, isMulti }: { ns: string; isMulti?: boolean }) {
  const { data: hpas, isLoading } = useAksHpas(ns);

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!hpas || hpas.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No HPAs found</div>;

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            {isMulti && <th className="py-2 pr-4">Namespace</th>}
            <th className="py-2 pr-4">Target</th>
            <th className="py-2 pr-4">Min</th>
            <th className="py-2 pr-4">Max</th>
            <th className="py-2 pr-4">Current</th>
            <th className="py-2 pr-4">Desired</th>
            <th className="py-2 pr-4">CPU%</th>
            <th className="py-2 pr-4">Type</th>
          </tr>
        </thead>
        <tbody data-testid="hpas-table-body">
          {hpas.map((hpa) => (
            <tr key={`${hpa.namespace}/${hpa.name}`} data-testid={`hpa-row-${hpa.name}`} className="border-b last:border-0">
              <td className="py-2 pr-4 font-medium">{hpa.name}</td>
              {isMulti && <td className="py-2 pr-4 text-xs text-muted-foreground">{hpa.namespace}</td>}
              <td className="py-2 pr-4 text-xs text-muted-foreground">{hpa.targetKind}/{hpa.targetName}</td>
              <td className="py-2 pr-4">{hpa.minReplicas}</td>
              <td className="py-2 pr-4">{hpa.maxReplicas}</td>
              <td className="py-2 pr-4">{hpa.currentReplicas}</td>
              <td className="py-2 pr-4 text-green-500">{hpa.desiredReplicas}</td>
              <td className="py-2 pr-4">
                {hpa.currentCpuUtilizationPercent != null ? (
                  <span className={
                    hpa.targetCpuUtilizationPercent != null && hpa.currentCpuUtilizationPercent > hpa.targetCpuUtilizationPercent
                      ? "text-yellow-500" : "text-muted-foreground"
                  }>
                    {hpa.currentCpuUtilizationPercent}%
                  </span>
                ) : "—"}
              </td>
              <td className="py-2 pr-4">
                {hpa.isKedaManaged ? (
                  <span className="rounded bg-purple-500/20 px-1.5 py-0.5 text-xs text-purple-500">KEDA</span>
                ) : (
                  <span className="text-xs text-muted-foreground">HPA</span>
                )}
                {hpa.isScalingDisabled && (
                  <span className="ml-1 rounded bg-red-500/20 px-1.5 py-0.5 text-xs text-red-500">Disabled</span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
