import { useAksGatewayClasses } from "@/lib/hooks";
import type { GatewayClassInfo } from "@/lib/types";

interface GatewayClassesTabProps {
  onContextMenu?: (e: React.MouseEvent, gc: GatewayClassInfo) => void;
}

export function GatewayClassesTab({ onContextMenu }: GatewayClassesTabProps) {
  const { data: classes, isLoading } = useAksGatewayClasses();

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!classes || classes.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No gateway classes found</div>;

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            <th className="py-2 pr-4">Controller</th>
            <th className="py-2 pr-4">Status</th>
          </tr>
        </thead>
        <tbody data-testid="gatewayclasses-table-body">
          {classes.map((gc) => (
            <tr
              key={gc.name}
              data-testid={`gatewayclass-row-${gc.name}`}
              className="border-b last:border-0 hover:bg-accent/30"
              onContextMenu={(e) => onContextMenu?.(e, gc)}
            >
              <td className="py-2 pr-4 font-medium">{gc.name}</td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">{gc.controllerName ?? "—"}</td>
              <td className="py-2 pr-4">
                <span className={
                  gc.status === "Accepted" ? "text-green-500" :
                  gc.status === "Pending" ? "text-yellow-500" :
                  "text-muted-foreground"
                }>
                  {gc.status}
                </span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
