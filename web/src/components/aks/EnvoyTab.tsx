import { useCallback, useState, type MouseEvent } from "react";
import { useAksEnvoyResources } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksActions } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { EnvoyResourceInfo } from "@/lib/types";

/**
 * The "Envoy Gateway" tab: every CRD the gateway.envoyproxy.io API group
 * installs, behind a kind picker. Only the selected kind is fetched — eight
 * sections × auto-refresh would be eight cluster calls per tick for data the
 * user isn't looking at. Spec detail is flattened server-side into highlights;
 * row click opens the full YAML.
 */

// Ordering puts the traffic-shaping policies first — that's where the answers
// ("how many connections does this backend allow?") usually live.
const ENVOY_KINDS = [
    { plural: "backendtrafficpolicies", label: "BackendTrafficPolicy" },
    { plural: "clienttrafficpolicies", label: "ClientTrafficPolicy" },
    { plural: "securitypolicies", label: "SecurityPolicy" },
    { plural: "backends", label: "Backend" },
    { plural: "httproutefilters", label: "HTTPRouteFilter" },
    { plural: "envoyextensionpolicies", label: "EnvoyExtensionPolicy" },
    { plural: "envoypatchpolicies", label: "EnvoyPatchPolicy" },
    { plural: "envoyproxies", label: "EnvoyProxy" },
] as const;

type EnvoyPlural = (typeof ENVOY_KINDS)[number]["plural"];

const columns: Column<EnvoyResourceInfo>[] = [
    {
        header: "Targets",
        cell: (r) => (
            <span className="text-xs text-muted-foreground">
                {r.targetRefs.length > 0 ? r.targetRefs.join(", ") : "—"}
            </span>
        ),
    },
    {
        header: "Highlights",
        className: "py-2 pr-4",
        cell: (r) => (
            <div className="flex flex-wrap gap-1">
                {r.highlights.map((h, i) => (
                    <span
                        key={i}
                        className="rounded bg-accent/60 px-1.5 py-0.5 text-[11px] whitespace-nowrap"
                        title={`${h.label}: ${h.value}`}
                    >
                        {h.label}: {h.value}
                    </span>
                ))}
                {r.highlights.length === 0 && (
                    <span className="text-xs text-muted-foreground">—</span>
                )}
            </div>
        ),
    },
];

export function EnvoyTab({ ns, isMulti }: { ns: string; isMulti?: boolean }) {
    const [plural, setPlural] = useState<EnvoyPlural>("backendtrafficpolicies");
    const { data, isLoading, error } = useAksEnvoyResources(ns, plural);
    const ws = useAksActions();

    const kind = ENVOY_KINDS.find((k) => k.plural === plural)!;

    const buildMenu = useCallback(
        (r: EnvoyResourceInfo): ContextMenuItem[] => [
            {
                label: "Copy name",
                icon: "📋",
                onClick: () => ws.copyToClipboard(r.name),
            },
            {
                label: "View YAML",
                icon: "{ }",
                // EnvoyProxy is cluster-scoped (empty namespace) — the YAML route
                // still needs a {ns} segment; the backend ignores it for
                // cluster-scoped kinds.
                onClick: () =>
                    ws.openYaml(plural, r.name, r.namespace || ns),
            },
        ],
        [ws, plural, ns],
    );

    const handleRowContextMenu = useCallback(
        (e: MouseEvent<HTMLTableRowElement>, r: EnvoyResourceInfo) =>
            ws.showContextMenu(e, buildMenu(r)),
        [ws, buildMenu],
    );

    return (
        <div className="p-4" data-testid="envoy-tab">
            <div className="mb-3 flex flex-wrap gap-1.5" role="tablist">
                {ENVOY_KINDS.map((k) => (
                    <button
                        key={k.plural}
                        role="tab"
                        aria-selected={plural === k.plural}
                        onClick={() => setPlural(k.plural)}
                        data-testid={`envoy-kind-${k.plural}`}
                        className={`rounded-full border px-2.5 py-1 text-xs transition-colors ${
                            plural === k.plural
                                ? "border-primary bg-primary/10 text-foreground"
                                : "border-border text-muted-foreground hover:text-foreground"
                        }`}
                    >
                        {k.label}
                    </button>
                ))}
            </div>
            <ResourceTable
                data={data}
                isLoading={isLoading}
                error={error}
                isMulti={isMulti}
                testIdPrefix="envoy"
                tableBodyTestId="envoy-table-body"
                emptyMessage={`No ${kind.label} resources found — the Envoy Gateway CRDs may not be installed`}
                onRowClick={(r) =>
                    ws.openYaml(plural, r.name, r.namespace || ns)
                }
                onRowContextMenu={handleRowContextMenu}
                columns={columns}
            />
        </div>
    );
}
