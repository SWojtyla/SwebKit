import { useAksIngresses } from "@/lib/hooks";
import type { IngressInfo } from "@/lib/types";

interface IngressesTabProps {
  ns: string;
  onContextMenu?: (e: React.MouseEvent, ing: IngressInfo) => void;
}

export function IngressesTab({ ns, onContextMenu }: IngressesTabProps) {
  const { data: ingresses, isLoading } = useAksIngresses(ns);

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!ingresses || ingresses.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No ingresses found</div>;

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            <th className="py-2 pr-4">Class</th>
            <th className="py-2 pr-4">Hosts</th>
            <th className="py-2 pr-4">Addresses</th>
            <th className="py-2 pr-4">Rules</th>
          </tr>
        </thead>
        <tbody data-testid="ingresses-table-body">
          {ingresses.map((ing) => (
            <tr key={ing.name} data-testid={`ingress-row-${ing.name}`} className="border-b last:border-0 hover:bg-accent/30" onContextMenu={(e) => onContextMenu?.(e, ing)}>
              <td className="py-2 pr-4 font-medium">{ing.name}</td>
              <td className="py-2 pr-4 text-muted-foreground">{ing.ingressClass ?? "—"}</td>
              <td className="py-2 pr-4 text-xs">{ing.rules.map((r) => r.host).filter(Boolean).join(", ") || "—"}</td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">{ing.addresses.length > 0 ? ing.addresses.join(", ") : "—"}</td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">
                {ing.rules.length} rule(s)
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
