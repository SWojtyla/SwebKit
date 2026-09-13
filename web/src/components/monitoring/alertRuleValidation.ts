import type { MonitoringAlertRule } from "../../lib/api";

/**
 * A rule can be saved today with only a `name` filled in — the Save button's only check — even
 * though its selected `source` needs specific params to ever actually evaluate anything (an AKS
 * namespace, a Service Bus namespace+entity, or a Redis cache alias). Such a rule persists, looks
 * configured, and silently never fires. This mirrors `AlertRuleDialog`'s own
 * `source.startsWith(...)` branches for which params block is shown/relevant, so the two can't
 * drift apart.
 */
export function isAlertRuleComplete(rule: MonitoringAlertRule): boolean {
  if (!rule.name.trim()) return false;

  if (rule.source.startsWith("Aks")) {
    return Boolean(rule.aksPodParams?.namespace?.trim());
  }
  if (rule.source.startsWith("ServiceBus")) {
    return Boolean(
      rule.serviceBusParams?.namespaceConnectionAlias?.trim() && rule.serviceBusParams?.entityPath?.trim(),
    );
  }
  if (rule.source.startsWith("Redis")) {
    return Boolean(rule.redisAlertParams?.connectionAlias?.trim());
  }

  return true;
}
