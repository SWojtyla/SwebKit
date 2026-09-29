const SERVICE_BUS_SUFFIX = ".servicebus.windows.net";
const REDIS_SUFFIX = ".redis.cache.windows.net";

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
 * Redis cache names are stored bare (the backend derives `<name>.redis.cache.windows.net`),
 * but users shouldn't have to know that either — a pasted FQDN has the suffix stripped so
 * the stored value stays a name.
 */
export function normalizeRedisCacheName(input: string): string {
    const host = stripToHost(input);
    if (host.toLowerCase().endsWith(REDIS_SUFFIX)) {
        return host.slice(0, host.length - REDIS_SUFFIX.length);
    }
    return host;
}

