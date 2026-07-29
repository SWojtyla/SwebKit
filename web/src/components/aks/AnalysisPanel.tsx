import { useAksIngresses, useAksConfigMaps } from "@/lib/hooks";

interface Props { ns: string; }

export function AnalysisPanel({ ns }: Props) {
  const ingresses = useAksIngresses(ns);
  const configMaps = useAksConfigMaps(ns);

  return (
    <div className="space-y-6 p-4" data-testid="aks-analysis-panel">
      {/* Ingress Analysis */}
      <div>
        <h3 className="mb-2 text-sm font-semibold">Ingress Analysis</h3>
        <div className="rounded-md border overflow-hidden" data-testid="aks-ingress-analysis">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Name</th>
                <th className="px-3 py-2 text-left">Hosts</th>
                <th className="px-3 py-2 text-left">Paths</th>
                <th className="px-3 py-2 text-left">TLS</th>
                <th className="px-3 py-2 text-left">Backend</th>
              </tr>
            </thead>
            <tbody>
              {ingresses.data?.map((ing) => {
                const rules = ing.rules ?? [];
                const hosts = rules.map((r) => r.host).filter(Boolean).join(", ") || "*";
                const paths = rules.flatMap((r) => r.paths ?? []).map((p) => p.path).join(", ") || "/";
                const hasTls = ing.addresses.length > 0;
                const backend = rules.flatMap((r) => r.paths ?? []).map((p) => p.serviceName ? `${p.serviceName}:${p.servicePort}` : "").filter(Boolean).join(", ");
                return (
                  <tr key={ing.name} className="border-b last:border-0">
                    <td className="px-3 py-2 font-mono text-xs">{ing.name}</td>
                    <td className="px-3 py-2 text-xs">{hosts}</td>
                    <td className="px-3 py-2 text-xs">{paths}</td>
                    <td className="px-3 py-2 text-xs">
                      <span className={hasTls ? "text-green-500" : "text-yellow-500"}>
                        {hasTls ? "Yes" : "No"}
                      </span>
                    </td>
                    <td className="px-3 py-2 font-mono text-xs text-muted-foreground">{backend || "-"}</td>
                  </tr>
                );
              })}
              {(!ingresses.data || ingresses.data.length === 0) && (
                <tr><td colSpan={5} className="px-3 py-4 text-center text-muted-foreground">No ingresses found</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Probe Analysis */}
      <div>
        <h3 className="mb-2 text-sm font-semibold">Probe Analysis (ConfigMaps with probe configs)</h3>
        <div className="rounded-md border overflow-hidden" data-testid="aks-probe-analysis">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">ConfigMap</th>
                <th className="px-3 py-2 text-left">Keys</th>
                <th className="px-3 py-2 text-left">Data Size</th>
              </tr>
            </thead>
            <tbody>
              {configMaps.data?.map((cm) => {
                const keyCount = Object.keys(cm.data ?? {}).length;
                const dataSize = Object.values(cm.data ?? {}).join("").length;
                return (
                  <tr key={cm.name} className="border-b last:border-0">
                    <td className="px-3 py-2 font-mono text-xs">{cm.name}</td>
                    <td className="px-3 py-2 text-xs">{keyCount}</td>
                    <td className="px-3 py-2 text-xs text-muted-foreground">{dataSize} chars</td>
                  </tr>
                );
              })}
              {(!configMaps.data || configMaps.data.length === 0) && (
                <tr><td colSpan={3} className="px-3 py-4 text-center text-muted-foreground">No configmaps found</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Resource Quotas (from namespace events) */}
      <div>
        <h3 className="mb-2 text-sm font-semibold">Resource Quota Summary</h3>
        <div className="rounded-md border p-4 text-sm text-muted-foreground" data-testid="aks-quota-summary">
          <p>Resource quotas are derived from namespace annotations and limitRanges. No explicit quota data available from the current API. Check namespace events for quota-related warnings.</p>
        </div>
      </div>

      {/* Network Policy Summary */}
      <div>
        <h3 className="mb-2 text-sm font-semibold">Network Policy Summary</h3>
        <div className="rounded-md border p-4 text-sm text-muted-foreground" data-testid="aks-network-policy-summary">
          <p>Network policies are not exposed via the current sidecar API. Use <code className="rounded bg-muted px-1">kubectl get networkpolicies -n {ns}</code> for details.</p>
        </div>
      </div>
    </div>
  );
}
