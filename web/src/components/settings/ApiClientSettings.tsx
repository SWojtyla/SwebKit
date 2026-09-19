import { useProfile, useUpdateProfile, useUserSettings, useUpdateUserSettings } from "@/lib/hooks";
import { DraftInput } from "./DraftInput";

export function ApiClientSettings() {
  const { data: settings, isLoading } = useUserSettings();
  const { data: profile } = useProfile();
  const updateProfile = useUpdateProfile();
  const updateSettings = useUpdateUserSettings();

  if (isLoading || !settings) {
    return <div className="text-muted-foreground">Loading...</div>;
  }

  return (
    <div className="space-y-6">
      <section>
        <h2 className="mb-3 text-lg font-semibold">Requests</h2>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={settings.verifyApiClientSsl}
            onChange={(e) => {
              const verifyApiClientSsl = e.target.checked;
              updateSettings.mutate((prev) => ({ ...prev, verifyApiClientSsl }));
            }}
            data-testid="verify-ssl-toggle"
          />
          Verify SSL certificates
        </label>
        <p className="mb-2 mt-0.5 pl-6 text-xs text-muted-foreground">
          Reject requests to hosts with an invalid/self-signed TLS certificate. Turn off only
          for local/dev endpoints you trust.
        </p>
        <label className="mt-2 flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={settings.apiClientRequestTabs}
            onChange={(e) => {
              const apiClientRequestTabs = e.target.checked;
              updateSettings.mutate((prev) => ({ ...prev, apiClientRequestTabs }));
            }}
          />
          Enable request tabs
        </label>
        <p className="mb-2 mt-0.5 pl-6 text-xs text-muted-foreground">
          Open each request in its own tab so several stay open side by side, instead of one
          request replacing the last.
        </p>
        <label className="mt-2 flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={settings.autoSaveRequests}
            onChange={(e) => {
              const autoSaveRequests = e.target.checked;
              updateSettings.mutate((prev) => ({ ...prev, autoSaveRequests }));
            }}
          />
          Auto-save request changes
        </label>
        <p className="mb-2 mt-0.5 pl-6 text-xs text-muted-foreground">
          Save edits to a request (URL, headers, body) back to its collection as you make
          them, instead of only when you explicitly save.
        </p>
      </section>

      {profile && (
        <section data-testid="key-vaults-section">
          <h2 className="mb-1 text-lg font-semibold">Azure Key Vaults</h2>
          <p className="mb-2 text-xs text-muted-foreground">
            Named vaults used by environment variables of type "Key Vault". Authentication uses your Azure CLI identity.
          </p>
          {profile.config.keyVaults.map((kv, i) => (
            <div key={kv.id} className="mb-2 flex items-center gap-2">
              <DraftInput
                type="text"
                value={kv.name}
                onCommit={(name) =>
                  updateProfile.mutate((prev) => ({ ...prev, config: { ...prev.config, keyVaults: prev.config.keyVaults.map((v) => (v.id === kv.id ? { ...v, name } : v)) } }))
                }
                placeholder="Name"
                className="w-40 rounded border bg-background px-2 py-1 text-sm"
                data-testid={`kv-name-${i}`}
              />
              <DraftInput
                type="text"
                value={kv.url}
                onCommit={(url) =>
                  updateProfile.mutate((prev) => ({ ...prev, config: { ...prev.config, keyVaults: prev.config.keyVaults.map((v) => (v.id === kv.id ? { ...v, url } : v)) } }))
                }
                placeholder="https://my-vault.vault.azure.net/"
                className="flex-1 rounded border bg-background px-2 py-1 text-sm"
                data-testid={`kv-url-${i}`}
              />
              <button
                onClick={() =>
                  updateProfile.mutate((prev) => ({ ...prev, config: { ...prev.config, keyVaults: prev.config.keyVaults.filter((v) => v.id !== kv.id) } }))
                }
                className="rounded border px-2 py-1 text-xs hover:bg-accent"
                data-testid={`kv-remove-${i}`}
              >
                Remove
              </button>
            </div>
          ))}
          <button
            onClick={() =>
              updateProfile.mutate((prev) => ({ ...prev, config: { ...prev.config, keyVaults: [...prev.config.keyVaults, { id: crypto.randomUUID(), name: "", url: "" }] } }))
            }
            className="mt-1 rounded border px-2 py-1 text-xs hover:bg-accent"
            data-testid="kv-add"
          >
            + Add Key Vault
          </button>
        </section>
      )}
    </div>
  );
}
