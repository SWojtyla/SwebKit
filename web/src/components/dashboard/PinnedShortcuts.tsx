import { Link } from "react-router";
import { Pin } from "lucide-react";
import { useProfile } from "@/lib/hooks";

/**
 * Pinned shortcuts — persisted as `FavoriteResource` entries whose snapshots
 * point at feature pages (`kind: "service"` pins created from the service
 * cards). Rendered as navigation shortcuts, which is what they are.
 */
export function PinnedShortcuts() {
    const { data: profile } = useProfile();
    const pinned = profile?.config.favoriteResources ?? [];

    return (
        <div data-testid="pinned-resources">
            <h2 className="mb-3 text-sm font-semibold text-muted-foreground">
                Pinned shortcuts
            </h2>
            {pinned.length === 0 ? (
                <p className="rounded-xl border border-dashed p-4 text-sm text-muted-foreground">
                    Pin a service above for quick access.
                </p>
            ) : (
                <div className="flex flex-wrap gap-2">
                    {pinned.map((favorite) => (
                        <Link
                            key={favorite.snapshot.resource.key}
                            to={favorite.snapshot.resource.displayPath ?? "/"}
                            className="inline-flex items-center gap-2 rounded-lg border bg-card/50 px-3 py-2 text-sm hover:border-primary"
                            data-testid={`pinned-resource-${favorite.snapshot.resource.key}`}
                        >
                            <Pin className="h-3.5 w-3.5 text-primary" />
                            {favorite.name}
                        </Link>
                    ))}
                </div>
            )}
        </div>
    );
}
