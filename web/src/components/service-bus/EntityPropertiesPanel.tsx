import { useState } from "react";
import { ChevronDown, ChevronRight } from "lucide-react";
import { useSbEntityProperties } from "@/lib/hooks";
import type { SbEntityProperty } from "@/lib/types";

interface Props {
    nsId: string;
    entityPath: string;
}

/**
 * Collapsible read-only properties surface — the management-plane settings the
 * SDK exposes for the selected queue/topic/subscription (sizing, TTL, lock
 * duration, delivery caps, partitioning and session flags) as grouped
 * name/value rows. Editing is deliberately not surfaced: these are broker-side
 * configuration, and the panel answers "what is this entity configured as",
 * nothing more.
 */
export function EntityPropertiesPanel({ nsId, entityPath }: Props) {
    const [open, setOpen] = useState(false);
    const props = useSbEntityProperties(nsId, entityPath, { enabled: open });

    const groups = new Map<string, SbEntityProperty[]>();
    for (const p of props.data?.properties ?? []) {
        const list = groups.get(p.group) ?? [];
        list.push(p);
        groups.set(p.group, list);
    }

    return (
        <div className="border-b" data-testid="sb-entity-properties">
            <button
                type="button"
                onClick={() => setOpen((v) => !v)}
                className="flex w-full items-center gap-2 px-4 py-1.5 text-xs text-muted-foreground hover:text-foreground"
                aria-expanded={open}
                data-testid="sb-entity-properties-toggle"
            >
                {open ? (
                    <ChevronDown className="h-3.5 w-3.5" />
                ) : (
                    <ChevronRight className="h-3.5 w-3.5" />
                )}
                Entity properties
                <span className="text-muted-foreground/70">
                    — read-only broker configuration
                </span>
            </button>
            {open && (
                <div className="px-4 pb-3" data-testid="sb-entity-properties-body">
                    {props.isLoading && (
                        <p className="py-2 text-xs text-muted-foreground">
                            Loading properties…
                        </p>
                    )}
                    {props.isError && (
                        <p
                            className="py-2 text-xs text-destructive"
                            data-testid="sb-entity-properties-error"
                        >
                            Couldn&apos;t load entity properties —{" "}
                            {String(props.error)}
                        </p>
                    )}
                    {props.data && (
                        <div className="grid gap-x-6 gap-y-1 sm:grid-cols-2 lg:grid-cols-3">
                            {[...groups.entries()].map(([group, rows]) => (
                                <div key={group}>
                                    <div className="pt-2 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
                                        {group}
                                    </div>
                                    <dl className="mt-1 space-y-0.5">
                                        {rows.map((p) => (
                                            <div
                                                key={p.name}
                                                className="flex items-baseline justify-between gap-3 text-xs"
                                            >
                                                <dt className="text-muted-foreground">
                                                    {p.name}
                                                </dt>
                                                <dd
                                                    className="truncate font-mono"
                                                    title={p.value}
                                                    data-testid={`sb-entity-prop-${p.name.toLowerCase().replace(/[^a-z0-9]+/g, "-")}`}
                                                >
                                                    {p.value}
                                                </dd>
                                            </div>
                                        ))}
                                    </dl>
                                </div>
                            ))}
                        </div>
                    )}
                </div>
            )}
        </div>
    );
}
