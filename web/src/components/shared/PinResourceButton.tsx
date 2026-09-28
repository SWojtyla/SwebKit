import { Pin, PinOff } from "lucide-react";
import { useProfile, useTogglePinnedResource } from "@/lib/hooks";
import { isFavoritePinned } from "@/lib/pinned-resources";
import type { FavoriteResource } from "@/lib/types";

// Shared pin/unpin toggle for resource surfaces (Service Bus entity, Redis
// cache, storage account, AKS namespace selection, SQL connection). Everything
// funnels into `profile.config.favoriteResources`, which the sidebar's
// PinnedRail, the dashboard's PinnedShortcuts and the command palette all read.
//
// The button stops propagation so it is safe to drop inside clickable rows.

export function PinResourceButton({
    resource,
    testId,
    className,
}: {
    /** Built via the `pin*` factories in `@/lib/pinned-resources` so `key` and
     *  `displayPath` (a complete in-app URL) stay consistent per surface. */
    resource: FavoriteResource;
    testId?: string;
    className?: string;
}) {
    const { data: profile } = useProfile();
    const toggle = useTogglePinnedResource();
    const key = resource.snapshot.resource.key;
    const pinned = isFavoritePinned(profile?.config.favoriteResources, key);
    const Icon = pinned ? PinOff : Pin;

    return (
        <button
            type="button"
            onClick={(e) => {
                e.stopPropagation();
                toggle.mutate({ resource, pinned: !pinned });
            }}
            disabled={toggle.isPending}
            title={pinned ? "Unpin from sidebar" : "Pin to sidebar"}
            aria-pressed={pinned}
            className={`rounded p-1 text-muted-foreground hover:bg-accent hover:text-foreground disabled:opacity-50 ${className ?? ""}`}
            data-testid={testId ?? `pin-resource-${key}`}
        >
            <Icon className="h-3.5 w-3.5" />
        </button>
    );
}
