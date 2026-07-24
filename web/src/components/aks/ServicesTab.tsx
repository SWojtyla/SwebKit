import { useAksServices } from "@/lib/hooks";

export function ServicesTab({ ns }: { ns: string }) {
  const { data: services, isLoading } = useAksServices(ns);

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!services || services.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No services found</div>;

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            <th className="py-2 pr-4">Type</th>
            <th className="py-2 pr-4">Cluster IP</th>
            <th className="py-2 pr-4">External</th>
            <th className="py-2 pr-4">Ports</th>
          </tr>
        </thead>
        <tbody>
          {services.map((svc) => (
            <tr key={svc.name} className="border-b last:border-0">
              <td className="py-2 pr-4 font-medium">{svc.name}</td>
              <td className="py-2 pr-4">{svc.type}</td>
              <td className="py-2 pr-4 text-muted-foreground">{svc.clusterIp}</td>
              <td className="py-2 pr-4 text-muted-foreground">
                {svc.externalAddresses.length > 0 ? svc.externalAddresses.join(", ") : "—"}
              </td>
              <td className="py-2 pr-4 text-xs">
                {svc.ports.map((p) => `${p.port}:${p.targetPort ?? p.port}/${p.protocol}`).join(", ")}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
