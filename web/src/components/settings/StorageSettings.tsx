import { useState } from "react";
import { AlertTriangle } from "lucide-react";
import { useProfile, useUpdateProfile } from "@/lib/hooks";
import { useStorageTestConnection } from "@/lib/hooks/useStorage";
import type { StorageConfig } from "@/lib/types";
import { DraftInput } from "./DraftInput";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { ProfileListLayout } from "./ProfileListLayout";

/** An account is worth confirming removal of once it has real configured data — an
 * untouched "New Storage Account" placeholder can go without the extra click. */
function isConfigured(account: StorageConfig): boolean {
    return (
        account.accountName.trim() !== "" ||
        !!account.connectionStringRef?.trim()
    );
}

export function StorageSettings() {
    const { data: profile } = useProfile();
    const updateProfile = useUpdateProfile();
    const [pendingRemoveId, setPendingRemoveId] = useState<string | null>(null);

    if (!profile) return null;

    const accounts = profile.config.storageAccounts;

    const addAccount = () => {
        const entry: StorageConfig = {
            id: crypto.randomUUID().slice(0, 8),
            displayName: "New Storage Account",
            accountName: "",
            connectionStringRef: null,
            useAad: false,
            allowMutations: false,
        };
        // Updater form so concurrent edits queue against current state instead of each
        // PUTting a profile snapshot taken before the other landed.
        updateProfile.mutate((prev) => ({
            ...prev,
            config: {
                ...prev.config,
                storageAccounts: [...prev.config.storageAccounts, entry],
            },
        }));
    };

    const removeAccount = (id: string) => {
        updateProfile.mutate((prev) => ({
            ...prev,
            config: {
                ...prev.config,
                storageAccounts: prev.config.storageAccounts.filter(
                    (a) => a.id !== id,
                ),
            },
        }));
    };

    const requestRemove = (account: StorageConfig) => {
        if (isConfigured(account)) {
            setPendingRemoveId(account.id);
        } else {
            removeAccount(account.id);
        }
    };

    const updateAccount = (id: string, patch: Partial<StorageConfig>) => {
        updateProfile.mutate((prev) => ({
            ...prev,
            config: {
                ...prev.config,
                storageAccounts: prev.config.storageAccounts.map((a) =>
                    a.id === id ? { ...a, ...patch } : a,
                ),
            },
        }));
    };

    return (
        <div className="space-y-4">
            <div className="flex items-center justify-between">
                <h2 className="text-lg font-semibold">Storage Accounts</h2>
                <button
                    onClick={addAccount}
                    className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
                >
                    Add Account
                </button>
            </div>

            <ProfileListLayout
                items={accounts}
                getKey={(a) => a.id}
                getTitle={(a) => a.displayName}
                getSubtitle={(a) => a.accountName}
                testIdPrefix="storage"
                emptyMessage='No storage accounts configured. Click "Add Account" to create one.'
                renderEditor={(account) => (
                    <AccountRow
                        account={account}
                        onUpdate={(patch) => updateAccount(account.id, patch)}
                        onRequestRemove={() => requestRemove(account)}
                        pendingRemove={pendingRemoveId === account.id}
                        onConfirmRemove={() => {
                            removeAccount(account.id);
                            setPendingRemoveId(null);
                        }}
                        onCancelRemove={() => setPendingRemoveId(null)}
                    />
                )}
            />
        </div>
    );
}

interface AccountRowProps {
    account: StorageConfig;
    onUpdate: (patch: Partial<StorageConfig>) => void;
    onRequestRemove: () => void;
    pendingRemove: boolean;
    onConfirmRemove: () => void;
    onCancelRemove: () => void;
}

function AccountRow({
    account,
    onUpdate,
    onRequestRemove,
    pendingRemove,
    onConfirmRemove,
    onCancelRemove,
}: AccountRowProps) {
    // `enabled: false`: only fires when "Test connection" is clicked, not on every render.
    const test = useStorageTestConnection(account.id, { enabled: false });

    return (
        <div
            className="space-y-3 rounded-lg border p-4"
            data-testid={`storage-account-${account.id}`}
        >
            <div className="flex items-center justify-between">
                <DraftInput
                    type="text"
                    value={account.displayName}
                    onCommit={(v) => onUpdate({ displayName: v })}
                    className="flex-1 rounded-md border bg-card px-3 py-1.5 text-sm"
                    placeholder="Display name"
                />
                <button
                    onClick={onRequestRemove}
                    className="ml-2 text-sm text-destructive hover:opacity-80"
                    data-testid={`storage-remove-${account.id}`}
                >
                    Remove
                </button>
            </div>

            <DraftInput
                type="text"
                value={account.accountName}
                onCommit={(v) => onUpdate({ accountName: v })}
                className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                placeholder="Storage account name"
            />

            <div className="flex items-center gap-4">
                <label className="flex items-center gap-2 text-sm">
                    <input
                        type="radio"
                        name={`storage-auth-${account.id}`}
                        checked={!account.useAad}
                        onChange={() => onUpdate({ useAad: false })}
                        data-testid={`storage-auth-connstring-${account.id}`}
                    />
                    Connection String
                </label>
                <label className="flex items-center gap-2 text-sm">
                    <input
                        type="radio"
                        name={`storage-auth-${account.id}`}
                        checked={account.useAad}
                        onChange={() => onUpdate({ useAad: true })}
                        data-testid={`storage-auth-entra-${account.id}`}
                    />
                    Entra ID (AAD)
                </label>
            </div>

            {!account.useAad && (
                <div>
                    <DraftInput
                        type="text"
                        value={account.connectionStringRef ?? ""}
                        onCommit={(v) =>
                            onUpdate({ connectionStringRef: v || null })
                        }
                        className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                        placeholder="Credential key for connection string"
                    />
                    <p className="mt-1 text-xs text-muted-foreground">
                        Looked up in your OS credential store — save the actual
                        connection string there under this key (not typed here)
                        before testing the connection.
                    </p>
                </div>
            )}

            <div>
                <label className="flex items-center gap-2 text-sm">
                    <input
                        type="checkbox"
                        checked={account.allowMutations}
                        onChange={(e) =>
                            onUpdate({ allowMutations: e.target.checked })
                        }
                        data-testid={`storage-allow-mutations-${account.id}`}
                    />
                    Allow mutations (upload, delete, etc.)
                </label>
                {account.allowMutations && (
                    <div
                        className="mt-2 flex items-center gap-2 rounded-md bg-warning/10 px-3 py-2 text-xs text-warning"
                        data-testid={`storage-allow-mutations-warning-${account.id}`}
                    >
                        <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
                        This account grants real write access — uploads,
                        deletes, and overwrites in the Storage browser take
                        effect immediately, unlike a read-only connection.
                    </div>
                )}
            </div>

            <div className="flex items-center gap-2 pt-1">
                <button
                    onClick={() => test.refetch()}
                    disabled={test.isFetching}
                    className="rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                    data-testid={`storage-test-connection-${account.id}`}
                >
                    {test.isFetching ? "Testing…" : "Test connection"}
                </button>
                {test.data && (
                    <span
                        className={`text-xs ${test.data.connected ? "text-success" : "text-destructive"}`}
                        data-testid={`storage-test-result-${account.id}`}
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
                    message={`Remove "${account.displayName}"? This deletes its configuration from your profile — the storage account itself is unaffected.`}
                    confirmLabel="Remove"
                    onConfirm={onConfirmRemove}
                    onCancel={onCancelRemove}
                    testId={`storage-remove-confirm-${account.id}`}
                />
            )}
        </div>
    );
}
