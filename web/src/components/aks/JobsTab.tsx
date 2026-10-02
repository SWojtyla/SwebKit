import { useCallback, type MouseEvent } from "react";
import { useAksJobs } from "@/lib/hooks";
import { ResourceTable, type Column } from "./shared/ResourceTable";
import { useAksActions } from "./shared/aks-workspace-context";
import type { ContextMenuItem } from "./ContextMenu";
import type { AksQueryTarget, JobInfo } from "@/lib/types";

interface JobsTabProps {
    targets: AksQueryTarget[];
    isMulti?: boolean;
    showContext?: boolean;
}

function jobStatusRank(job: JobInfo): number {
    if (job.status === "Failed") return -1;
    if (job.status === "Completed") return 1;
    return 0;
}

const columns: Column<JobInfo>[] = [
    {
        header: "Status",
        cell: (job) => (
            <span
                className={
                    job.status === "Completed"
                        ? "text-success"
                        : job.status === "Failed"
                          ? "text-destructive"
                          : "text-warning"
                }
            >
                {job.status}
            </span>
        ),
        sortValue: jobStatusRank,
    },
    {
        header: "Active",
        cell: (job) => job.active,
        sortValue: (job) => job.active,
    },
    {
        header: "Succeeded",
        cell: (job) => <span className="text-success">{job.succeeded}</span>,
        sortValue: (job) => job.succeeded,
    },
    {
        header: "Failed",
        cell: (job) => <span className="text-destructive">{job.failed}</span>,
        sortValue: (job) => job.failed,
    },
    {
        header: "Completions",
        cell: (job) => (
            <span className="text-muted-foreground">
                {job.desiredCompletions ?? "—"}
            </span>
        ),
    },
    {
        header: "Source",
        cell: (job) => (
            <span className="text-xs text-muted-foreground">
                {job.sourceKind && job.sourceName
                    ? `${job.sourceKind}/${job.sourceName}`
                    : "—"}
            </span>
        ),
    },
];

export function JobsTab({ targets, isMulti, showContext }: JobsTabProps) {
    const { data: jobs, isLoading, error, contextErrors } = useAksJobs(targets);
    const ws = useAksActions();

    const buildMenu = useCallback(
        (job: JobInfo): ContextMenuItem[] => [
            {
                label: "Copy name",
                icon: "📋",
                onClick: () => ws.copyToClipboard(job.name),
            },
            {
                label: "View YAML",
                icon: "{ }",
                onClick: () =>
                    ws.openYaml("job", job.name, job.namespace, job.context),
            },
        ],
        [ws],
    );

    const handleRowContextMenu = useCallback(
        (e: MouseEvent<HTMLTableRowElement>, job: JobInfo) =>
            ws.showContextMenu(e, buildMenu(job)),
        [ws, buildMenu],
    );

    return (
        <ResourceTable
            data={jobs}
            isLoading={isLoading}
            error={error}
            isMulti={isMulti}
            showContext={showContext}
            contextErrors={contextErrors}
            testIdPrefix="job"
            tableBodyTestId="jobs-table-body"
            emptyMessage="No jobs found"
            onRowClick={(job) =>
                ws.openYaml("job", job.name, job.namespace, job.context)
            }
            onRowContextMenu={handleRowContextMenu}
            columns={columns}
            defaultSort={{ sortValue: jobStatusRank, direction: "asc" }}
        />
    );
}
