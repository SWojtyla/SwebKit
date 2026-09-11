import { useProfile, useUpdateProfile } from "@/lib/hooks";
import type { ServiceBusNamespace } from "@/lib/types";
import { DraftInput } from "./DraftInput";

export function ServiceBusSettings() {
  const { data: profile } = useProfile();
  const updateProfile = useUpdateProfile();

  if (!profile) return null;

  const namespaces = profile.serviceBusNamespaces;

  const addNamespace = () => {
    const ns: ServiceBusNamespace = {
      id: crypto.randomUUID(),
      alias: "New Namespace",
      fullyQualifiedNamespace: "",
      authMode: "ConnectionString",
      credentialKey: "",
      transportType: "Amqp",
      createdAt: new Date().toISOString(),
    };
    // Updater form, not a snapshot: the mutation reads current state inside `mutationFn`,
    // so two edits in quick succession cannot each PUT a profile computed before the other
    // landed and silently drop one.
    updateProfile.mutate((prev) => ({
      ...prev,
      serviceBusNamespaces: [...prev.serviceBusNamespaces, ns],
    }));
  };

  const removeNamespace = (id: string) => {
    updateProfile.mutate((prev) => ({
      ...prev,
      serviceBusNamespaces: prev.serviceBusNamespaces.filter((n) => n.id !== id),
    }));
  };

  const updateNamespace = (id: string, patch: Partial<ServiceBusNamespace>) => {
    updateProfile.mutate((prev) => ({
      ...prev,
      serviceBusNamespaces: prev.serviceBusNamespaces.map((n) =>
        n.id === id ? { ...n, ...patch } : n,
      ),
    }));
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold">Service Bus Namespaces</h2>
        <button
          onClick={addNamespace}
          className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
        >
          Add Namespace
        </button>
      </div>

      {namespaces.length === 0 && (
        <p className="text-sm text-muted-foreground">
          No Service Bus namespaces configured. Click "Add Namespace" to create one.
        </p>
      )}

      {namespaces.map((ns) => (
        <div key={ns.id} className="space-y-3 rounded-lg border p-4">
          <div className="flex items-center justify-between">
            <DraftInput
              type="text"
              value={ns.alias}
              onCommit={(alias) => updateNamespace(ns.id, { alias })}
              className="flex-1 rounded-md border bg-card px-3 py-1.5 text-sm"
              placeholder="Alias"
            />
            <button
              onClick={() => removeNamespace(ns.id)}
              className="ml-2 text-sm text-destructive hover:opacity-80"
            >
              Remove
            </button>
          </div>

          <DraftInput
            type="text"
            value={ns.fullyQualifiedNamespace}
            onCommit={(fullyQualifiedNamespace) =>
              updateNamespace(ns.id, { fullyQualifiedNamespace })
            }
            className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
            placeholder="e.g. sb-dev-shared-sb-weu.servicebus.windows.net"
          />

          <div className="flex items-center gap-4">
            <label className="flex items-center gap-2 text-sm">
              <input
                type="radio"
                name={`sb-auth-${ns.id}`}
                checked={ns.authMode === "ConnectionString"}
                onChange={() => updateNamespace(ns.id, { authMode: "ConnectionString" })}
                data-testid={`sb-auth-connstring-${ns.id}`}
              />
              Connection String
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input
                type="radio"
                name={`sb-auth-${ns.id}`}
                checked={ns.authMode === "DefaultAzureCredential"}
                onChange={() => updateNamespace(ns.id, { authMode: "DefaultAzureCredential" })}
                data-testid={`sb-auth-entra-${ns.id}`}
              />
              Entra ID
            </label>
            <label className="flex items-center gap-2 text-sm">
              Transport:
              <select
                value={ns.transportType}
                onChange={(e) =>
                  updateNamespace(ns.id, {
                    transportType: e.target.value as "Amqp" | "AmqpWebSockets",
                  })
                }
                className="rounded-md border bg-card px-2 py-1 text-sm"
              >
                <option value="Amqp">AMQP</option>
                <option value="AmqpWebSockets">AMQP WebSockets</option>
              </select>
            </label>
          </div>
        </div>
      ))}
    </div>
  );
}
