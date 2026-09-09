import { useProfile, useUpdateProfile } from "@/lib/hooks";
import type { StorageConfig } from "@/lib/types";
import { DraftInput } from "./DraftInput";

export function StorageSettings() {
  const { data: profile } = useProfile();
  const updateProfile = useUpdateProfile();

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
        storageAccounts: prev.config.storageAccounts.filter((a) => a.id !== id),
      },
    }));
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

      {accounts.map((account) => (
        <div key={account.id} className="space-y-3 rounded-lg border p-4">
          <div className="flex items-center justify-between">
            <DraftInput
              type="text"
              value={account.displayName}
              onCommit={(v) => updateAccount(account.id, { displayName: v })}
              className="flex-1 rounded-md border bg-card px-3 py-1.5 text-sm"
              placeholder="Display name"
            />
            <button
              onClick={() => removeAccount(account.id)}
              className="ml-2 text-sm text-destructive hover:opacity-80"
            >
              Remove
            </button>
          </div>

          <DraftInput
            type="text"
            value={account.accountName}
            onCommit={(v) => updateAccount(account.id, { accountName: v })}
            className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
            placeholder="Storage account name"
          />

          <div className="flex items-center gap-4">
            <label className="flex items-center gap-2 text-sm">
              <input
                type="radio"
                name={`storage-auth-${account.id}`}
                checked={!account.useAad}
                onChange={() => updateAccount(account.id, { useAad: false })}
                data-testid={`storage-auth-connstring-${account.id}`}
              />
              Connection String
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input
                type="radio"
                name={`storage-auth-${account.id}`}
                checked={account.useAad}
                onChange={() => updateAccount(account.id, { useAad: true })}
                data-testid={`storage-auth-entra-${account.id}`}
              />
              Entra ID (AAD)
            </label>
          </div>

          {!account.useAad && (
            <DraftInput
              type="text"
              value={account.connectionStringRef ?? ""}
              onCommit={(v) => updateAccount(account.id, { connectionStringRef: v || null })}
              className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
              placeholder="Credential key for connection string"
            />
          )}

          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={account.allowMutations}
              onChange={(e) => updateAccount(account.id, { allowMutations: e.target.checked })}
            />
            Allow mutations (upload, delete, etc.)
          </label>
        </div>
      ))}
    </div>
  );
}
