import type { MonitoringAlertRule } from "../../lib/api";

/**
 * Builds the rule dialog's draft for a `state: { prefillRule }` deep link
 * (monitoring-closed-loop): feature pages navigate to Monitoring → New Rule carrying a
 * *partial* rule (source + params/threshold from the surface's own context — e.g. an AKS pod
 * view prefills namespace + restart threshold, a DLQ view prefills namespace + entity). The
 * result is always a new-rule draft (`id: ""` routes the dialog down the existing create path)
 * with the same defaults the dialog itself seeds, so an arriving surface can't unset the safe
 * defaults (`autoFixProposalsEnabled` stays opt-in, `aiInvestigationEnabled` on) unless it
 * explicitly says otherwise.
 */
export function buildPrefilledRuleDraft(
    prefill: Partial<MonitoringAlertRule>,
): MonitoringAlertRule {
    return {
        id: "",
        name: "",
        enabled: true,
        source: "AksPodHealth",
        severity: "Warning",
        intervalSeconds: 60,
        cooldownMinutes: 5,
        aiInvestigationEnabled: true,
        autoFixProposalsEnabled: false,
        ...prefill,
    };
}
