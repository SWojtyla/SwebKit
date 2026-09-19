import { describe, it, expect } from "vitest";
import { rollupGroupStatus } from "./alertRuleGroupRollup";
import type { AlertSignalStatus, MonitoringAlertRule } from "../../lib/api";

function rule(id: string): MonitoringAlertRule {
  return {
    id,
    name: id,
    enabled: true,
    source: "AksPodHealth",
    severity: "Warning",
    intervalSeconds: 60,
    cooldownMinutes: 5,
    aiInvestigationEnabled: true,
  };
}

describe("rollupGroupStatus", () => {
  it("counts nothing when there are no rules", () => {
    expect(rollupGroupStatus([], {})).toEqual({ firing: 0, error: 0 });
  });

  it("ignores rules with no known status, Ok, or Skipped", () => {
    const rules = [rule("a"), rule("b"), rule("c")];
    const statuses: Record<string, AlertSignalStatus> = { a: "Ok", b: "Skipped" };
    expect(rollupGroupStatus(rules, statuses)).toEqual({ firing: 0, error: 0 });
  });

  it("counts Firing and Error separately", () => {
    const rules = [rule("a"), rule("b"), rule("c"), rule("d")];
    const statuses: Record<string, AlertSignalStatus> = {
      a: "Firing",
      b: "Firing",
      c: "Error",
      d: "Ok",
    };
    expect(rollupGroupStatus(rules, statuses)).toEqual({ firing: 2, error: 1 });
  });
});
