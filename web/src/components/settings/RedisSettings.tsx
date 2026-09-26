import { useState } from "react";
import { useProfile, useUpdateProfile } from "@/lib/hooks";
import { useRedisTestConnection } from "@/lib/hooks/useRedis";
import { useNotification } from "@/components/layout/notification-context";
import { clampInt } from "@/lib/clamp-int";
import type { RedisCacheEntry } from "@/lib/types";
import { DraftInput } from "./DraftInput";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { ProfileListLayout } from "./ProfileListLayout";

/** A cache is worth confirming removal of once it has real configured data — an untouched
 * "New Cache" placeholder can go without the extra click. */
function isConfigured(cache: RedisCacheEntry): boolean {
    return (
        cache.connectionString.trim() !== "" || cache.cacheName.trim() !== ""
    );
}

export function RedisSettings() {
    const { data: profile } = useProfile();
    const updateProfile = useUpdateProfile();
    const { notify } = useNotification();
    const [pendingRemoveId, setPendingRemoveId] = useState<string | null>(null);

    if (!profile) return null;

    const redis = profile.config.redisConfig ?? {
        caches: [],
        activeCacheId: null,
        namespaceSeparator: ":",
    };

    // Updater form so concurrent edits queue against current state instead of each
    // PUTting a profile snapshot taken before the other landed.
    const update = (patch: Partial<typeof redis>) => {
        updateProfile.mutate((prev) => ({
            ...prev,
            config: {
                ...prev.config,
                redisConfig: {
                    ...(prev.config.redisConfig ?? redis),
                    ...patch,
                },
            },
        }));
    };

    const addCache = () => {
        const entry: RedisCacheEntry = {
            id: crypto.randomUUID().slice(0, 8),
            displayName: "New Cache",
            connectionString: "",
            database: 0,
            useAad: false,
            cacheName: "",
        };
        update({
            caches: [...redis.caches, entry],
            activeCacheId: redis.activeCacheId ?? entry.id,
        });
    };

    const removeCache = (id: string) => {
        const caches = redis.caches.filter((c) => c.id !== id);
        update({
            caches,
            activeCacheId:
                redis.activeCacheId === id
                    ? (caches[0]?.id ?? null)
                    : redis.activeCacheId,
        });
    };

    const requestRemove = (cache: RedisCacheEntry) => {
        if (isConfigured(cache)) {
            setPendingRemoveId(cache.id);
        } else {
            removeCache(cache.id);
        }
    };

    const updateCache = (id: string, patch: Partial<RedisCacheEntry>) => {
        update({
            caches: redis.caches.map((c) =>
                c.id === id ? { ...c, ...patch } : c,
            ),
        });
    };

    const commitDatabase = (cache: RedisCacheEntry, raw: string) => {
        const result = clampInt(raw, {
            min: 0,
            max: 15,
            fallback: cache.database,
        });
        if (result.invalid) {
            notify(
                "error",
                "Invalid database index",
                `"${raw}" isn't a number — kept at ${cache.database}.`,
            );
        } else if (result.clamped) {
            notify(
                "error",
                "Database index out of range",
                `Redis logical databases are numbered 0–15. Clamped to ${result.value}.`,
            );
        }
        updateCache(cache.id, { database: result.value });
    };

    return (
        <div className="space-y-4">
            <div className="flex items-center justify-between">
                <h2 className="text-lg font-semibold">Redis Caches</h2>
                <button
                    onClick={addCache}
                    className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
                >
                    Add Cache
                </button>
            </div>

            <ProfileListLayout
                items={redis.caches}
                getKey={(c) => c.id}
                getTitle={(c) => c.displayName}
                getSubtitle={(c) =>
                    c.useAad ? c.cacheName : c.connectionString
                }
                isActive={(c) => c.id === redis.activeCacheId}
                testIdPrefix="redis"
                emptyMessage='No Redis caches configured. Click "Add Cache" to create one.'
                above={
                    <div>
                        <label className="mb-1 block text-sm font-medium">
                            Namespace Separator
                        </label>
                        <DraftInput
                            type="text"
                            value={redis.namespaceSeparator}
                            onCommit={(v) => update({ namespaceSeparator: v })}
                            className="w-24 rounded-md border bg-card px-3 py-1.5 text-sm"
                        />
                        <p className="mt-1 text-xs text-muted-foreground">
                            Groups keys hierarchically in the Redis key browser
                            (e.g. "user:123:profile").
                        </p>
                    </div>
                }
                renderEditor={(cache) => (
                    <CacheRow
                        cache={cache}
                        onUpdate={(patch) => updateCache(cache.id, patch)}
                        onCommitDatabase={(raw) => commitDatabase(cache, raw)}
                        onRequestRemove={() => requestRemove(cache)}
                        pendingRemove={pendingRemoveId === cache.id}
                        onConfirmRemove={() => {
                            removeCache(cache.id);
                            setPendingRemoveId(null);
                        }}
                        onCancelRemove={() => setPendingRemoveId(null)}
                    />
                )}
            />
        </div>
    );
}

