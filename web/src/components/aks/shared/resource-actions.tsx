import { MoreHorizontal } from "lucide-react";
import type { Column } from "./ResourceTable";
import type { ContextMenuItem } from "../ContextMenu";
import type { AksActionsValue } from "./aks-workspace-context";

/**
 * Shared scaffolding for the per-resource AKS tables. Each resource still owns
 * its columns, confirm copy, and mutations — these helpers only own the parts
 * that were byte-for-byte duplicated across HpaTable/ScaledJobsTable (and the
 * prefix of DeploymentsTab's menu): the confirm→mutate wrapper, the standard
 * context-menu skeleton, and the "primary action + ⋯ overflow" actions column.
 */

export interface NamedResource {
    name: string;
    namespace: string;
}

/**
 * `requestConfirm` + `mutation.mutate` in one call — every destructive or
 * consequential row action on these tables follows this exact shape.
 */
export function confirmMutation<TArgs>(
    ws: Pick<AksActionsValue, "requestConfirm">,
    mutation: { mutate: (args: TArgs) => void },
    message: string,
    resourceName: string,
    args: TArgs,
): void {
    ws.requestConfirm({ message, resourceName, onConfirm: () => mutation.mutate(args) });
}

/**
 * The standard row context menu: Copy name / View YAML, then any
 * resource-specific items, then a destructive Delete when provided.
 */
export function resourceMenuItems(
    ws: Pick<AksActionsValue, "copyToClipboard" | "openYaml">,
    resource: NamedResource,
    yamlKind: string,
    opts: { middle?: ContextMenuItem[]; onDelete?: () => void } = {},
): ContextMenuItem[] {
    return [
        { label: "Copy name", icon: "📋", onClick: () => ws.copyToClipboard(resource.name) },
        { label: "View YAML", icon: "{ }", onClick: () => ws.openYaml(yamlKind, resource.name, resource.namespace) },
        ...(opts.middle ?? []),
        ...(opts.onDelete
            ? [
                  { label: "", separator: true, onClick: () => {} },
                  { label: "Delete", icon: "✕", onClick: opts.onDelete, destructive: true },
              ]
            : []),
    ];
}

/**
 * The trailing "Actions" column: an optional primary button (e.g. "Scale")
 * plus the ⋯ overflow that opens the row's context menu.
 */
export function actionsColumn<T extends NamedResource>(
    ws: Pick<AksActionsValue, "showContextMenu">,
    buildMenu: (resource: T) => ContextMenuItem[],
    opts: { primaryLabel?: string; onPrimary?: (resource: T) => void; testIdPrefix: string },
): Column<T> {
    return {
        header: "Actions",
        className: "py-2 pr-4 w-px whitespace-nowrap",
        cell: (resource) => (
            <div className="flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
                {opts.onPrimary && (
                    <button
                        onClick={() => opts.onPrimary!(resource)}
                        className="rounded border border-border px-2 py-1 text-xs hover:bg-accent/50"
                        data-testid={`${opts.testIdPrefix}-scale-${resource.name}`}
                    >
                        {opts.primaryLabel ?? "Scale"}
                    </button>
                )}
                <button
                    onClick={(e) => ws.showContextMenu(e, buildMenu(resource))}
                    className="rounded border border-border px-1.5 py-1 text-xs text-muted-foreground hover:bg-accent/50"
                    aria-label={`More actions for ${resource.name}`}
                    title="More actions"
                    data-testid={`${opts.testIdPrefix}-actions-${resource.name}`}
                >
                    <MoreHorizontal className="h-3.5 w-3.5" />
                </button>
            </div>
        ),
    };
}
