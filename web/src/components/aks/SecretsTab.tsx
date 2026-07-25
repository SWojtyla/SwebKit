import { useAksSecrets } from "@/lib/hooks";

export function SecretsTab({ ns }: { ns: string }) {
  const { data: secrets, isLoading } = useAksSecrets(ns);

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!secrets || secrets.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No secrets found</div>;

  return (
    <div className="p-4">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th className="py-2 pr-4">Name</th>
            <th className="py-2 pr-4">Type</th>
            <th className="py-2 pr-4">Keys</th>
          </tr>
        </thead>
        <tbody data-testid="secrets-table-body">
          {secrets.map((secret) => (
            <tr key={secret.name} data-testid={`secret-row-${secret.name}`} className="border-b last:border-0">
              <td className="py-2 pr-4 font-medium">{secret.name}</td>
              <td className="py-2 pr-4 text-muted-foreground">{secret.type}</td>
              <td className="py-2 pr-4 text-xs text-muted-foreground">
                {secret.keys.length > 0 ? secret.keys.join(", ") : "—"}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