interface CacheRowProps {
    cache: RedisCacheEntry;
    onUpdate: (patch: Partial<RedisCacheEntry>) => void;
    onCommitDatabase: (raw: string) => void;
    onRequestRemove: () => void;
    pendingRemove: boolean;
    onConfirmRemove: () => void;
    onCancelRemove: () => void;
}

function CacheRow({
    cache,
    onUpdate,
    onCommitDatabase,
    onRequestRemove,
    pendingRemove,
    onConfirmRemove,
    onCancelRemove,
}: CacheRowProps) {
    // `enabled: false`: only fires when "Test connection" is clicked, not on every render.
    const test = useRedisTestConnection(cache.id, { enabled: false });

    return (
        <div
            className="space-y-3 rounded-lg border p-4"
            data-testid={`redis-cache-${cache.id}`}
        >
            <div className="flex items-center justify-between">
                <DraftInput
                    type="text"
                    value={cache.displayName}
                    onCommit={(v) => onUpdate({ displayName: v })}
                    className="flex-1 rounded-md border bg-card px-3 py-1.5 text-sm"
                    placeholder="Display name"
                />
                <button
                    onClick={onRequestRemove}
                    className="ml-2 text-sm text-destructive hover:opacity-80"
                    data-testid={`redis-remove-${cache.id}`}
                >
                    Remove
                </button>
            </div>

            <div className="flex items-center gap-4">
                <label className="flex items-center gap-2 text-sm">
                    <input
                        type="radio"
                        name={`redis-auth-${cache.id}`}
                        checked={!cache.useAad}
                        onChange={() => onUpdate({ useAad: false })}
                        data-testid={`redis-auth-connstring-${cache.id}`}
                    />
                    Connection String
                </label>
                <label className="flex items-center gap-2 text-sm">
                    <input
                        type="radio"
                        name={`redis-auth-${cache.id}`}
                        checked={cache.useAad}
                        onChange={() => onUpdate({ useAad: true })}
                        data-testid={`redis-auth-entra-${cache.id}`}
                    />
                    Entra ID (AAD)
                </label>
            </div>

            {cache.useAad ? (
                <div>
                    <DraftInput
                        type="text"
                        value={cache.cacheName}
                        onCommit={(v) => onUpdate({ cacheName: v })}
                        className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                        placeholder="my-cache"
                    />
                    <p className="mt-1 text-xs text-muted-foreground">
                        The cache's resource name from the Azure portal —
                        connects to{" "}
                        <code>&lt;name&gt;.redis.cache.windows.net</code> using
                        your signed-in Azure identity.
                    </p>
                </div>
            ) : (
                <div>
                    <DraftInput
                        type="text"
                        value={cache.connectionString}
                        onCommit={(v) => onUpdate({ connectionString: v })}
                        className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                        placeholder="localhost:6379"
                    />
                    <p className="mt-1 text-xs text-muted-foreground">
                        StackExchange.Redis connection string, e.g.{" "}
                        <code>localhost:6379</code> or{" "}
                        <code>
                            mycache.redis.cache.windows.net:6380,ssl=True,password=...
                        </code>
                        . Unlike Storage/Service Bus's credential-store key
                        indirection, this is stored as plain text in your local
                        profile (<code>profiles.json</code>), password included.
                    </p>
                </div>
            )}

            <div className="flex items-center gap-2">
                <label className="text-sm">Database:</label>
                <DraftInput
                    type="number"
                    value={String(cache.database)}
                    onCommit={onCommitDatabase}
                    className="w-20 rounded-md border bg-card px-3 py-1.5 text-sm"
                    data-testid={`redis-database-${cache.id}`}
                />
            </div>
            <p className="text-xs text-muted-foreground">
                Database: Redis logical database index (0–15). Leave 0 unless
                this cache uses multiple databases. The cache you select on the
                Redis page is remembered — it is also the one the assistant's
                Redis tools use by default.
            </p>

            <div className="flex items-center gap-2 pt-1">
                <button
                    onClick={() => test.refetch()}
                    disabled={test.isFetching}
                    title={test.isFetching ? "Testing…" : undefined}
                    className="rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                    data-testid={`redis-test-connection-${cache.id}`}
                >
                    {test.isFetching ? "Testing…" : "Test connection"}
                </button>
                {test.data && (
                    <span
                        className={`text-xs ${test.data.connected ? "text-success" : "text-destructive"}`}
                        data-testid={`redis-test-result-${cache.id}`}
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
                    message={`Remove "${cache.displayName}"? This deletes its configuration from your profile — the cache itself is unaffected.`}
                    confirmLabel="Remove"
                    onConfirm={onConfirmRemove}
                    onCancel={onCancelRemove}
                    testId={`redis-remove-confirm-${cache.id}`}
                />
            )}
        </div>
    );
}
