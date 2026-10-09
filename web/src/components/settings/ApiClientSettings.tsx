import { Check, Copy, FolderOpen } from "lucide-react";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
    useProfile,
    useUpdateProfile,
    useUserSettings,
    useUpdateUserSettings,
} from "@/lib/hooks";
import { useNotification } from "@/components/layout/notification-context";
import { getCollectionsLocation } from "@/lib/api/apiClient";
import { revealInExplorer } from "@/lib/tauri-bridge";
import { DraftInput } from "./DraftInput";

/// `isTauri` in tauri-bridge is module-private; the same probe is duplicated
/// in transport.ts/update-check.ts, so a local copy is the established pattern.
const RUNS_IN_TAURI =
    typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;

export function ApiClientSettings() {
    const { data: settings, isLoading } = useUserSettings();
    const { data: profile } = useProfile();
    const updateProfile = useUpdateProfile();
    const updateSettings = useUpdateUserSettings();
    const { notify } = useNotification();
    const [copied, setCopied] = useState(false);

    const location = useQuery({
        queryKey: ["collections-location"],
        queryFn: getCollectionsLocation,
        // It only moves when SWEBKIT_APPDATA_ROOT changes — i.e. on a sidecar restart.
        staleTime: Infinity,
    });

    if (isLoading || !settings) {
        return <div className="text-muted-foreground">Loading...</div>;
    }

    const copyPath = async () => {
        if (!location.data?.path) return;
        await navigator.clipboard.writeText(location.data.path);
        setCopied(true);
        setTimeout(() => setCopied(false), 2000);
    };

    const revealPath = async () => {
        if (!location.data?.path) return;
        try {
            const revealed = await revealInExplorer(location.data.path);
            if (!revealed) {
                notify(
                    "info",
                    "Reveal unavailable",
                    "Revealing files needs the desktop app.",
                );
            }
        } catch (err) {
            notify(
                "error",
                "Couldn't reveal in explorer",
                err instanceof Error ? err.message : String(err),
            );
        }
    };

    return (
        <div className="space-y-6">
            <section>
                <h2 className="mb-3 text-lg font-semibold">API Client</h2>
                <label className="flex items-center gap-2 text-sm">
                    <input
                        type="checkbox"
                        checked={settings.verifyApiClientSsl}
                        onChange={(e) => {
                            const verifyApiClientSsl = e.target.checked;
                            updateSettings.mutate((prev) => ({
                                ...prev,
                                verifyApiClientSsl,
                            }));
                        }}
                    />
                    Verify SSL certificates
                </label>
                <p className="mb-2 mt-0.5 pl-6 text-xs text-muted-foreground">
                    Reject requests to hosts with an invalid/self-signed TLS
                    certificate. Turn off only for local/dev endpoints you
                    trust.
                </p>
                <label className="mt-2 flex items-center gap-2 text-sm">
                    <input
                        type="checkbox"
                        checked={settings.apiClientRequestTabs}
                        onChange={(e) => {
                            const apiClientRequestTabs = e.target.checked;
                            updateSettings.mutate((prev) => ({
                                ...prev,
                                apiClientRequestTabs,
                            }));
                        }}
                    />
                    Enable request tabs
                </label>
                <p className="mb-2 mt-0.5 pl-6 text-xs text-muted-foreground">
                    Open each request in its own tab so several stay open side
                    by side, instead of one request replacing the last.
                </p>
                <label className="mt-2 flex items-center gap-2 text-sm">
                    <input
                        type="checkbox"
                        checked={settings.autoSaveRequests}
                        onChange={(e) => {
                            const autoSaveRequests = e.target.checked;
                            updateSettings.mutate((prev) => ({
                                ...prev,
                                autoSaveRequests,
                            }));
                        }}
                    />
                    Auto-save request changes
                </label>
                <p className="mb-2 mt-0.5 pl-6 text-xs text-muted-foreground">
                    Save edits to a request (URL, headers, body) back to its
                    collection as you make them, instead of only when you
                    explicitly save.
                </p>
            </section>

            <section data-testid="collections-storage-section">
                <h2 className="mb-1 text-lg font-semibold">Storage</h2>
                <p className="mb-2 text-sm text-muted-foreground">
                    Collections and environments are stored as plain JSON in the
                    app data folder.
                </p>
                <div className="flex items-center gap-2">
                    <code
                        className="block flex-1 truncate rounded border bg-muted/50 px-3 py-2 font-mono text-xs"
                        data-testid="collections-store-path"
                        title={location.data?.path ?? undefined}
                    >
                        {location.isError
                            ? "Couldn't determine the collections path"
                            : (location.data?.path ?? "…")}
                    </code>
                    <button
                        onClick={copyPath}
                        disabled={!location.data?.path}
                        className="flex shrink-0 items-center gap-1.5 rounded-md border px-3 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
                        data-testid="collections-store-path-copy"
                    >
                        {copied ? (
                            <Check className="h-3.5 w-3.5 text-success" />
                        ) : (
                            <Copy className="h-3.5 w-3.5" />
                        )}
                        {copied ? "Copied" : "Copy path"}
                    </button>
                    {RUNS_IN_TAURI && (
                        <button
                            onClick={revealPath}
                            disabled={!location.data?.path}
                            className="flex shrink-0 items-center gap-1.5 rounded-md border px-3 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
                            data-testid="collections-store-path-reveal"
                        >
                            <FolderOpen className="h-3.5 w-3.5" />
                            Reveal
                        </button>
                    )}
                </div>
            </section>

            {profile && (
                <section data-testid="key-vaults-section">
                    <h2 className="mb-1 text-lg font-semibold">
                        Azure Key Vaults
                    </h2>
                    <p className="mb-2 text-xs text-muted-foreground">
                        Named vaults used by environment variables of type "Key
                        Vault". Authentication uses your Azure CLI identity.
                    </p>
                    {profile.config.keyVaults.map((kv, i) => (
                        <div
                            key={kv.id}
                            className="mb-2 flex items-center gap-2"
                        >
                            <DraftInput
                                type="text"
                                value={kv.name}
                                onCommit={(name) =>
                                    updateProfile.mutate((prev) => ({
                                        ...prev,
                                        config: {
                                            ...prev.config,
                                            keyVaults:
                                                prev.config.keyVaults.map(
                                                    (v) =>
                                                        v.id === kv.id
                                                            ? { ...v, name }
                                                            : v,
                                                ),
                                        },
                                    }))
                                }
                                placeholder="Name"
                                className="w-40 rounded border bg-background px-2 py-1 text-sm"
                                data-testid={`kv-name-${i}`}
                            />
                            <DraftInput
                                type="text"
                                value={kv.url}
                                onCommit={(url) =>
                                    updateProfile.mutate((prev) => ({
                                        ...prev,
                                        config: {
                                            ...prev.config,
                                            keyVaults:
                                                prev.config.keyVaults.map(
                                                    (v) =>
                                                        v.id === kv.id
                                                            ? { ...v, url }
                                                            : v,
                                                ),
                                        },
                                    }))
                                }
                                placeholder="https://my-vault.vault.azure.net/"
                                className="flex-1 rounded border bg-background px-2 py-1 text-sm"
                                data-testid={`kv-url-${i}`}
                            />
                            <button
                                onClick={() =>
                                    updateProfile.mutate((prev) => ({
                                        ...prev,
                                        config: {
                                            ...prev.config,
                                            keyVaults:
                                                prev.config.keyVaults.filter(
                                                    (v) => v.id !== kv.id,
                                                ),
                                        },
                                    }))
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
                            updateProfile.mutate((prev) => ({
                                ...prev,
                                config: {
                                    ...prev.config,
                                    keyVaults: [
                                        ...prev.config.keyVaults,
                                        {
                                            id: crypto.randomUUID(),
                                            name: "",
                                            url: "",
                                        },
                                    ],
                                },
                            }))
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
