import { useAksHttpRoutes } from "@/lib/hooks";
import type { HttpRouteInfo } from "@/lib/types";

interface HttpRoutesTabProps {
  ns: string;
  onContextMenu?: (e: React.MouseEvent, route: HttpRouteInfo) => void;
}

export function HttpRoutesTab({ ns, onContextMenu }: HttpRoutesTabProps) {
  const { data: routes, isLoading } = useAksHttpRoutes(ns);

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!routes || routes.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No HTTP routes found</div>;

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            <th className="py-2 pr-4">Hosts</th>
            <th className="py-2 pr-4">Parents</th>
            <th className="py-2 pr-4">Backends</th>
            <th className="py-2 pr-4">Status</th>
          </tr>
        </thead>
        <tbody data-testid="httproutes-table-body">
          {routes.map((route) => (
            <tr
              key={route.name}
              data-testid={`httproute-row-${route.name}`}
              className="border-b last:border-0 hover:bg-accent/30"
              onContextMenu={(e) => onContextMenu?.(e, route)}
            >
              <td className="py-2 pr-4 font-medium">{route.name}</td>
              <td className="py-2 pr-4 text-xs">
                {route.hostnames.length > 0 ? route.hostnames.join(", ") : "—"}
              </td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">
                {route.parentRefs.length > 0 ? route.parentRefs.join(", ") : "—"}
              </td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">
                {route.backendRefs.length > 0 ? route.backendRefs.join(", ") : "—"}
              </td>
              <td className="py-2 pr-4">
                <span className={
                  route.status === "Accepted" ? "text-green-500" :
                  route.status === "Pending" ? "text-yellow-500" :
                  "text-muted-foreground"
                }>
                  {route.status}
                </span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
