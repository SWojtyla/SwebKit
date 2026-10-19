import { useMemo, useState } from "react";
import { Dialog } from "@/components/shared/Dialog";
import { X } from "lucide-react";
import type { CronJobInfo } from "@/lib/types";
import {
    buildCronExpression,
    describeCronExpression,
    detectCronPreset,
    isValidCronExpression,
    nextCronRun,
    type CronPreset,
} from "@/lib/cron";
import { formatLocalDateTime } from "@/lib/datetime";

const PRESETS: { value: CronPreset; label: string }[] = [
    { value: "everyNMinutes", label: "Every N minutes" },
    { value: "hourly", label: "Hourly" },
    { value: "daily", label: "Daily" },
    { value: "weekly", label: "Weekly" },
    { value: "monthly", label: "Monthly" },
    { value: "yearly", label: "Yearly" },
    { value: "custom", label: "Custom expression" },
];

const WEEKDAYS = [
    "Sunday",
    "Monday",
    "Tuesday",
    "Wednesday",
    "Thursday",
    "Friday",
    "Saturday",
];

const MONTHS = [
    "January",
    "February",
    "March",
    "April",
    "May",
    "June",
    "July",
    "August",
    "September",
    "October",
    "November",
    "December",
];

/**
 * Friendly schedule editor for a CronJob: presets cover the common cases without
 * ever showing cron syntax, and "Custom expression" keeps full control available
 * with live validation + a preview of the resulting expression and next run.
 */
