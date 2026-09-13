import type { AlertSignalStatus, MonitoringAlertRule } from "../../lib/api";

export interface AlertRuleGroupRollup {
  firing: number;
  error: number;
}

/**
 * Summarizes a group's live signal statuses into firing/error counts, so a collapsed group header
 * (which otherwise shows only a static rule count) can still surface "something in here needs
 * attention" without expanding it. Rules with no known status yet (never evaluated) or in a benign
 * state (`Ok`/`Skipped`) don't contribute to either count.
 */
export function rollupGroupStatus(
  rules: MonitoringAlertRule[],
  statuses: Record<string, AlertSignalStatus>,
): AlertRuleGroupRollup {
  let firing = 0;
  let error = 0;
  for (const rule of rules) {
    const status = statuses[rule.id];
    if (status === "Firing") firing += 1;
    else if (status === "Error") error += 1;
  }
  return { firing, error };
}
