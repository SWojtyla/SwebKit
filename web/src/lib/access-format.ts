import type { AccessRequestArtifact, AccessStatus } from "./types";

// ── Access report display helpers (pure — unit-tested) ──────────────────────

const CAPABILITY_LABELS: Record<string, string> = {
    "servicebus.manage": "Manage entities",
    "servicebus.peek": "Peek messages",
    "servicebus.send": "Send messages",
    "sql.query": "Run queries",
    "sql.metadata": "Browse schema",
    "redis.data": "Read data",
    "storage.blobs": "List & read blobs",
    "kubernetes.read": "Read cluster",
    "observability.logs": "Query logs",
    "monitoring.sources": "Read monitoring signals",
};

/** Human label for a dotted capability — known capabilities get friendly names,
 * unknown ones fall back to the dotted key (honest, never silent). */
export function capabilityDisplayName(capability: string): string {
    return CAPABILITY_LABELS[capability] ?? capability;
}

/** Feature-area key → display label. */
export function featureAreaLabel(area: string): string {
    const labels: Record<string, string> = {
        ServiceBus: "Service Bus",
        Sql: "SQL",
        Redis: "Redis",
        Storage: "Storage",
        Aks: "AKS",
        Observability: "Observability",
        Monitoring: "Monitoring",
    };
    return labels[area] ?? area;
}

export function accessStatusLabel(status: AccessStatus): string {
    switch (status) {
        case "Ok":
            return "OK";
        case "Denied":
            return "Denied";
        case "Unknown":
            return "Unknown";
    }
}

/** Who the artifact asks access for — mirrors the endpoint's degrade order. */
export function principalDisplay(
    principal: AccessRequestArtifact["principal"],
): string {
    if (!principal) return "signed-in identity (unresolved)";
    if (principal.upn) return principal.upn;
    if (principal.objectId) {
        return principal.appId
            ? `${principal.objectId} (app ${principal.appId})`
            : principal.objectId;
    }
    return "signed-in identity (unresolved)";
}

/**
 * The plain-text block "Copy request" puts on the clipboard — self-contained enough to
 * paste into a Teams message or ticket. Everything optional degrades to an explicit
 * "ask your admin" line rather than being dropped silently.
 */
export function buildAccessRequestText(artifact: AccessRequestArtifact): string {
    const lines: string[] = [artifact.summaryText, ""];
    lines.push(`Access needed: ${artifact.role}`);
    lines.push(`Resource: ${artifact.resource}`);
    lines.push(
        `Scope: ${artifact.scope ?? "unknown — ask your admin for the resource's ARM id"}`,
    );
    lines.push(`Principal: ${principalDisplay(artifact.principal)}`);
    if (artifact.grantStatement) {
        lines.push("", `Grant statement:`, artifact.grantStatement);
    }
    if (artifact.azCommand) {
        lines.push("", "Command:", artifact.azCommand);
    }
    return lines.join("\n");
}
