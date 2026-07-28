import { useAksGateways } from "@/lib/hooks";
import type { GatewayInfo } from "@/lib/types";

interface GatewaysTabProps {
  ns: string;
  isMulti?: boolean;
  onContextMenu?: (e: React.MouseEvent, gw: GatewayInfo) => void;
}

export function GatewaysTab({ ns, isMulti, onContextMenu }: GatewaysTabProps) {
  const { data: gateways, isLoading } = useAksGateways(ns);

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!gateways || gateways.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No gateways found</div>;

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            {isMulti && <th className="py-2 pr-4">Namespace</th>}
            <th className="py-2 pr-4">Class</th>
            <th className="py-2 pr-4">Status</th>
            <th className="py-2 pr-4">Addresses</th>
            <th className="py-2 pr-4">Attached Routes</th>
          </tr>
        </thead>
        <tbody data-testid="gateways-table-body">
          {gateways.map((gw) => (
            <tr
              key={`${gw.namespace}/${gw.name}`}
              data-testid={`gateway-row-${gw.name}`}
              className="border-b last:border-0 hover:bg-accent/30"
              onContextMenu={(e) => onContextMenu?.(e, gw)}
            >
              <td className="py-2 pr-4 font-medium">{gw.name}</td>
              {isMulti && <td className="py-2 pr-4 text-xs text-muted-foreground">{gw.namespace}</td>}
              <td className="py-2 pr-4 text-xs text-muted-foreground">{gw.gatewayClass ?? "—"}</td>
              <td className="py-2 pr-4">
                <span className={
                  gw.status === "Ready" ? "text-green-500" :
                  gw.status === "Pending" ? "text-yellow-500" :
                  "text-muted-foreground"
                }>
                  {gw.status}
                </span>
              </td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">
                {gw.addresses.length > 0 ? gw.addresses.join(", ") : "—"}
              </td>
              <td className="py-2 pr-4">{gw.attachedRoutes}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
