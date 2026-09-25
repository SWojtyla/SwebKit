/**
 * Mirrors `SwebKit.Core.Domain.ObservabilityConfig` — only the two fields the agent-tool-only
 * integration actually needs (which Application Insights resource to query) are surfaced in the
 * UI; the rest of the real C# shape (SavedQueries, SLOs, guided-query drafts, thresholds) backs
 * the Observability *browsing* page that was dropped from this rewrite, so there's nothing here to
 * bind to. Auth is ambient `DefaultAzureCredential` (Azure CLI/VS login) — there's deliberately no
 * `credentialKey` field; Observability doesn't use the OS credential store the way Redis/Service
 * Bus do.
 */
export interface ObservabilityConfig {
    selectedResourceId: string | null;
    selectedResourceName: string | null;
}

export interface ObservabilityResource {
    resourceId: string;
    name: string;
    subscriptionId: string;
    subscriptionName: string;
    resourceGroup: string;
    location: string;
    workspaceType?: string | null;
}
