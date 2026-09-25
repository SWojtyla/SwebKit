import { useCallback, useMemo, type MouseEvent } from "react";
import { useAksHttpRoutes, useAksDeleteHttpRoute } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksActions, useAksNav } from "./shared/AksWorkspaceContext";
import type { ContextMenuItem } from "./ContextMenu";
import type { HttpRouteInfo } from "@/lib/types";

interface HttpRoutesTabProps {
    ns: string;
    isMulti?: boolean;
}

function httpRouteStatusRank(route: HttpRouteInfo): number {
    return route.status === "Accepted"
        ? 1
        : route.status === "Pending"
          ? 0
          : -1;
}

const columns: Column<HttpRouteInfo>[] = [
    {
        header: "Hosts",
        cell: (route) => (
            <span className="text-xs">
                {route.hostnames.length > 0 ? route.hostnames.join(", ") : "—"}
            </span>
        ),
    },
    {
        header: "Parents",
        cell: (route) => (
            <span className="text-xs text-muted-foreground">
                {route.parentRefs.length > 0
                    ? route.parentRefs.join(", ")
                    : "—"}
            </span>
        ),
    },
    {
        header: "Backends",
        cell: (route) => (
            <span className="text-xs text-muted-foreground">
                {route.backendRefs.length > 0
                    ? route.backendRefs.join(", ")
                    : "—"}
            </span>
        ),
    },
    {
        header: "Matches",
        cell: (route) => {
            const all = route.rules.flatMap((r) => r.matches);
            return (
                <span
                    className="text-xs text-muted-foreground"
                    title={all.join("\n")}
                >
                    {all.length > 0
                        ? `${all[0]}${all.length > 1 ? ` +${all.length - 1}` : ""}`
                        : "—"}
                </span>
            );
        },
    },
    {
        header: "Filters",
        cell: (route) => {
            const types = [...new Set(route.rules.flatMap((r) => r.filters))];
            return (
                <span
                    className="text-xs text-muted-foreground"
                    title={types.join("\n")}
                >
                    {types.length > 0 ? types.join(", ") : "—"}
                </span>
            );
        },
    },
    {
        header: "Status",
        cell: (route) => (
            <span
                className={
                    route.status === "Accepted"
                        ? "text-success"
                        : route.status === "Pending"
                          ? "text-warning"
                          : "text-muted-foreground"
                }
            >
                {route.status}
            </span>
        ),
        sortValue: httpRouteStatusRank,
    },
];

export function HttpRoutesTab({ ns, isMulti }: HttpRoutesTabProps) {
    const { data: routes, isLoading, error } = useAksHttpRoutes(ns);
    const nav = useAksNav();
  const actions = useAksActions();
  const ws = useMemo(() => ({ ...nav, ...actions }), [nav, actions]);
    const deleteHttpRoute = useAksDeleteHttpRoute();

    const buildMenu = useCallback(
        (route: HttpRouteInfo): ContextMenuItem[] => {
            const host = route.hostnames[0];
            return [
                {
                    label: "View details",
                    icon: "👁",
                    onClick: () => ws.setSelectedHttpRoute(route),
                },
                {
                    label: "Copy name",
                    icon: "📋",
                    onClick: () => ws.copyToClipboard(route.name),
                },
                {
                    label: "View YAML",
                    icon: "{ }",
                    onClick: () =>
                        ws.openYaml("httproute", route.name, route.namespace),
                },
                {
                    label: "Edit YAML",
                    icon: "✎",
                    onClick: () =>
                        ws.openYaml("httproute", route.name, route.namespace),
                },
                {
                    label: "Open URL in browser",
                    icon: "🔗",
                    onClick: () => {
                        if (host) window.open(`https://${host}`, "_blank");
                    },
                    disabled: !host,
                },
                {
                    label: "Copy URL",
                    icon: "📋",
                    onClick: () => {
                        if (host) ws.copyToClipboard(`https://${host}`);
                    },
                    disabled: !host,
                },
                { label: "", separator: true, onClick: () => {} },
                {
                    label: "Delete HTTPRoute",
                    icon: "✕",
                    onClick: () => {
                        ws.requestConfirm({
                            message: `Delete HTTPRoute "${route.name}"?`,
                            resourceName: route.name,
                            onConfirm: () =>
                                deleteHttpRoute.mutate({
                                    ns: route.namespace,
                                    name: route.name,
                                }),
                        });
                    },
                    destructive: true,
                },
            ];
        },
        [ws, deleteHttpRoute],
    );

    const handleRowContextMenu = useCallback(
        (e: MouseEvent<HTMLTableRowElement>, route: HttpRouteInfo) =>
            ws.showContextMenu(e, buildMenu(route)),
        [ws, buildMenu],
    );

    return (
        <ResourceTable
            data={routes}
            isLoading={isLoading}
            error={error}
            isMulti={isMulti}
            testIdPrefix="httproute"
            tableBodyTestId="httproutes-table-body"
            emptyMessage="No HTTP routes found"
            onRowClick={(route) => ws.setSelectedHttpRoute(route)}
            onRowContextMenu={handleRowContextMenu}
            columns={columns}
            defaultSort={{ sortValue: httpRouteStatusRank, direction: "asc" }}
        />
    );
}
