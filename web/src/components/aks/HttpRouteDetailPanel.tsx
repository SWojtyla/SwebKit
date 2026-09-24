import { X, Globe, CheckCircle2, AlertCircle, Clock } from "lucide-react";
import type { HttpRouteInfo, HttpRouteRuleInfo } from "@/lib/types";

interface Props {
    route: HttpRouteInfo;
    onClose: () => void;
    onViewYaml: () => void;
}

function RuleCard({ rule, index }: { rule: HttpRouteRuleInfo; index: number }) {
    return (
        <div
            className="rounded-lg border p-3 space-y-2"
            data-testid={`httproute-rule-${index}`}
        >
            <div className="text-xs font-semibold text-muted-foreground">
                Rule {index + 1}
            </div>
            {rule.matches.length > 0 && (
                <div>
                    <div className="text-xs font-medium mb-1">Matches</div>
                    <ul className="space-y-0.5">
                        {rule.matches.map((m, i) => (
                            <li
                                key={i}
                                className="font-mono text-xs text-muted-foreground"
                            >
                                {m}
                            </li>
                        ))}
                    </ul>
                </div>
            )}
            {rule.filters.length > 0 && (
                <div>
                    <div className="text-xs font-medium mb-1">Filters</div>
                    <div className="flex flex-wrap gap-1">
                        {rule.filters.map((f, i) => (
                            <span
                                key={i}
                                className="rounded bg-accent/60 px-1.5 py-0.5 font-mono text-[11px]"
                                data-testid={`httproute-rule-${index}-filter`}
                            >
                                {f}
                            </span>
                        ))}
                    </div>
                </div>
            )}
            {rule.backendRefs.length > 0 && (
                <div>
                    <div className="text-xs font-medium mb-1">Backend refs</div>
                    <ul className="space-y-0.5">
                        {rule.backendRefs.map((b, i) => (
                            <li
                                key={i}
                                className="font-mono text-xs text-muted-foreground"
                            >
                                {b}
                            </li>
                        ))}
                    </ul>
                </div>
            )}
            {(rule.requestTimeout || rule.backendRequestTimeout) && (
                <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
                    <Clock className="h-3 w-3" />
                    <span data-testid={`httproute-rule-${index}-timeouts`}>
                        {[
                            rule.requestTimeout
                                ? `request ${rule.requestTimeout}`
                                : null,
                            rule.backendRequestTimeout
                                ? `backend ${rule.backendRequestTimeout}`
                                : null,
                        ]
                            .filter(Boolean)
                            .join(" · ")}
                    </span>
                </div>
            )}
        </div>
    );
}

export function HttpRouteDetailPanel({ route, onClose, onViewYaml }: Props) {
    const url = route.hostnames[0] ? `https://${route.hostnames[0]}` : null;
    const accepted = route.status === "Accepted";

    return (
        <div
            className="flex h-full flex-col"
            data-testid="httproute-detail-panel"
        >
            <div className="flex items-center justify-between border-b px-4 py-3">
                <div className="min-w-0">
                    <div className="flex items-center gap-2">
                        <h2
                            className="truncate text-lg font-semibold"
                            data-testid="httproute-detail-name"
                        >
                            {route.name}
                        </h2>
                        <span
                            className={`inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-[11px] ${
                                accepted
                                    ? "bg-emerald-500/15 text-emerald-600 dark:text-emerald-400"
                                    : "bg-destructive/15 text-destructive"
                            }`}
                            data-testid="httproute-detail-status"
                        >
                            {accepted ? (
                                <CheckCircle2 className="h-3 w-3" />
                            ) : (
                                <AlertCircle className="h-3 w-3" />
                            )}
                            {route.status}
                        </span>
                    </div>
                    <p className="text-xs text-muted-foreground">
                        {route.namespace} · HTTPRoute
                    </p>
                </div>
                <button
                    onClick={onClose}
                    className="text-muted-foreground hover:text-foreground"
                    data-testid="httproute-detail-close"
                >
                    <X className="h-4 w-4" />
                </button>
            </div>
            <div className="flex-1 overflow-auto p-4 space-y-4">
                <div className="rounded-lg border p-3 space-y-2">
                    <div className="text-xs font-medium">Hostnames</div>
                    <div className="flex flex-wrap gap-1.5">
                        {route.hostnames.length > 0 ? (
                            route.hostnames.map((h) => (
                                <span
                                    key={h}
                                    className="rounded bg-accent/60 px-1.5 py-0.5 font-mono text-[11px]"
                                >
                                    {h}
                                </span>
                            ))
                        ) : (
                            <span className="text-xs text-muted-foreground">
                                * (all hosts)
                            </span>
                        )}
                    </div>
                    {url && (
                        <a
                            href={url}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="inline-flex items-center gap-1 text-xs text-primary hover:underline"
                            data-testid="httproute-detail-url"
                        >
                            <Globe className="h-3 w-3" /> {url}
                        </a>
                    )}
                </div>

                <div className="rounded-lg border p-3 space-y-2">
                    <div className="text-xs font-medium">
                        Attached gateways / parents
                    </div>
                    {route.parentStatuses.length > 0 ? (
                        <ul className="space-y-1.5">
                            {route.parentStatuses.map((p, i) => {
                                const pAccepted = p.status === "Accepted";
                                return (
                                    <li
                                        key={i}
                                        className="flex items-center gap-2 text-xs"
                                        data-testid={`httproute-parent-status-${i}`}
                                    >
                                        {pAccepted ? (
                                            <CheckCircle2 className="h-3.5 w-3.5 text-emerald-500" />
                                        ) : (
                                            <AlertCircle className="h-3.5 w-3.5 text-destructive" />
                                        )}
                                        <span className="font-mono">
                                            {p.parentRef}
                                        </span>
                                        <span
                                            className={`ml-auto text-[11px] ${
                                                pAccepted
                                                    ? "text-muted-foreground"
                                                    : "text-destructive"
                                            }`}
                                        >
                                            {p.status}
                                            {p.reason ? ` · ${p.reason}` : ""}
                                        </span>
                                    </li>
                                );
                            })}
                        </ul>
                    ) : (
                        <span className="font-mono text-xs text-muted-foreground">
                            {route.parentRefs.join(", ") || "—"}
                        </span>
                    )}
                </div>

                <div className="space-y-2">
                    <div className="text-xs font-medium">
                        Rules ({route.rules.length})
                    </div>
                    {route.rules.length > 0 ? (
                        route.rules.map((rule, i) => (
                            <RuleCard key={i} rule={rule} index={i} />
                        ))
                    ) : (
                        <div className="rounded-lg border p-3 text-xs text-muted-foreground">
                            No rules defined.
                        </div>
                    )}
                </div>
            </div>
            <div className="border-t px-4 py-3">
                <button
                    onClick={onViewYaml}
                    className="w-full rounded-md border px-3 py-1.5 text-sm hover:bg-accent"
                    data-testid="httproute-detail-view-yaml"
                >
                    View YAML
                </button>
            </div>
        </div>
    );
}
