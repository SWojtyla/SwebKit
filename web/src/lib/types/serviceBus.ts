export interface ServiceBusNamespace {
    id: string;
    alias: string;
    fullyQualifiedNamespace: string;
    /**
     * Must match the C# `SbAuthMode` enum member names exactly — the sidecar deserializes
     * this as an enum, and an unknown value fails the whole profile save. This said
     * `"Entra"`, which is not a member, so selecting Entra ID in Settings was rejected and
     * silently reverted. Entra ID auth is `DefaultAzureCredential`.
     *
     * The C# enum also has a `ServicePrincipal` member, but no code path (UI or sidecar
     * connection factory) actually implements it — the active connection factory only
     * branches on `ConnectionString` vs. everything else falling through to
     * `DefaultAzureCredential`, so selecting it would silently behave like Entra ID with no
     * indication why. Deliberately omitted here until it's really wired up end to end; add it
     * back only alongside real client id/secret (or cert) fields and sidecar support.
     */
    authMode: "DefaultAzureCredential" | "ConnectionString";
    credentialKey: string;
    transportType: "Amqp" | "AmqpWebSockets";
    createdAt: string;
}

export interface SbMessageTemplate {
    id: string;
    name: string;
    body: string;
    contentType: string | null;
    subject: string | null;
    correlationId: string | null;
    properties: Record<string, string>;
    createdAt: string;
}

// ── User Settings ────────────────────────────────────────────────────────────

export interface SbNamespaceInfo {
    name: string;
    endpoint: string;
}

export interface SbEntityInfo {
    name: string;
    entityPath: string;
    stats: SbEntityStats | null;
    isDisabled: boolean;
    isTopic: boolean;
    isSubscription: boolean;
    topicName: string | null;
    /**
     * For a topic: dead-lettered messages summed across its subscriptions, so a collapsed topic can show
     * its backlog without the tree fetching every topic's subscriptions. `null` elsewhere or when unreadable.
     */
    subscriptionDeadLetterCount: number | null;
}

export interface SbEntityStats {
    activeMessageCount: number;
    deadLetterMessageCount: number;
    scheduledMessageCount: number;
    transferCount: number;
    updatedAt: string | null;
}

export interface SbMessage {
    messageId: string;
    correlationId: string | null;
    subject: string | null;
    contentType: string | null;
    body: string;
    applicationProperties: Record<string, unknown>;
    systemProperties: SbSystemProperties | null;
    deadLetterReason: string | null;
    deadLetterErrorDescription: string | null;
    enqueuedAt: string;
    deliveryCount: number;
    lockToken: string | null;
    sequenceNumber: number | null;
    sessionId: string | null;
}

export interface SbSystemProperties {
    expiresAt: string | null;
    lockedUntil: string | null;
    enqueuedSequenceNumber: string | null;
    partitionKey: string | null;
}

export interface ScheduledMessageEntry {
    id: string;
    namespaceId: string;
    entityPath: string;
    sequenceNumber: number;
    scheduledEnqueueTime: string;
    messageId: string | null;
    subject: string | null;
    correlationId: string | null;
    createdAt: string;
}

export interface ResubmitRequest {
    sequenceNumbers: string[];
    targetEntityPath: string | null;
    remapRules: RemapRules | null;
}

export interface RemapRules {
    overrideSubject: string | null;
    overrideCorrelationId: string | null;
    propertyRenames: Record<string, string>;
    propertyRemoves: string[];
}

// ── AKS / Kubernetes ─────────────────────────────────────────────────────────
