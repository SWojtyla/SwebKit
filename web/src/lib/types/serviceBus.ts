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
    /** Optional full ARM resource id of the namespace — scopes access-request artifacts
     * for Entra-authenticated namespaces. Never derived from the FQDN. */
    resourceId?: string | null;
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
    /**
     * `RequiresSession` from the queue/subscription properties. Session-enabled entities reject
     * the plain receivers every settle path uses (complete/dead-letter/resubmit/resend/purge), so
     * the UI badges them and disables those actions rather than letting them 502 on the broker.
     */
    requiresSession: boolean;
}

/**
 * One session's footprint inside the peek window — produced by
 * `GET .../entities/{path}/sessions` (and by `groupMessagesBySession` client-side over the
 * loaded window when the endpoint hasn't answered yet). Sessions beyond the window are absent.
 */
export interface SbSessionSummary {
    sessionId: string;
    messageCount: number;
    firstEnqueuedAt: string;
    lastEnqueuedAt: string;
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

// ── Power ops (reach-message + DLQ triage) ──────────────────────────────────
// Shapes mirror ServiceBusEndpoints' request/response records. Enums travel as
// strings — the sidecar registers JsonStringEnumConverter for HTTP JSON.

/** What the reach-message op does when it reaches the target sequence. */
export type SbReachTargetAction = "Complete" | "DeadLetter" | "Resubmit";

/** Body for reach-message preview and start. */
export interface ReachMessageRequest {
    /** The message to reach, by broker sequence number. */
    targetSequenceNumber: number;
    action: SbReachTargetAction;
    /**
     * Resubmit only: when true the target's copy lands at the tail AFTER the
     * restored prefix copies; when false it lands first.
     */
    restoreBeforeTarget: boolean;
    /** Park cap override — defaults to 1,000; hard ceiling 5,000 (clamped server-side). */
    maxParked?: number | null;
}

/**
 * Preview contract the wizard renders verbatim — the honest "what will happen"
 * statement: park into the DLQ, restore as new tail-appended copies. Never
 * "put back like nothing happened".
 */
export interface ReachMessagePreview {
    canStart: boolean;
    /** Why the op is refused when canStart is false. */
    refusalReason: string | null;
    /** Messages ahead of the target — null when the target is beyond the peek window. */
    prefixCount: number | null;
    /** Target sits past the peeked window's max — the prefix count is unknowable. */
    targetBeyondWindow: boolean;
    /** The park cap that applies to this op. */
    maxParked: number;
    /** What the op will do — render verbatim. */
    consequences: string[];
    /** Honest failure modes — render verbatim. */
    warnings: string[];
}

export type SbOperationState =
    | "Running"
    | "Completed"
    | "Cancelled"
    | "Failed"
    | "Interrupted"
    | "Dismissed";

export type SbOperationPhase = "Parking" | "Restoring";

/**
 * API-facing snapshot of a reach-message operation — the wizard polls this.
 * `parkedInDlq` is enriched by a live DLQ stamp scan on the list endpoint:
 * the crash-recovery truth when the journal was lost mid-op.
 */
export interface SbOperationStatus {
    id: string;
    namespaceId: string;
    entityPath: string;
    kind: string;
    targetSequenceNumber: number;
    targetAction: SbReachTargetAction;
    restoreBeforeTarget: boolean;
    state: SbOperationState;
    phase: SbOperationPhase | null;
    parkedCount: number;
    restoredCount: number;
    parkedInDlq: number | null;
    error: string | null;
    createdAt: string;
    updatedAt: string;
}

/** Body for dlq/requeue-by-filter — resubmit a whole reason/description group server-side. */
export interface DlqRequeueByFilterRequest {
    reason: string;
    description?: string | null;
    limit: number;
}

export interface DlqRequeueByFilterResult {
    resubmitted: number;
}

// ── AKS / Kubernetes ─────────────────────────────────────────────────────────
