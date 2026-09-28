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

export type SbOperationPhase = "Parking" | "Restoring" | "Transferring";

/** `SbOperationStatus.kind` values — mirrored from SbOperationService's kind constants. */
export const SB_OP_KIND_REACH = "reach-message";
export const SB_OP_KIND_REPLAY_TO = "replay-to";

/**
 * API-facing snapshot of a journaled background operation — reach-message and
 * cross-environment replay share this shape; the replay-only fields are null/0
 * on reach ops and vice versa. `parkedInDlq` is enriched by a live DLQ stamp
 * scan on the list endpoint (reach ops only — replay ops carry no parked set;
 * their crash-recovery record is the journaled processed-set instead).
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
    // ── replay-to fields ────────────────────────────────────────────────
    /** Replay only: destination namespace id. */
    targetNamespaceId: string | null;
    /** Replay only: destination entity on the target namespace. */
    targetEntityPath: string | null;
    /** Replay only: the source was the entity's dead-letter sub-queue. */
    sourceIsDeadLetter: boolean;
    /** Replay only: source copies are settled once their clone lands (move semantics). */
    removeSource: boolean;
    /** Replay only: how many source messages were requested. */
    requestedCount: number;
    /** Replay only: copies accepted by the target so far. */
    replayedCount: number;
    /** Replay only: matched messages whose send/settle failed — retried on resume. */
    failedCount: number;
    /** Replay only: requested sequences never found in the source. */
    missingCount: number;
}

// ── Cross-environment replay (requeue to ANOTHER namespace/entity) ─────────
// Same honest contract as reach restore, one hop wider: every replayed message
// is a NEW message on the target — fresh sequence, tail-appended, delivery
// count reset — stamped SwebKit.ReplayedFrom. No transaction spans two
// namespaces, so a crash mid-run is at-least-once.

/** Body for `replay-to/preview` and `replay-to/start`. */
export interface ReplayToRequest {
    /** Source sequence numbers to replay — required, capped at 5,000 server-side. */
    sequenceNumbers: number[];
    /** When true the source copies come from the entity's dead-letter sub-queue. */
    deadLetter: boolean;
    /** Target namespace id — resolved server-side against the same profile/demo overlay. */
    targetNsId: string;
    /** Target entity path in that namespace (queue or topic path; subscriptions are receive-only). */
    targetEntityPath: string;
    /** Drop every application property on the cloned send; provenance stamps still apply. */
    scrubProperties: boolean;
    /** Drop session ids so a non-session target accepts the copies. Refused for session-required targets. */
    stripSessionId: boolean;
    /** Move semantics: complete the source copy after its clone lands on the target. */
    removeSource: boolean;
}

/** The replay preview contract — consequences and warnings are rendered verbatim. */
export interface ReplayToPreview {
    canStart: boolean;
    /** Why the op is refused when canStart is false. */
    refusalReason: string | null;
    requestedCount: number;
    /** Selected sequences visible inside the peek window — best-effort; the transfer loop is the truth. */
    matchedCount: number;
    /** Selected sequences NOT in the peek window — may be gone or past the window tail. */
    missingCount: number;
    /** Matched messages carrying a session id — drives the strip/refuse decision. */
    sessionBoundCount: number;
    targetRequiresSession: boolean;
    /** What the op will do — render verbatim. */
    consequences: string[];
    /** Honest failure modes — render verbatim. */
    warnings: string[];
}

// ── Entity properties surface (read-only) ───────────────────────────────────

/** One displayed entity property — a name, its rendered value, and the section it groups under. */
export interface SbEntityProperty {
    name: string;
    value: string;
    /** Section label — "General" / "Sizing" / "Delivery" / "Lifecycle". */
    group: string;
}

/** The properties view of one entity — read-only by design (editing is not surfaced). */
export interface SbEntityProperties {
    entityPath: string;
    /** "queue" | "topic" | "subscription". */
    entityKind: string;
    /** For subscriptions: the parent topic name; null otherwise. */
    topicName: string | null;
    requiresSession: boolean;
    properties: SbEntityProperty[];
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
