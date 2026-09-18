import { useState } from "react";
import { useProfile, useUpdateProfile } from "@/lib/hooks";
import { useSbTestConnection } from "@/lib/hooks/useServiceBus";
import type { ServiceBusNamespace } from "@/lib/types";
import { DraftInput } from "./DraftInput";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { ProfileListLayout } from "./ProfileListLayout";

/** A namespace is worth confirming removal of once it has real configured data — an
 * untouched "New Namespace" placeholder can go without the extra click. */
function isConfigured(ns: ServiceBusNamespace): boolean {
    return (
        ns.fullyQualifiedNamespace.trim() !== "" ||
        ns.credentialKey.trim() !== ""
    );
}

export function ServiceBusSettings() {
    const { data: profile } = useProfile();
    const updateProfile = useUpdateProfile();
    const [pendingRemoveId, setPendingRemoveId] = useState<string | null>(null);

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
            serviceBusNamespaces: prev.serviceBusNamespaces.filter(
                (n) => n.id !== id,
            ),
        }));
    };

    const requestRemove = (ns: ServiceBusNamespace) => {
        if (isConfigured(ns)) {
            setPendingRemoveId(ns.id);
        } else {
            removeNamespace(ns.id);
        }
    };

    const updateNamespace = (
        id: string,
        patch: Partial<ServiceBusNamespace>,
    ) => {
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
                <h2 className="text-lg font-semibold">
                    Service Bus Namespaces
                </h2>
                <button
                    onClick={addNamespace}
                    className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
                >
                    Add Namespace
                </button>
            </div>

            <ProfileListLayout
                items={namespaces}
                getKey={(n) => n.id}
                getTitle={(n) => n.alias}
                getSubtitle={(n) => n.fullyQualifiedNamespace}
                testIdPrefix="sb"
                emptyMessage='No Service Bus namespaces configured. Click "Add Namespace" to create one.'
                renderEditor={(ns) => (
                    <NamespaceRow
                        ns={ns}
                        onUpdate={(patch) => updateNamespace(ns.id, patch)}
                        onRequestRemove={() => requestRemove(ns)}
                        pendingRemove={pendingRemoveId === ns.id}
                        onConfirmRemove={() => {
                            removeNamespace(ns.id);
                            setPendingRemoveId(null);
                        }}
                        onCancelRemove={() => setPendingRemoveId(null)}
                    />
                )}
            />
        </div>
    );
}

interface NamespaceRowProps {
    ns: ServiceBusNamespace;
    onUpdate: (patch: Partial<ServiceBusNamespace>) => void;
    onRequestRemove: () => void;
    pendingRemove: boolean;
    onConfirmRemove: () => void;
    onCancelRemove: () => void;
}

function NamespaceRow({
    ns,
    onUpdate,
    onRequestRemove,
    pendingRemove,
    onConfirmRemove,
    onCancelRemove,
}: NamespaceRowProps) {
    // `enabled: false` here: this only fires when the user clicks "Test connection", not on
    // every Settings render — a namespace can be freshly added with an empty FQDN, and this
    // row exists once per namespace so nothing else has to test on its behalf.
    const test = useSbTestConnection(ns.id, { enabled: false });

    return (
        <div
            className="space-y-3 rounded-lg border p-4"
            data-testid={`sb-namespace-${ns.id}`}
        >
            <div className="flex items-center justify-between">
                <DraftInput
                    type="text"
                    value={ns.alias}
                    onCommit={(alias) => onUpdate({ alias })}
                    className="flex-1 rounded-md border bg-card px-3 py-1.5 text-sm"
                    placeholder="Alias"
                />
                <button
                    onClick={onRequestRemove}
                    className="ml-2 text-sm text-destructive hover:opacity-80"
                    data-testid={`sb-remove-${ns.id}`}
                >
                    Remove
                </button>
            </div>

            <DraftInput
                type="text"
                value={ns.fullyQualifiedNamespace}
                onCommit={(fullyQualifiedNamespace) =>
                    onUpdate({ fullyQualifiedNamespace })
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
                        onChange={() =>
                            onUpdate({ authMode: "ConnectionString" })
                        }
                        data-testid={`sb-auth-connstring-${ns.id}`}
                    />
                    Connection String
                </label>
                <label className="flex items-center gap-2 text-sm">
                    <input
                        type="radio"
                        name={`sb-auth-${ns.id}`}
                        checked={ns.authMode === "DefaultAzureCredential"}
                        onChange={() =>
                            onUpdate({ authMode: "DefaultAzureCredential" })
                        }
                        data-testid={`sb-auth-entra-${ns.id}`}
                    />
                    Entra ID
                </label>
                <label className="flex items-center gap-2 text-sm">
                    Transport:
                    <select
                        value={ns.transportType}
                        onChange={(e) =>
                            onUpdate({
                                transportType: e.target.value as
                                    | "Amqp"
                                    | "AmqpWebSockets",
                            })
                        }
                        className="rounded-md border bg-card px-2 py-1 text-sm"
                    >
                        <option value="Amqp">AMQP</option>
                        <option value="AmqpWebSockets">AMQP WebSockets</option>
                    </select>
                </label>
            </div>

            {ns.authMode === "ConnectionString" && (
                <div>
                    <DraftInput
                        type="text"
                        value={ns.credentialKey}
                        onCommit={(credentialKey) =>
                            onUpdate({ credentialKey })
                        }
                        className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                        placeholder="Credential key for connection string"
                        data-testid={`sb-credential-key-${ns.id}`}
                    />
                    <p className="mt-1 text-xs text-muted-foreground">
                        Looked up in your OS credential store — save the actual
                        connection string there under this key (not typed here)
                        before testing the connection.
                    </p>
                </div>
            )}

            <div className="flex items-center gap-2 pt-1">
                <button
                    onClick={() => test.refetch()}
                    disabled={test.isFetching}
                    title={test.isFetching ? "Testing…" : undefined}
                    className="rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                    data-testid={`sb-test-connection-${ns.id}`}
                >
                    {test.isFetching ? "Testing…" : "Test connection"}
                </button>
                {test.data && (
                    <span
                        className={`text-xs ${test.data.connected ? "text-success" : "text-destructive"}`}
                        data-testid={`sb-test-result-${ns.id}`}
                    >
                        {test.data.connected
                            ? "Connected"
                            : `Failed: ${test.data.error ?? "unknown error"}`}
                    </span>
                )}
                {test.isError && (
                    <span className="text-xs text-destructive">
                        {String(test.error)}
                    </span>
                )}
            </div>

            {pendingRemove && (
                <ConfirmBar
                    message={`Remove "${ns.alias}"? This deletes its configuration from your profile — the namespace itself is unaffected.`}
                    confirmLabel="Remove"
                    onConfirm={onConfirmRemove}
                    onCancel={onCancelRemove}
                    testId={`sb-remove-confirm-${ns.id}`}
                />
            )}
        </div>
    );
}
