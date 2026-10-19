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
    /** Source cluster stamp — merged multi-context rows carry it so actions route
     * to the cluster the row came from. */
    context?: string;
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
    ws.requestConfirm({
        message,
        resourceName,
        onConfirm: () => mutation.mutate(args),
    });
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
        {
            label: "Copy name",
            icon: "📋",
            onClick: () => ws.copyToClipboard(resource.name),
        },
        {
            label: "View YAML",
            icon: "{ }",
            onClick: () =>
                ws.openYaml(
                    yamlKind,
                    resource.name,
                    resource.namespace,
                    resource.context,
                ),
        },
        ...(opts.middle ?? []),
        ...(opts.onDelete
            ? [
                  { label: "", separator: true, onClick: () => {} },
                  {
                      label: "Delete",
                      icon: "✕",
                      onClick: opts.onDelete,
                      destructive: true,
                  },
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
    opts: {
        primaryLabel?: string;
        onPrimary?: (resource: T) => void;
        testIdPrefix: string;
    },
): Column<T> {
    return {
        header: "Actions",
        sortable: false,
        className: "w-px whitespace-nowrap py-1 pr-4",
        cell: (resource) => (
            <div
                className="flex items-center gap-1 opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100"
                onClick={(e) => e.stopPropagation()}
            >
                {opts.onPrimary && (
                    <button
                        onClick={() => opts.onPrimary!(resource)}
                        className="h-7 rounded-md border border-border bg-surface-1 px-2 text-xs text-fg-muted hover:bg-surface-2 hover:text-fg"
                        data-testid={`${opts.testIdPrefix}-scale-${resource.name}`}
                    >
                        {opts.primaryLabel ?? "Scale"}
                    </button>
                )}
                <button
                    onClick={(e) => ws.showContextMenu(e, buildMenu(resource))}
                    className="inline-flex h-7 w-7 items-center justify-center rounded-md text-fg-muted hover:bg-surface-2 hover:text-fg"
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
