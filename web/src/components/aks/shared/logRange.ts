/// Shared time-range options for pod log streams, single source of truth for the
/// `sinceSeconds` mapping so the single-pod and multi-pod views can never disagree about what
/// "Last 5m" means.

export type LogRange = "5m" | "10m" | "1h" | "all" | "previous";

export interface LogRangeOption {
  label: string;
  value: LogRange;
  since?: number;
}

export const rangeOptions: LogRangeOption[] = [
  { label: "Last 5m", value: "5m", since: 300 },
  { label: "Last 10m", value: "10m", since: 600 },
  { label: "Last 1h", value: "1h", since: 3600 },
  { label: "All", value: "all" },
  { label: "Previous container", value: "previous" },
];

// A pod's own previous instance isn't a concept that correlates across multiple pods, so the
// multi-pod correlation view drops it.
export const multiPodLogRangeOptions: LogRangeOption[] = rangeOptions.filter(
  (o) => o.value !== "previous",
);
