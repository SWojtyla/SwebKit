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
            className="rounded-lg border bg-surface-1 p-4 shadow space-y-2"
            data-testid={`httproute-rule-${index}`}
        >
            <div className="text-xs font-medium uppercase tracking-wide text-fg-muted">
                Rule {index + 1}
            </div>
            {rule.matches.length > 0 && (
                <div>
                    <div className="text-xs font-medium mb-1">Matches</div>
                    <ul className="space-y-0.5">
                        {rule.matches.map((m, i) => (
                            <li
                                key={i}
                                className="font-mono text-xs text-fg-muted"
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
                                className="rounded bg-accent-soft px-1.5 py-0.5 font-mono text-[11px] text-accent-ink"
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
                                className="font-mono text-xs text-fg-muted"
                            >
                                {b}
                            </li>
                        ))}
                    </ul>
                </div>
            )}
            {(rule.requestTimeout || rule.backendRequestTimeout) && (
                <div className="flex items-center gap-1.5 text-xs text-fg-muted">
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
                            className="truncate font-mono text-sm font-semibold"
                            data-testid="httproute-detail-name"
                        >
                            {route.name}
                        </h2>
                        <span
                            className={`inline-flex h-5 items-center gap-1 rounded-full px-2 text-xs font-semibold ${
                                accepted
                                    ? "bg-state-done-soft text-state-done-ink"
                                    : "bg-state-blocked-soft text-state-blocked-ink"
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
                    <p className="mt-0.5 text-xs text-fg-muted">
                        {route.namespace} · HTTPRoute
                    </p>
                </div>
                <button
                    onClick={onClose}
                    className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-fg-muted hover:bg-surface-2 hover:text-fg"
                    data-testid="httproute-detail-close"
                >
                    <X className="h-4 w-4" />
                </button>
            </div>
            <div className="flex-1 overflow-auto p-4 space-y-4">
                <div className="rounded-lg border bg-surface-1 p-4 shadow space-y-2">
                    <div className="text-xs font-medium uppercase tracking-wide text-fg-muted">
                        Hostnames
                    </div>
                    <div className="flex flex-wrap gap-1.5">
                        {route.hostnames.length > 0 ? (
                            route.hostnames.map((h) => (
                                <span
                                    key={h}
                                    className="rounded bg-accent-soft px-1.5 py-0.5 font-mono text-[11px] text-accent-ink"
                                >
                                    {h}
                                </span>
                            ))
                        ) : (
                            <span className="text-xs text-fg-muted">
                                * (all hosts)
                            </span>
                        )}
                    </div>
                    {url && (
                        <a
                            href={url}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="inline-flex items-center gap-1 text-xs text-accent-ink hover:underline"
                            data-testid="httproute-detail-url"
                        >
                            <Globe className="h-3 w-3" /> {url}
                        </a>
                    )}
                </div>

                <div className="rounded-lg border bg-surface-1 p-4 shadow space-y-2">
                    <div className="text-xs font-medium uppercase tracking-wide text-fg-muted">
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
                                            <CheckCircle2 className="h-3.5 w-3.5 text-state-done" />
                                        ) : (
                                            <AlertCircle className="h-3.5 w-3.5 text-state-blocked" />
                                        )}
                                        <span className="font-mono">
                                            {p.parentRef}
                                        </span>
                                        <span
                                            className={`ml-auto text-[11px] ${
                                                pAccepted
                                                    ? "text-fg-muted"
                                                    : "text-state-blocked-ink"
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
                        <span className="font-mono text-xs text-fg-muted">
                            {route.parentRefs.join(", ") || "—"}
                        </span>
                    )}
                </div>

                <div className="space-y-2">
                    <div className="text-xs font-medium uppercase tracking-wide text-fg-muted">
                        Rules ({route.rules.length})
                    </div>
                    {route.rules.length > 0 ? (
                        route.rules.map((rule, i) => (
                            <RuleCard key={i} rule={rule} index={i} />
                        ))
                    ) : (
                        <div className="rounded-lg border bg-surface-1 p-4 shadow text-xs text-fg-muted">
                            No rules defined.
                        </div>
                    )}
                </div>
            </div>
            <div className="border-t px-4 py-3">
                <button
                    onClick={onViewYaml}
                    className="h-8 w-full rounded-md border bg-surface-1 px-3 text-sm text-fg hover:bg-surface-2"
                    data-testid="httproute-detail-view-yaml"
                >
                    View YAML
                </button>
            </div>
        </div>
    );
}
