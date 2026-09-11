import { useProfile, useUpdateProfile } from "@/lib/hooks";
import type { RedisCacheEntry } from "@/lib/types";
import { DraftInput } from "./DraftInput";

export function RedisSettings() {
  const { data: profile } = useProfile();
  const updateProfile = useUpdateProfile();

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
        redisConfig: { ...(prev.config.redisConfig ?? redis), ...patch },
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
      activeCacheId: redis.activeCacheId === id ? caches[0]?.id ?? null : redis.activeCacheId,
    });
  };

  const updateCache = (id: string, patch: Partial<RedisCacheEntry>) => {
    update({
      caches: redis.caches.map((c) => (c.id === id ? { ...c, ...patch } : c)),
    });
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

      <div>
        <label className="mb-1 block text-sm font-medium">Namespace Separator</label>
        <DraftInput
          type="text"
          value={redis.namespaceSeparator}
          onCommit={(v) => update({ namespaceSeparator: v })}
          className="w-24 rounded-md border bg-card px-3 py-1.5 text-sm"
        />
        <p className="mt-1 text-xs text-muted-foreground">
          Groups keys hierarchically in the Redis key browser (e.g. "user:123:profile").
        </p>
      </div>

      {redis.caches.map((cache) => (
        <div key={cache.id} className="space-y-3 rounded-lg border p-4">
          <div className="flex items-center justify-between">
            <DraftInput
              type="text"
              value={cache.displayName}
              onCommit={(v) => updateCache(cache.id, { displayName: v })}
              className="flex-1 rounded-md border bg-card px-3 py-1.5 text-sm"
              placeholder="Display name"
            />
            <button
              onClick={() => removeCache(cache.id)}
              className="ml-2 text-sm text-destructive hover:opacity-80"
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
                onChange={() => updateCache(cache.id, { useAad: false })}
                data-testid={`redis-auth-connstring-${cache.id}`}
              />
              Connection String
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input
                type="radio"
                name={`redis-auth-${cache.id}`}
                checked={cache.useAad}
                onChange={() => updateCache(cache.id, { useAad: true })}
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
                onCommit={(v) => updateCache(cache.id, { cacheName: v })}
                className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                placeholder="my-cache"
              />
              <p className="mt-1 text-xs text-muted-foreground">
                The cache's resource name from the Azure portal — connects to{" "}
                <code>&lt;name&gt;.redis.cache.windows.net</code> using your signed-in Azure identity.
              </p>
            </div>
          ) : (
            <div>
              <DraftInput
                type="text"
                value={cache.connectionString}
                onCommit={(v) => updateCache(cache.id, { connectionString: v })}
                className="w-full rounded-md border bg-card px-3 py-1.5 text-sm"
                placeholder="localhost:6379"
              />
              <p className="mt-1 text-xs text-muted-foreground">
                StackExchange.Redis connection string, e.g. <code>localhost:6379</code> or{" "}
                <code>mycache.redis.cache.windows.net:6380,ssl=True,password=...</code>.
              </p>
            </div>
          )}

          <div className="flex items-center gap-2">
            <label className="text-sm">Database:</label>
            <DraftInput
              type="number"
              value={String(cache.database)}
              onCommit={(v) => updateCache(cache.id, { database: parseInt(v) || 0 })}
              className="w-20 rounded-md border bg-card px-3 py-1.5 text-sm"
            />
            <label className="ml-4 flex items-center gap-2 text-sm">
              <input
                type="radio"
                name="redis-active-cache"
                checked={redis.activeCacheId === cache.id}
                onChange={() => update({ activeCacheId: cache.id })}
              />
              Active
            </label>
          </div>
          <p className="text-xs text-muted-foreground">
            Database: Redis logical database index (0–15). Leave 0 unless this cache uses multiple
            databases. Active: the cache used when you open the Redis browser page.
          </p>
        </div>
      ))}
    </div>
  );
}