export function CronJobScheduleDialog({
    cronJob,
    isSaving,
    onCancel,
    onSave,
}: {
    cronJob: CronJobInfo;
    isSaving: boolean;
    onCancel: () => void;
    onSave: (schedule: string) => void;
}) {
    const initial = useMemo(
        () => detectCronPreset(cronJob.schedule ?? ""),
        [cronJob.schedule],
    );
    const [preset, setPreset] = useState<CronPreset>(initial.preset);
    const [everyN, setEveryN] = useState(initial.everyN ?? 5);
    const [minute, setMinute] = useState(initial.minute ?? 0);
    const [hour, setHour] = useState(initial.hour ?? 0);
    const [weekday, setWeekday] = useState(initial.weekday ?? 1);
    const [dayOfMonth, setDayOfMonth] = useState(initial.dayOfMonth ?? 1);
    const [month, setMonth] = useState(initial.month ?? 1);
    const [custom, setCustom] = useState(
        initial.preset === "custom" ? (cronJob.schedule ?? "") : "",
    );

    const schedule =
        preset === "custom"
            ? custom.trim()
            : buildCronExpression({
                  preset,
                  everyN,
                  minute,
                  hour,
                  weekday,
                  dayOfMonth,
                  month,
              });

    const valid = preset === "custom" ? isValidCronExpression(schedule) : true;
    const nextRun = valid
        ? nextCronRun(schedule, { timeZone: cronJob.timeZone })
        : null;
    const description = valid ? describeCronExpression(schedule) : null;

    const numberInput =
        "mt-1 block h-7 w-24 rounded-md border bg-surface-1 px-2 text-xs tabular-nums";

    return (
        <Dialog
            onClose={onCancel}
            label={`Edit schedule for ${cronJob.name}`}
            testId="cronjob-schedule-dialog"
            widthClassName="w-[440px]"
        >
            <div className="flex items-start justify-between gap-3 border-b px-4 py-3">
                <div className="min-w-0">
                    <h2 className="text-sm font-semibold">Edit schedule</h2>
                    <p
                        className="truncate text-xs text-fg-muted"
                        title={`${cronJob.namespace}/${cronJob.name}`}
                    >
                        {cronJob.namespace} /{" "}
                        <span className="font-medium text-fg">
                            {cronJob.name}
                        </span>
                    </p>
                </div>
                <button
                    onClick={onCancel}
                    className="shrink-0 text-fg-muted hover:text-fg"
                    aria-label="Close"
                    data-testid="cronjob-schedule-close"
                >
                    <X className="h-4 w-4" />
                </button>
            </div>

            <div className="space-y-3 p-4">
                <label className="block text-xs">
                    Runs
                    <select
                        value={preset}
                        onChange={(e) =>
                            setPreset(e.target.value as CronPreset)
                        }
                        className="mt-1 block h-7 w-full rounded-md border bg-surface-1 px-2 text-xs"
                        data-testid="cronjob-schedule-preset"
                    >
                        {PRESETS.map((p) => (
                            <option key={p.value} value={p.value}>
                                {p.label}
                            </option>
                        ))}
                    </select>
                </label>

                {preset === "everyNMinutes" && (
                    <label className="block text-xs">
                        Every (minutes)
                        <input
                            type="number"
                            min={1}
                            max={59}
                            value={everyN}
                            onChange={(e) =>
                                setEveryN(
                                    Math.min(
                                        59,
                                        Math.max(
                                            1,
                                            parseInt(e.target.value, 10) || 1,
                                        ),
                                    ),
                                )
                            }
                            className={numberInput}
                            data-testid="cronjob-schedule-everyn"
                        />
                    </label>
                )}

                {preset === "hourly" && (
                    <label className="block text-xs">
                        At minute
                        <input
                            type="number"
                            min={0}
                            max={59}
                            value={minute}
                            onChange={(e) =>
                                setMinute(
                                    Math.min(
                                        59,
                                        Math.max(
                                            0,
                                            parseInt(e.target.value, 10) || 0,
                                        ),
                                    ),
                                )
                            }
                            className={numberInput}
                            data-testid="cronjob-schedule-minute"
                        />
                    </label>
                )}

                {(preset === "daily" ||
                    preset === "weekly" ||
                    preset === "monthly" ||
                    preset === "yearly") && (
                    <div className="flex flex-wrap items-end gap-3">
                        {preset === "weekly" && (
                            <label className="text-xs">
                                On
                                <select
                                    value={weekday}
                                    onChange={(e) =>
                                        setWeekday(parseInt(e.target.value, 10))
                                    }
                                    className="mt-1 block h-7 rounded-md border bg-surface-1 px-2 text-xs"
                                    data-testid="cronjob-schedule-weekday"
                                >
                                    {WEEKDAYS.map((d, i) => (
                                        <option key={d} value={i}>
                                            {d}
                                        </option>
                                    ))}
                                </select>
                            </label>
                        )}
                        {preset === "monthly" && (
                            <label className="text-xs">
                                On day
                                <input
                                    type="number"
                                    min={1}
                                    max={31}
                                    value={dayOfMonth}
                                    onChange={(e) =>
                                        setDayOfMonth(
                                            Math.min(
                                                31,
                                                Math.max(
                                                    1,
                                                    parseInt(
                                                        e.target.value,
                                                        10,
                                                    ) || 1,
                                                ),
                                            ),
                                        )
                                    }
                                    className={numberInput}
                                    data-testid="cronjob-schedule-dom"
                                />
                            </label>
                        )}
                        {preset === "yearly" && (
                            <>
                                <label className="text-xs">
                                    In
                                    <select
                                        value={month}
                                        onChange={(e) =>
                                            setMonth(
                                                parseInt(e.target.value, 10),
                                            )
                                        }
                                        className="mt-1 block h-7 rounded-md border bg-surface-1 px-2 text-xs"
                                        data-testid="cronjob-schedule-month"
                                    >
                                        {MONTHS.map((m, i) => (
                                            <option key={m} value={i + 1}>
                                                {m}
                                            </option>
                                        ))}
                                    </select>
                                </label>
                                <label className="text-xs">
                                    On day
                                    <input
                                        type="number"
                                        min={1}
                                        max={31}
                                        value={dayOfMonth}
                                        onChange={(e) =>
                                            setDayOfMonth(
                                                Math.min(
                                                    31,
                                                    Math.max(
                                                        1,
                                                        parseInt(
                                                            e.target.value,
                                                            10,
                                                        ) || 1,
                                                    ),
                                                ),
                                            )
                                        }
                                        className={numberInput}
                                        data-testid="cronjob-schedule-dom"
                                    />
                                </label>
                            </>
                        )}
                        <label className="text-xs">
                            At
                            <input
                                type="time"
                                value={`${String(hour).padStart(2, "0")}:${String(minute).padStart(2, "0")}`}
                                onChange={(e) => {
                                    const [h, m] = e.target.value.split(":");
                                    setHour(parseInt(h, 10) || 0);
                                    setMinute(parseInt(m, 10) || 0);
                                }}
                                className="mt-1 block h-7 rounded-md border bg-surface-1 px-2 text-xs tabular-nums"
                                data-testid="cronjob-schedule-time"
                            />
                        </label>
                    </div>
                )}

                {preset === "custom" && (
                    <label className="block text-xs">
                        Cron expression
                        <input
                            type="text"
                            value={custom}
                            onChange={(e) => setCustom(e.target.value)}
                            placeholder="minute hour day-of-month month weekday"
                            spellCheck={false}
                            className="mt-1 block h-7 w-full rounded-md border bg-surface-1 px-2 font-mono text-xs"
                            data-testid="cronjob-schedule-custom"
                        />
                    </label>
                )}

                <div
                    className="rounded-md border bg-sunken px-3 py-2 text-xs"
                    data-testid="cronjob-schedule-preview"
                >
                    <div className="flex items-center gap-2">
                        <span className="text-fg-muted">Schedule:</span>
                        <code className="font-mono">{schedule || "—"}</code>
                    </div>
                    {description && (
                        <div className="mt-1 text-fg-muted">{description}</div>
                    )}
                    {!valid && (
                        <div
                            className="mt-1 text-state-blocked-ink"
                            data-testid="cronjob-schedule-invalid"
                        >
                            Invalid cron expression — use 5 fields (minute hour
                            day-of-month month weekday) or a @macro.
                        </div>
                    )}
                    {valid && (
                        <div className="mt-1">
                            <span className="text-fg-muted">
                                Next run (local time):
                            </span>{" "}
                            <span data-testid="cronjob-schedule-next-run">
                                {nextRun ? formatLocalDateTime(nextRun) : "—"}
                            </span>
                        </div>
                    )}
                    {cronJob.timeZone && (
                        <div className="mt-1 text-fg-muted">
                            Schedule evaluates in {cronJob.timeZone}{" "}
                            (spec.timeZone)
                        </div>
                    )}
                </div>
            </div>

            <div className="flex justify-end gap-2 border-t px-4 py-3">
                <button
                    onClick={onCancel}
                    disabled={isSaving}
                    className="h-7 rounded-md border bg-surface-1 px-3 text-xs text-fg-muted hover:bg-surface-2 hover:text-fg disabled:opacity-50"
                    data-testid="cronjob-schedule-cancel"
                >
                    Cancel
                </button>
                <button
                    onClick={() => onSave(schedule)}
                    disabled={isSaving || !valid || !schedule}
                    title={
                        isSaving
                            ? "Saving…"
                            : !valid
                              ? "Fix the cron expression first"
                              : undefined
                    }
                    className="h-7 rounded-md bg-primary px-3 text-xs text-primary-foreground hover:bg-accent-hover disabled:opacity-50"
                    data-testid="cronjob-schedule-save"
                >
                    {isSaving ? "Saving…" : "Save"}
                </button>
            </div>
        </Dialog>
    );
}
