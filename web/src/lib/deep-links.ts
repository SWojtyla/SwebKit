// `swebkit://` deep-link handling — the pure mapping layer. The Tauri side
// (deep-link + single-instance plugins) hands raw URLs here; this module decides
// the in-app route. Anything outside the whitelist returns null so the caller can
// notify the user and fall back to the dashboard instead of navigating blind.
//
// `profile=` is accepted-and-dropped everywhere: the app is single-profile, so the
// parameter survives in shared links for forward compatibility without selecting
// anything here.

export interface ServiceBusNamespaceRef {
    id: string;
    alias?: string | null;
    fullyQualifiedNamespace?: string | null;
}

export interface SqlConnectionRef {
    id: string;
    displayName?: string | null;
    server?: string | null;
    database?: string | null;
}

/** Lazily-resolved alias sources — the caller feeds whatever profile data is loaded. */
export interface DeepLinkContext {
    serviceBusNamespaces?: ServiceBusNamespaceRef[];
    sqlConnections?: SqlConnectionRef[];
}

const SETTINGS_TABS = new Set([
    "general",
    "service-bus",
    "aks",
    "redis",
    "sql",
    "storage",
    "access",
    "agent",
    "map",
    "diagnostics",
    "appearance",
]);

const SIMPLE_PAGES = new Set([
    "aks",
    "api-client",
    "redis",
    "storage",
    "agent",
    "monitoring",
]);

function resolveNamespaceId(hint: string, ctx: DeepLinkContext): string {
    const match = ctx.serviceBusNamespaces?.find(
        (ns) =>
            ns.id === hint ||
            ns.alias?.toLowerCase() === hint.toLowerCase() ||
            ns.fullyQualifiedNamespace?.toLowerCase() === hint.toLowerCase(),
    );
    return match?.id ?? hint;
}

function resolveSqlConnectionId(hint: string, ctx: DeepLinkContext): string {
    const match = ctx.sqlConnections?.find(
        (c) =>
            c.id === hint ||
            c.displayName?.toLowerCase() === hint.toLowerCase() ||
            c.server?.toLowerCase() === hint.toLowerCase(),
    );
    return match?.id ?? hint;
}

function mapServiceBus(segments: string[], params: URLSearchParams, ctx: DeepLinkContext): string {
    const out = new URLSearchParams();
    const ns = params.get("ns");
    if (ns) out.set("ns", resolveNamespaceId(ns, ctx));

    // `swebkit://servicebus/queue/order-created` and `?entity=order-created` both work;
    // entity paths can carry `/` (topic/subscription), so the rest of the path joins back up.
    const entityFromPath = segments.length > 1 ? segments.slice(1).join("/") : null;
    const entity = params.get("entity") ?? entityFromPath;
    if (entity) out.set("entity", entity);

    const entityName = params.get("entityName");
    if (entityName) out.set("entityName", entityName);

    const view = params.get("view");
    if (view === "dlq" || view === "active") out.set("view", view);

    const msg = params.get("msg");
    if (msg) out.set("msg", msg);
    const seq = params.get("seq");
    if (seq) out.set("seq", seq);

    const qs = out.toString();
    return `/service-bus${qs ? `?${qs}` : ""}`;
}

function mapSql(segments: string[], params: URLSearchParams, ctx: DeepLinkContext): string {
    const out = new URLSearchParams();
    // Both `swebkit://sql/demo-sql-prd` and `swebkit://sql?connection=demo-sql-prd`.
    const connection = params.get("connection") ?? segments[0];
    if (connection) out.set("connection", resolveSqlConnectionId(connection, ctx));
    const database = params.get("database");
    if (database) out.set("database", database);
    const table = params.get("table");
    if (table) out.set("table", table);
    const qs = out.toString();
    return `/sql${qs ? `?${qs}` : ""}`;
}

/**
 * Maps a `swebkit://` URL onto an in-app route (path + search). Returns `null` for
 * anything outside the whitelist — malformed URLs, unknown hosts, unknown schemes.
 */
export function mapDeepLink(raw: string, ctx: DeepLinkContext = {}): string | null {
    let url: URL;
    try {
        url = new URL(raw);
    } catch {
        return null;
    }
    if (url.protocol !== "swebkit:") return null;

    const host = url.hostname.toLowerCase();
    const segments = url.pathname.split("/").filter(Boolean).map(decodeURIComponent);
    const params = url.searchParams;

    switch (host) {
        case "":
        case "dashboard":
        case "home":
            return "/";
        case "servicebus":
            return mapServiceBus(segments, params, ctx);
        case "sql":
            return mapSql(segments, params, ctx);
        case "settings": {
            const tab = segments[0] ?? params.get("tab");
            if (tab && SETTINGS_TABS.has(tab)) {
                return `/settings?tab=${encodeURIComponent(tab)}`;
            }
            return "/settings";
        }
        default:
            if (SIMPLE_PAGES.has(host)) return `/${host}`;
            return null;
    }
}

// ── Wiring ───────────────────────────────────────────────────────────────────

export const DEEP_LINK_EVENT = "swebkit://deep-link";

export interface DeepLinkHandlers {
    navigate: (path: string) => void;
    /** User-facing note for an unrecognized link (we still land on the dashboard). */
    notify: (message: string) => void;
    /** Called per link so alias resolution sees the freshest profile data. */
    context: () => DeepLinkContext;
}

/**
 * Wires `swebkit://` handling inside the Tauri shell: subscribes to the
 * `deep-link` event the single-instance/deep-link plugins emit for warm launches,
 * then drains the cold-start queue (`get_pending_deep_links` — the URLs the deep-link
 * plugin captured before the webview's listeners existed). Outside Tauri this is a
 * no-op; browser sessions have no protocol handler to listen to.
 * Returns the unsubscribe function.
 */
export async function initDeepLinks(handlers: DeepLinkHandlers): Promise<() => void> {
    if (typeof window === "undefined" || !("__TAURI_INTERNALS__" in window)) {
        return () => {};
    }
    const { listen } = await import("@tauri-apps/api/event");
    const { invoke } = await import("@tauri-apps/api/core");

    const handle = (url: string) => {
        const route = mapDeepLink(url, handlers.context());
        if (route) {
            handlers.navigate(route);
        } else {
            handlers.notify(`Couldn't open link: ${url}`);
            handlers.navigate("/");
        }
    };

    const unlisten = await listen<string>(DEEP_LINK_EVENT, (event) => handle(event.payload));

    const pending = await invoke<string[]>("get_pending_deep_links").catch(() => [] as string[]);
    for (const url of pending) handle(url);

    return unlisten;
}
