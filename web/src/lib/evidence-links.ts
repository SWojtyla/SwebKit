// Evidence-view link resolution (agent-colleague item 1): the backend emits
// `EvidenceItem.view` as `{ kind, params }` — never a URL — and this module is the
// single place those hints become in-app routes. Every kind maps onto the page's
// *existing* search-param conventions; anything outside the whitelist (unknown kind,
// missing required param, bad enum value) returns null so the caller renders the
// finding as plain text — never a dead link.
//
// The same contract is enforced server-side by `EvidenceViewValidator`
// (SwebKit.Core/Models/EvidenceModels.cs) when model output is parsed — the two
// deliberately duplicate the table: each is the other's last line of defense.

export interface EvidenceViewRef {
    kind?: string;
    params?: Record<string, unknown> | null;
}

type Params = Record<string, string>;

interface ViewSpec {
    /** Params that must be present and non-empty for the link to resolve. */
    required: string[];
    /** Params honored beyond the required set. */
    optional: string[];
    /** Per-param allowed values (case-insensitive). Absent = any non-empty value. */
    enums?: Record<string, readonly string[]>;
    path: string;
}

// AKS tab ids duplicated from components/aks/shared/aks-workspace-context.ts —
// kept inline so this lib module doesn't import the component layer.
const AKS_TABS = [
    "deployments", "statefulsets", "pods", "configmaps", "secrets", "helm",
    "jobs", "cronjobs", "services", "ingresses", "gatewayclasses", "gateways",
    "httproutes", "envoy", "hpa", "events", "portforward", "analysis",
] as const;

// Redis tab ids duplicated from components/redis/redis-context.ts (mainTabs).
const REDIS_TABS = [
    "keys", "info", "slowlog", "keyspace", "prefix", "ops", "pubsub",
] as const;

const VIEW_SPECS: Record<string, ViewSpec> = {
    // /service-bus?ns=&entity=&entityName=&view=&msg=&seq=
    serviceBus: {
        required: ["ns"],
        optional: ["entity", "entityName", "view", "msg", "seq"],
        enums: { view: ["active", "dlq"] },
        path: "/service-bus",
    },
    // /sql?connection=&database=&table= (table is "schema.name")
    sql: {
        required: ["connection"],
        optional: ["database", "table"],
        path: "/sql",
    },
    // /aks?ns=&tab=&pod=&yaml=&helm=&container=&logs=&logsNs=
    aks: {
        required: [],
        optional: ["ns", "tab", "pod", "yaml", "helm", "container", "logs", "logsNs"],
        enums: { tab: AKS_TABS },
        path: "/aks",
    },
    // /monitoring?tab=&report= — `report` implies the reports tab; emit it even when
    // the hint omitted `tab` so the link actually opens the reports panel.
    monitoring: {
        required: [],
        optional: ["tab", "report"],
        enums: { tab: ["rules", "history", "ops", "reports"] },
        path: "/monitoring",
    },
    // /redis?cache=&tab=
    redis: {
        required: ["cache"],
        optional: ["tab"],
        enums: { tab: REDIS_TABS },
        path: "/redis",
    },
};

const MAX_VALUE_LENGTH = 512;

/**
 * Resolves an `EvidenceView` hint to an in-app route (path + query), or null when
 * the hint is unusable — unknown kind, non-string/param garbage, a required param
 * missing, or an enum-valued param outside its set. Unknown params are *stripped*
 * rather than fatal (extra model noise shouldn't kill a good link); the caller's
 * contract is `null → render the finding as plain text`.
 */
export function resolveEvidenceView(view: EvidenceViewRef | null | undefined): string | null {
    if (!view || typeof view.kind !== "string") return null;
    const spec = VIEW_SPECS[view.kind.trim()];
    if (!spec) return null;

    const allowed = new Set([...spec.required, ...spec.optional]);
    const params: Params = {};
    const raw = view.params;
    if (raw && typeof raw === "object") {
        for (const [key, value] of Object.entries(raw)) {
            if (!allowed.has(key)) continue;
            const str =
                typeof value === "string"
                    ? value.trim()
                    : typeof value === "number" || typeof value === "boolean"
                      ? String(value)
                      : "";
            if (!str || str.length > MAX_VALUE_LENGTH) continue;
            const enumValues = spec.enums?.[key];
            if (enumValues && !enumValues.includes(str.toLowerCase() as never)) continue;
            params[key] = str;
        }
    }

    for (const key of spec.required) {
        if (!params[key]) return null;
    }
    // Kinds with no required params still need at least one usable param to
    // navigate anywhere meaningful.
    if (Object.keys(params).length === 0) return null;

    if (spec.path === "/monitoring" && params.report && !params.tab) {
        params.tab = "reports";
    }

    const qs = new URLSearchParams(params).toString();
    return `${spec.path}${qs ? `?${qs}` : ""}`;
}
