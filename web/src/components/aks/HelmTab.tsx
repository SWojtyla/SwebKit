import { useAksHelmReleases } from "@/lib/hooks";
import type { HelmReleaseInfo } from "@/lib/types";

interface HelmTabProps {
  ns: string;
  isMulti?: boolean;
  onReleaseClick?: (release: HelmReleaseInfo) => void;
  onContextMenu?: (e: React.MouseEvent, rel: HelmReleaseInfo) => void;
}

export function HelmTab({ ns, isMulti, onReleaseClick, onContextMenu }: HelmTabProps) {
  const { data: releases, isLoading } = useAksHelmReleases(ns);

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!releases || releases.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No Helm releases found</div>;

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            {isMulti && <th className="py-2 pr-4">Namespace</th>}
            <th className="py-2 pr-4">Chart</th>
            <th className="py-2 pr-4">Version</th>
            <th className="py-2 pr-4">Revision</th>
            <th className="py-2 pr-4">Status</th>
            <th className="py-2 pr-4">Updated</th>
          </tr>
        </thead>
        <tbody data-testid="helm-table-body">
          {releases.map((rel) => (
            <tr key={`${rel.namespace}/${rel.name}`} data-testid={`helm-row-${rel.name}`} className={`border-b last:border-0 ${onReleaseClick ? "cursor-pointer hover:bg-accent/50" : ""}`} onClick={() => onReleaseClick?.(rel)} onContextMenu={(e) => onContextMenu?.(e, rel)}>
              <td className="py-2 pr-4 font-medium">{rel.name}</td>
              {isMulti && <td className="py-2 pr-4 text-xs text-muted-foreground">{rel.namespace}</td>}
              <td className="py-2 pr-4 text-muted-foreground">{rel.chart ?? "—"}</td>
              <td className="py-2 pr-4 text-muted-foreground">{rel.appVersion ?? rel.chartVersion ?? "—"}</td>
              <td className="py-2 pr-4">{rel.revision}</td>
              <td className="py-2 pr-4">
                <span className={rel.status === "deployed" ? "text-green-500" : "text-yellow-500"}>
                  {rel.status}
                </span>
              </td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">
                {rel.updated ? new Date(rel.updated).toLocaleString() : "—"}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
