const SERVICE_BUS_SUFFIX = ".servicebus.windows.net";

/** Strips a URI scheme, trailing path/slash and a port so a pasted endpoint reduces to
 * its host part. `sb://x.servicebus.windows.net/` and `rediss://x:6380` both collapse to
 * the bare host. */
function stripToHost(input: string): string {
    return input
        .trim()
        .replace(/^[a-z][a-z0-9+.-]*:\/\//i, "")
        .replace(/\/.*$/, "")
        .replace(/:\d+$/, "")
        .trim();
}

/**
 * Service Bus namespaces are stored fully qualified, but users shouldn't have to know the
 * suffix — a bare resource name gets it appended, a pasted FQDN (or private-endpoint
 * hostname) passes through untouched.
 */
export function normalizeServiceBusNamespace(input: string): string {
    const host = stripToHost(input);
    if (host === "" || host.includes(".")) return host;
    return `${host}${SERVICE_BUS_SUFFIX}`;
}

/**
 * Redis accepts the cache name or its full hostname — the stored value is used verbatim
 * when it contains a dot (see `RedisClient.ResolveAadHost`), and a bare name gets the
 * `<name>.redis.cache.windows.net` suffix server-side. Keeping the FQDN intact is what
 * makes private-link and custom-DNS endpoints connectable.
 */
export function normalizeRedisCacheHost(input: string): string {
    return stripToHost(input);
}
