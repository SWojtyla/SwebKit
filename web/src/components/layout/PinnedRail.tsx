import { useNavigate } from "react-router";
import { X } from "lucide-react";
import { useProfile, useTogglePinnedResource } from "@/lib/hooks";
import {
    pinnedAreaIcon,
    resolveFavoriteTarget,
} from "@/lib/pinned-resources";

/**
 * Persistent pinned-resource rail at the bottom of the sidebar (ux-power-pack
 * §2). Reads `profile.config.favoriteResources` — the same model the dashboard
 * shortcuts, surface pin buttons and the command palette share — and navigates
 * to `snapshot.resource.displayPath`, which is written as a complete in-app
 * URL by the pin factories (`resolveFavoriteTarget` reconstructs one for
 * legacy non-URL display paths).
 */
export function PinnedRail({ collapsed }: { collapsed: boolean }) {
    const { data: profile } = useProfile();
    const toggle = useTogglePinnedResource();
    const navigate = useNavigate();
    const pinned = profile?.config.favoriteResources ?? [];

    if (pinned.length === 0) return null;

    return (
        <div
            className="max-h-64 overflow-y-auto border-t p-2"
            data-testid="pinned-rail"
        >
            {!collapsed && (
                <p
                    className="px-2 pb-1 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground"
                    data-testid="pinned-rail-label"
                >
                    Pinned
                </p>
            )}
            <div className="space-y-0.5">
                {pinned.map((favorite) => {
                    const resource = favorite.snapshot.resource;
                    const target = resolveFavoriteTarget(favorite);
                    const Icon = pinnedAreaIcon(resource.area);
                    const label = favorite.name || resource.displayName;
                    return (
                        <div key={resource.key} className="group relative">
                            <button
                                type="button"
                                onClick={() =>
                                    target &&
                                    navigate(target.to, {
                                        state: target.state,
                                    })
                                }
                                disabled={!target}
                                title={label}
                                className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm text-sidebar-foreground transition-colors hover:bg-sidebar-active hover:text-primary disabled:opacity-50"
                                data-testid={`pinned-rail-item-${resource.key}`}
                            >
                                <Icon className="h-3.5 w-3.5 shrink-0" />
                                {!collapsed && (
                                    <span className="truncate">{label}</span>
                                )}
                            </button>
                            <button
                                type="button"
                                onClick={() =>
                                    toggle.mutate({
                                        resource: favorite,
                                        pinned: false,
                                    })
                                }
                                disabled={toggle.isPending}
                                title={`Unpin ${label}`}
                                className={`rounded p-0.5 text-muted-foreground hover:bg-accent hover:text-foreground disabled:opacity-50 ${
                                    collapsed
                                        ? "hidden"
                                        : "absolute right-1 top-1/2 -translate-y-1/2 opacity-0 group-hover:opacity-100"
                                }`}
                                data-testid={`pinned-rail-unpin-${resource.key}`}
                            >
                                <X className="h-3 w-3" />
                            </button>
                        </div>
                    );
                })}
            </div>
        </div>
    );
}
