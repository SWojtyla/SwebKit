import { useState } from "react";
import { BellOff, Plus, Trash2 } from "lucide-react";
import {
    useMonitoringSilences,
    useCreateMonitoringSilence,
    useDeleteMonitoringSilence,
} from "../../lib/hooks";
import { formatLocalDateTime } from "@/lib/datetime";
import {
    SILENCE_DURATIONS,
    isSilenceActive,
    isSilenceCurrent,
    silenceEndFor,
    silenceScopeLabel,
    type SilenceDuration,
} from "./silenceWindows";
import type { MonitoringAlertRule } from "../../lib/api";

/**
 * "Silences" section on the Monitoring Rules tab (monitoring-closed-loop item 3): shared
 * suppression windows — created for a deploy or maintenance, covering all rules or a named
 * subset — plus the ability to delete them early. Per-rule mutes live on the rule rows
 * themselves; this section is only for the multi-rule window model. Expired windows are kept
 * server-side but hidden here — they add noise, not information.
 */
export function SilencesSection({ rules }: { rules: MonitoringAlertRule[] }) {
    const { data: silences = [], isLoading } = useMonitoringSilences();
    const createSilence = useCreateMonitoringSilence();
    const deleteSilence = useDeleteMonitoringSilence();

    const [creating, setCreating] = useState(false);
    const [reason, setReason] = useState("");
    const [scope, setScope] = useState<string>("all");
    const [duration, setDuration] = useState<SilenceDuration>("1h");

    const ruleName = (id: string) =>
        rules.find((r) => r.id === id)?.name ?? id;
    const current = silences.filter((s) => isSilenceCurrent(s));

    const submit = () => {
        const now = new Date();
        createSilence.mutate(
            {
                startUtc: now.toISOString(),
                endUtc: silenceEndFor(duration, now),
                ruleIds: scope === "all" ? null : [scope],
                reason: reason.trim(),
            },
            {
                onSuccess: () => {
                    setCreating(false);
                    setReason("");
                    setScope("all");
                    setDuration("1h");
                },
            },
        );
    };

    return (
        <section
            className="mt-6 rounded-lg border"
            data-testid="monitoring-silences"
        >
            <div className="flex items-center justify-between border-b px-3 py-2">
                <h2 className="flex items-center gap-2 text-sm font-semibold">
                    <BellOff className="h-4 w-4 text-muted-foreground" />
                    Silences
                    {current.length > 0 && (
                        <span className="text-xs font-normal text-muted-foreground">
                            ({current.length})
                        </span>
                    )}
                </h2>
                <button
                    onClick={() => setCreating((v) => !v)}
                    className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent"
                    data-testid="monitoring-silence-add"
                >
                    <Plus className="h-3 w-3" />
                    New silence
                </button>
            </div>

            {creating && (
                <div
                    className="flex flex-wrap items-end gap-2 border-b px-3 py-2"
                    data-testid="monitoring-silence-form"
                >
                    <label className="flex min-w-40 flex-1 flex-col gap-1 text-xs text-muted-foreground">
                        Reason
                        <input
                            value={reason}
                            onChange={(e) => setReason(e.target.value)}
                            placeholder="e.g. weekend deploy freeze"
                            className="rounded-md border bg-card px-2 py-1.5 text-sm text-foreground"
                            data-testid="monitoring-silence-reason"
                        />
                    </label>
                    <label className="flex flex-col gap-1 text-xs text-muted-foreground">
                        Rules
                        <select
                            value={scope}
                            onChange={(e) => setScope(e.target.value)}
                            className="rounded-md border bg-card px-2 py-1.5 text-sm text-foreground"
                            data-testid="monitoring-silence-scope"
                        >
                            <option value="all">All rules</option>
                            {rules.map((r) => (
                                <option key={r.id} value={r.id}>
                                    {r.name}
                                </option>
                            ))}
                        </select>
                    </label>
                    <label className="flex flex-col gap-1 text-xs text-muted-foreground">
                        Duration
                        <select
                            value={duration}
                            onChange={(e) =>
                                setDuration(e.target.value as SilenceDuration)
                            }
                            className="rounded-md border bg-card px-2 py-1.5 text-sm text-foreground"
                            data-testid="monitoring-silence-duration"
                        >
                            {SILENCE_DURATIONS.map((d) => (
                                <option key={d.id} value={d.id}>
                                    {d.label}
                                </option>
                            ))}
                        </select>
                    </label>
                    <button
                        onClick={submit}
                        disabled={createSilence.isPending}
                        className="rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
                        data-testid="monitoring-silence-save"
                    >
                        Create
                    </button>
                </div>
            )}

            {isLoading ? (
                <div className="px-3 py-4 text-xs text-muted-foreground">
                    Loading silences…
                </div>
            ) : current.length === 0 ? (
                <div
                    className="px-3 py-4 text-xs text-muted-foreground"
                    data-testid="monitoring-silences-empty"
                >
                    No active or upcoming silences — every enabled rule alerts
                    normally.
                </div>
            ) : (
                <ul>
                    {current.map((s) => {
                        const active = isSilenceActive(s);
                        return (
                            <li
                                key={s.id}
                                className="flex items-center gap-3 border-b px-3 py-2 last:border-0"
                                data-testid={`monitoring-silence-row-${s.id}`}
                            >
                                <span
                                    className={`h-2 w-2 shrink-0 rounded-full ${active ? "bg-warning" : "bg-gray-400"}`}
                                    title={active ? "Active now" : "Upcoming"}
                                    data-testid={`monitoring-silence-state-${s.id}`}
                                />
                                <div className="min-w-0 flex-1 text-sm">
                                    <span className="font-medium">
                                        {s.reason || "Silence"}
                                    </span>
                                    <span className="text-muted-foreground">
                                        {" "}
                                        · {silenceScopeLabel(s, ruleName)} ·{" "}
                                        {formatLocalDateTime(s.startUtc)} →{" "}
                                        {formatLocalDateTime(s.endUtc)}
                                    </span>
                                </div>
                                <button
                                    onClick={() => deleteSilence.mutate(s.id)}
                                    disabled={deleteSilence.isPending}
                                    className="rounded p-1 hover:bg-accent disabled:opacity-50"
                                    title="Delete this silence"
                                    data-testid={`monitoring-silence-delete-${s.id}`}
                                >
                                    <Trash2 className="h-3.5 w-3.5" />
                                </button>
                            </li>
                        );
                    })}
                </ul>
            )}
        </section>
    );
}
