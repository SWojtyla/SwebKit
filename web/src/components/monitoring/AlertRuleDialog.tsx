import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { X } from "lucide-react";
import { Dialog } from "@/components/shared/Dialog";
import type {
    MonitoringAlertRule,
    AlertRuleSource,
    AlertSeverity,
    AksPodAlertParams,
    ServiceBusAlertParams,
    RedisAlertParams,
} from "../../lib/api";
import { getServiceBusNamespaces, getRedisCaches } from "../../lib/api";
import { useAksContexts, useAksNamespaces, useProfile } from "../../lib/hooks";
import { isAlertRuleComplete } from "./alertRuleValidation";

const sources: { value: AlertRuleSource; label: string }[] = [
    { value: "AksPodHealth", label: "AKS · Pod Health" },
    { value: "AksPodRestartRate", label: "AKS · Pod Restart Rate" },
    { value: "AksNamespaceHealthScore", label: "AKS · Namespace Health Score" },
    { value: "ServiceBusDlqDepth", label: "Service Bus · DLQ Depth" },
    { value: "ServiceBusActiveDepth", label: "Service Bus · Active Depth" },
    {
        value: "ServiceBusDeadSubscription",
        label: "Service Bus · Dead Subscription",
    },
    { value: "RedisMemoryUsage", label: "Redis · Memory Usage" },
    { value: "RedisConnectedClients", label: "Redis · Connected Clients" },
];

const empty = (): MonitoringAlertRule => ({
    id: "",
    name: "",
    enabled: true,
    source: "AksPodHealth",
    severity: "Warning",
    intervalSeconds: 60,
    cooldownMinutes: 5,
    aksPodParams: {
        namespace: "",
        restartThreshold: 5,
        healthScoreThreshold: 0.25,
    },
    serviceBusParams: {
        namespaceConnectionAlias: "",
        entityPath: "",
        messageCountThreshold: 1,
    },
    redisAlertParams: {
        connectionAlias: "",
        memoryUsageThresholdPercent: 80,
        clientCountLowerBound: 1,
    },
    aiInvestigationEnabled: true,
});

