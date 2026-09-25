import { useCallback, useMemo, useState, type MouseEvent } from "react";
import {
    useAksCronJobs,
    useAksSuspendCronJob,
    useAksTriggerCronJob,
    useAksSetCronJobSchedule,
} from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksActions } from "./shared/aks-workspace-context";
import { CronJobScheduleDialog } from "./CronJobScheduleDialog";
import { nextCronRun } from "@/lib/cron";
import { formatLocalDateTime } from "@/lib/datetime";
import type { ContextMenuItem } from "./ContextMenu";
import type { CronJobInfo } from "@/lib/types";

interface CronJobsTabProps {
    ns: string;
    isMulti?: boolean;
}

export function CronJobsTab({ ns, isMulti }: CronJobsTabProps) {
    const { data: cronjobs, isLoading, error } = useAksCronJobs(ns);
    const ws = useAksActions();
    const suspendMutation = useAksSuspendCronJob();
    const triggerMutation = useAksTriggerCronJob();
    const scheduleMutation = useAksSetCronJobSchedule();

    const [scheduleTarget, setScheduleTarget] = useState<CronJobInfo | null>(
        null,
    );

    const toggle = useCallback(
        (cj: CronJobInfo) => {
            const next = !cj.suspend;
            const action = next ? "suspend" : "resume";
            ws.requestConfirm({
                message: `${action === "suspend" ? "Suspend" : "Resume"} cronjob "${cj.name}"?`,
                resourceName: cj.name,
                onConfirm: () =>
                    suspendMutation.mutate({
                        ns: cj.namespace,
                        name: cj.name,
                        suspend: next,
                    }),
            });
        },
        [ws, suspendMutation],
    );

    const handleSaveSchedule = useCallback(
        (schedule: string) => {
            if (!scheduleTarget) return;
            const cj = scheduleTarget;
            setScheduleTarget(null);
            ws.requestConfirm({
                message: `Change schedule for "${cj.name}" to "${schedule}"?`,
                resourceName: cj.name,
                onConfirm: () =>
                    scheduleMutation.mutate({
                        ns: cj.namespace,
                        name: cj.name,
                        schedule,
                    }),
            });
        },
        [ws, scheduleTarget, scheduleMutation],
    );

    const buildMenu = useCallback(
        (cj: CronJobInfo): ContextMenuItem[] => [
            {
                label: "Copy name",
                icon: "📋",
                onClick: () => ws.copyToClipboard(cj.name),
            },
            {
                label: "View YAML",
                icon: "{ }",
                onClick: () => ws.openYaml("cronjob", cj.name, cj.namespace),
            },
            {
                label: "Edit schedule…",
                icon: "🕐",
                onClick: () => setScheduleTarget(cj),
            },
            { label: "", separator: true, onClick: () => {} },
            {
                label: "Trigger",
                icon: "▶",
                onClick: () =>
                    triggerMutation.mutate({
                        ns: cj.namespace,
                        name: cj.name,
                    }),
                disabled: triggerMutation.isPending,
            },
            {
                label: cj.suspend ? "Resume" : "Suspend",
                icon: cj.suspend ? "▶" : "⏸",
                onClick: () => toggle(cj),
            },
        ],
        [ws, triggerMutation, toggle],
    );

    const handleRowContextMenu = useCallback(
        (e: MouseEvent<HTMLTableRowElement>, cj: CronJobInfo) =>
            ws.showContextMenu(e, buildMenu(cj)),
        [ws, buildMenu],
    );

    const columns: Column<CronJobInfo>[] = useMemo(
        () => [
            {
                header: "Schedule",
                cell: (cj) => (
                    <button
                        onClick={(e) => {
                            e.stopPropagation();
                            setScheduleTarget(cj);
                        }}
                        title={`Edit schedule${cj.timeZone ? ` (evaluates in ${cj.timeZone})` : ""}`}
                        className="rounded px-1 font-mono text-xs hover:bg-accent/50 hover:underline"
                        data-testid={`cronjob-schedule-${cj.name}`}
                    >
                        {cj.schedule ?? "—"}
                    </button>
                ),
            },
            {
                header: "Suspend",
                cell: (cj) =>
                    cj.suspend ? (
                        <span className="text-warning">Yes</span>
                    ) : (
                        <span className="text-success">No</span>
                    ),
                sortValue: (cj) => (cj.suspend ? 0 : 1),
            },
            {
                header: "Active",
                cell: (cj) => cj.activeCount,
                sortValue: (cj) => cj.activeCount,
            },
            {
                header: "Next Run",
                cell: (cj) => {
                    if (cj.suspend || !cj.schedule) {
                        return (
                            <span
                                className="text-xs text-muted-foreground"
                                title={
                                    cj.suspend
                                        ? "CronJob is suspended"
                                        : undefined
                                }
                                data-testid={`cronjob-nextrun-${cj.name}`}
                            >
                                —
                            </span>
                        );
                    }
                    const next = nextCronRun(cj.schedule, {
                        timeZone: cj.timeZone,
                    });
                    return (
                        <span
                            className="text-xs text-muted-foreground"
                            title={
                                cj.timeZone
                                    ? `Schedule evaluates in ${cj.timeZone}; shown in your local time`
                                    : "Shown in your local time"
                            }
                            data-testid={`cronjob-nextrun-${cj.name}`}
                        >
                            {next ? formatLocalDateTime(next) : "—"}
                        </span>
                    );
                },
            },
            {
                header: "Last Schedule",
                cell: (cj) => (
                    <span className="text-xs text-muted-foreground">
                        {formatLocalDateTime(cj.lastScheduleTime) || "—"}
                    </span>
                ),
            },
            {
                header: "Last Success",
                cell: (cj) => (
                    <span className="text-xs text-muted-foreground">
                        {formatLocalDateTime(cj.lastSuccessfulTime) || "—"}
                    </span>
                ),
            },
            {
                header: "Actions",
                cell: (cj) => (
                    <button
                        onClick={(e) => {
                            e.stopPropagation();
                            toggle(cj);
                        }}
                        disabled={suspendMutation.isPending}
                        title={
                            suspendMutation.isPending ? "Updating…" : undefined
                        }
                        className="rounded border border-border px-2 py-1 text-xs hover:bg-accent/50"
                        data-testid={`cronjob-suspend-${cj.name}`}
                    >
                        {cj.suspend ? "Resume" : "Suspend"}
                    </button>
                ),
            },
        ],
        [toggle, suspendMutation.isPending],
    );

    return (
        <>
            <ResourceTable
                data={cronjobs}
                isLoading={isLoading}
                error={error}
                isMulti={isMulti}
                testIdPrefix="cronjob"
                tableBodyTestId="cronjobs-table-body"
                emptyMessage="No cron jobs found"
                onRowClick={(cj) =>
                    ws.openYaml("cronjob", cj.name, cj.namespace)
                }
                onRowContextMenu={handleRowContextMenu}
                columns={columns}
            />

            {scheduleTarget && (
                <CronJobScheduleDialog
                    cronJob={scheduleTarget}
                    isSaving={scheduleMutation.isPending}
                    onCancel={() => setScheduleTarget(null)}
                    onSave={handleSaveSchedule}
                />
            )}
        </>
    );
}