export function AlertRuleDialog({
    rule,
    onSave,
    onCancel,
}: {
    rule: MonitoringAlertRule | null;
    onSave: (rule: MonitoringAlertRule) => void;
    onCancel: () => void;
}) {
    const [draft, setDraft] = useState<MonitoringAlertRule>(rule ?? empty());

    const [prevRule, setPrevRule] = useState(rule);
    if (prevRule !== rule) {
        setPrevRule(rule);
        setDraft(rule ?? empty());
    }

    const isAks = draft.source.startsWith("Aks");
    const selectedAksContext = draft.aksPodParams?.kubeconfigContext ?? "";
    const { data: profile } = useProfile();
    const configuredAksContext =
        profile?.config.aksConfig?.kubeconfigContext ?? "";
    const { data: aksContexts } = useAksContexts();
    // An empty kubeconfigContext means "follow the globally configured context", so the
    // namespace list resolves the effective context the same way evaluation does.
    const {
        data: aksNamespaces,
        isLoading: aksNamespacesLoading,
        isError: aksNamespacesError,
    } = useAksNamespaces(isAks, selectedAksContext || undefined);
    const { data: sbNamespaces } = useQuery({
        queryKey: ["sb-namespaces-list"],
        queryFn: ({ signal }) => getServiceBusNamespaces(signal),
    });
    const { data: redisCaches } = useQuery({
        queryKey: ["redis-caches-list"],
        queryFn: ({ signal }) => getRedisCaches(signal),
    });

    const set = (patch: Partial<MonitoringAlertRule>) =>
        setDraft((d) => ({ ...d, ...patch }));

    const isComplete = isAlertRuleComplete(draft);

    const save = () => {
        if (!isComplete) return;
        const cleaned: MonitoringAlertRule = { ...draft };
        // Only keep the params block relevant to the selected source.
        if (!draft.source.startsWith("Aks")) cleaned.aksPodParams = null;
        if (!draft.source.startsWith("ServiceBus"))
            cleaned.serviceBusParams = null;
        if (!draft.source.startsWith("Redis")) cleaned.redisAlertParams = null;
        onSave(cleaned);
    };

    return (
        <Dialog
            onClose={onCancel}
            label={rule ? "Edit Rule" : "New Alert Rule"}
            testId="alert-rule-dialog"
            widthClassName="max-h-[90vh] w-[34rem] overflow-auto"
        >
            <div className="p-6">
                <div className="mb-4 flex items-center justify-between">
                    <h2
                        className="text-lg font-semibold"
                        data-testid="alert-rule-dialog-title"
                    >
                        {rule ? "Edit Rule" : "New Alert Rule"}
                    </h2>
                    <button
                        onClick={onCancel}
                        className="rounded p-1 hover:bg-accent"
                        data-testid="alert-rule-dialog-cancel"
                    >
                        <X className="h-4 w-4" />
                    </button>
                </div>

                <div className="space-y-3">
                    <Field label="Name">
                        <input
                            value={draft.name}
                            onChange={(e) => set({ name: e.target.value })}
                            className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
                            data-testid="alert-rule-name"
                        />
                    </Field>

                    <div className="grid grid-cols-2 gap-3">
                        <Field label="Source">
                            <select
                                value={draft.source}
                                onChange={(e) =>
                                    set({
                                        source: e.target
                                            .value as AlertRuleSource,
                                    })
                                }
                                className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
                                data-testid="alert-rule-source"
                            >
                                {sources.map((s) => (
                                    <option key={s.value} value={s.value}>
                                        {s.label}
                                    </option>
                                ))}
                            </select>
                        </Field>
                        <Field label="Severity">
                            <select
                                value={draft.severity}
                                onChange={(e) =>
                                    set({
                                        severity: e.target
                                            .value as AlertSeverity,
                                    })
                                }
                                className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
                                data-testid="alert-rule-severity"
                            >
                                <option value="Warning">Warning</option>
                                <option value="Critical">Critical</option>
                            </select>
                        </Field>
                    </div>

                    <div className="grid grid-cols-2 gap-3">
                        <Field label="Interval (s)">
                            <input
                                type="number"
                                value={draft.intervalSeconds}
                                onChange={(e) =>
                                    set({
                                        intervalSeconds: Number(e.target.value),
                                    })
                                }
                                className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
                                data-testid="alert-rule-interval"
                            />
                        </Field>
                        <Field label="Cooldown (min)">
                            <input
                                type="number"
                                value={draft.cooldownMinutes}
                                onChange={(e) =>
                                    set({
                                        cooldownMinutes: Number(e.target.value),
                                    })
                                }
                                className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
                                data-testid="alert-rule-cooldown"
                            />
                        </Field>
                    </div>

                    {draft.source.startsWith("Aks") && (
                        <div className="space-y-3 rounded-md border p-3">
                            <Field label="Cluster context">
                                <select
                                    value={selectedAksContext}
                                    onChange={(e) =>
                                        // Namespaces are per-cluster — a stale pick could silently
                                        // point at a different namespace sharing the name.
                                        set({
                                            aksPodParams: {
                                                ...(draft.aksPodParams as AksPodAlertParams),
                                                kubeconfigContext:
                                                    e.target.value,
                                                namespace: "",
                                            },
                                        })
                                    }
                                    className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
                                    data-testid="alert-rule-aks-context"
                                >
                                    <option value="">
                                        Configured context
                                        {configuredAksContext
                                            ? ` (${configuredAksContext})`
                                            : ""}
                                    </option>
                                    {(aksContexts ?? []).map((c) => (
                                        <option key={c.name} value={c.name}>
                                            {c.name}
                                            {c.isCurrent ? " (current)" : ""}
                                        </option>
                                    ))}
                                    {/* A rule pinned to a context no longer in the kubeconfig
                                        stays selectable — blanking it would silently retarget
                                        the rule on the next save. */}
                                    {selectedAksContext &&
                                        !(aksContexts ?? []).some(
                                            (c) =>
                                                c.name === selectedAksContext,
                                        ) && (
                                            <option value={selectedAksContext}>
                                                {selectedAksContext} (not in
                                                kubeconfig)
                                            </option>
                                        )}
                                </select>
                            </Field>
                            <Field label="Namespace">
                                {aksNamespacesError ? (
                                    <input
                                        value={
                                            draft.aksPodParams?.namespace ?? ""
                                        }
                                        onChange={(e) =>
                                            set({
                                                aksPodParams: {
                                                    ...(draft.aksPodParams as AksPodAlertParams),
                                                    namespace: e.target.value,
                                                },
                                            })
                                        }
                                        placeholder="Couldn't load namespaces — type it"
                                        className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
                                        data-testid="alert-rule-aks-namespace"
                                    />
                                ) : (
                                    <select
                                        value={
                                            draft.aksPodParams?.namespace ?? ""
                                        }
                                        onChange={(e) =>
                                            set({
                                                aksPodParams: {
                                                    ...(draft.aksPodParams as AksPodAlertParams),
                                                    namespace: e.target.value,
                                                },
                                            })
                                        }
                                        disabled={aksNamespacesLoading}
                                        className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm disabled:opacity-50"
                                        data-testid="alert-rule-aks-namespace"
                                    >
                                        <option value="">
                                            {aksNamespacesLoading
                                                ? "Loading namespaces…"
                                                : "Select namespace…"}
                                        </option>
                                        {(aksNamespaces ?? []).map((ns) => (
                                            <option key={ns} value={ns}>
                                                {ns}
                                            </option>
                                        ))}
                                        {/* Keep a stored namespace selectable even when the
                                            cluster no longer lists it. */}
                                        {draft.aksPodParams?.namespace &&
                                            !(aksNamespaces ?? []).includes(
                                                draft.aksPodParams.namespace,
                                            ) && (
                                                <option
                                                    value={
                                                        draft.aksPodParams
                                                            .namespace
                                                    }
                                                >
                                                    {
                                                        draft.aksPodParams
                                                            .namespace
                                                    }{" "}
                                                    (not listed)
                                                </option>
                                            )}
                                    </select>
                                )}
                            </Field>
                            <div className="grid grid-cols-2 gap-3">
                                <Field label="Restart threshold">
                                    <input
                                        type="number"
                                        value={
                                            draft.aksPodParams
                                                ?.restartThreshold ?? 5
                                        }
                                        onChange={(e) =>
                                            set({
                                                aksPodParams: {
                                                    ...(draft.aksPodParams as AksPodAlertParams),
                                                    restartThreshold: Number(
                                                        e.target.value,
                                                    ),
                                                },
                                            })
                                        }
                                        className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
                                    />
                                </Field>
                                <Field label="Health score threshold">
                                    <input
                                        type="number"
                                        step="0.01"
                                        value={
                                            draft.aksPodParams
                                                ?.healthScoreThreshold ?? 0.25
                                        }
                                        onChange={(e) =>
                                            set({
                                                aksPodParams: {
                                                    ...(draft.aksPodParams as AksPodAlertParams),
                                                    healthScoreThreshold:
                                                        Number(e.target.value),
                                                },
                                            })
                                        }
                                        className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
                                    />
                                </Field>
                            </div>
                        </div>
                    )}

                    {draft.source.startsWith("ServiceBus") && (
                        <div className="space-y-3 rounded-md border p-3">
                            <Field label="Namespace">
                                <select
                                    value={
                                        draft.serviceBusParams
                                            ?.namespaceConnectionAlias ?? ""
                                    }
                                    onChange={(e) =>
                                        set({
                                            serviceBusParams: {
                                                ...(draft.serviceBusParams as ServiceBusAlertParams),
                                                namespaceConnectionAlias:
                                                    e.target.value,
                                            },
                                        })
                                    }
                                    className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
                                    data-testid="alert-rule-sb-namespace"
                                >
                                    <option value="">Select namespace…</option>
                                    {(sbNamespaces ?? []).map((ns) => (
                                        <option key={ns.id} value={ns.alias}>
                                            {ns.alias}
                                        </option>
                                    ))}
                                </select>
                            </Field>
                            <Field label="Entity path">
                                <input
                                    value={
                                        draft.serviceBusParams?.entityPath ?? ""
                                    }
                                    onChange={(e) =>
                                        set({
                                            serviceBusParams: {
                                                ...(draft.serviceBusParams as ServiceBusAlertParams),
                                                entityPath: e.target.value,
                                            },
                                        })
                                    }
                                    placeholder="e.g. orders/queue"
                                    className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
                                    data-testid="alert-rule-sb-entity"
                                />
                            </Field>
                            <Field label="Message count threshold">
                                <input
                                    type="number"
                                    value={
                                        draft.serviceBusParams
                                            ?.messageCountThreshold ?? 1
                                    }
                                    onChange={(e) =>
                                        set({
                                            serviceBusParams: {
                                                ...(draft.serviceBusParams as ServiceBusAlertParams),
                                                messageCountThreshold: Number(
                                                    e.target.value,
                                                ),
                                            },
                                        })
                                    }
                                    className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
                                />
                            </Field>
                        </div>
                    )}

                    {draft.source.startsWith("Redis") && (
                        <div className="space-y-3 rounded-md border p-3">
                            <Field label="Redis cache">
                                <select
                                    value={
                                        draft.redisAlertParams
                                            ?.connectionAlias ?? ""
                                    }
                                    onChange={(e) =>
                                        set({
                                            redisAlertParams: {
                                                ...(draft.redisAlertParams as RedisAlertParams),
                                                connectionAlias: e.target.value,
                                            },
                                        })
                                    }
                                    className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
                                    data-testid="alert-rule-redis-cache"
                                >
                                    <option value="">Select cache…</option>
                                    {(redisCaches ?? []).map((c) => (
                                        <option
                                            key={c.id}
                                            value={c.displayName}
                                        >
                                            {c.displayName}
                                        </option>
                                    ))}
                                </select>
                            </Field>
                            {draft.source === "RedisMemoryUsage" ? (
                                <Field label="Memory usage threshold (%)">
                                    <input
                                        type="number"
                                        value={
                                            draft.redisAlertParams
                                                ?.memoryUsageThresholdPercent ??
                                            80
                                        }
                                        onChange={(e) =>
                                            set({
                                                redisAlertParams: {
                                                    ...(draft.redisAlertParams as RedisAlertParams),
                                                    memoryUsageThresholdPercent:
                                                        Number(e.target.value),
                                                },
                                            })
                                        }
                                        className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
                                    />
                                </Field>
                            ) : (
                                <Field label="Connected clients lower bound">
                                    <input
                                        type="number"
                                        value={
                                            draft.redisAlertParams
                                                ?.clientCountLowerBound ?? 1
                                        }
                                        onChange={(e) =>
                                            set({
                                                redisAlertParams: {
                                                    ...(draft.redisAlertParams as RedisAlertParams),
                                                    clientCountLowerBound:
                                                        Number(e.target.value),
                                                },
                                            })
                                        }
                                        className="mt-1 w-full rounded-md border bg-card px-3 py-2 text-sm"
                                    />
                                </Field>
                            )}
                        </div>
                    )}

                    <div className="rounded-md border p-3">
                        <label className="flex items-start gap-2 text-sm">
                            <input
                                type="checkbox"
                                checked={draft.aiInvestigationEnabled}
                                onChange={(e) =>
                                    set({
                                        aiInvestigationEnabled:
                                            e.target.checked,
                                    })
                                }
                                className="mt-0.5"
                                data-testid="alert-rule-ai-investigation"
                            />
                            <span>
                                <span className="font-medium">
                                    AI investigation
                                </span>
                                <span className="mt-0.5 block text-xs text-muted-foreground">
                                    When this alert fires, the agent
                                    investigates related workspace resources and
                                    posts an insight you can open in chat.
                                    Requires an active agent profile with tool
                                    calling, and the alert's resource added to
                                    the Map (Settings → Map).
                                </span>
                            </span>
                        </label>
                    </div>
                </div>

                <div className="mt-6 flex items-center justify-end gap-2">
                    {!isComplete && (
                        <span
                            className="mr-auto text-xs text-muted-foreground"
                            data-testid="alert-rule-dialog-incomplete-hint"
                        >
                            Fill in the required fields for this source before
                            saving.
                        </span>
                    )}
                    <button
                        onClick={onCancel}
                        className="rounded-md border px-4 py-2 text-sm hover:bg-accent"
                        data-testid="alert-rule-dialog-cancel-btn"
                    >
                        Cancel
                    </button>
                    <button
                        onClick={save}
                        disabled={!isComplete}
                        title={
                            !isComplete
                                ? "Fill in the required fields for the selected signal source"
                                : undefined
                        }
                        className="rounded-md bg-primary px-4 py-2 text-sm text-primary-foreground hover:opacity-90 disabled:opacity-50"
                        data-testid="alert-rule-dialog-save"
                    >
                        Save
                    </button>
                </div>
            </div>
        </Dialog>
    );
}

function Field({
    label,
    children,
}: {
    label: string;
    children: React.ReactNode;
}) {
    return (
        <div>
            <label className="text-sm font-medium">{label}</label>
            {children}
        </div>
    );
}
