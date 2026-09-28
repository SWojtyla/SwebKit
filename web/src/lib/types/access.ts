// Access-awareness pipeline types — mirror SwebKit.Core.Security.AccessReportModels /
// AccessRequestModels. See docs/features/active/access-awareness-pipeline.md.

/** `AccessStatus` on the wire. `Denied` is a positively classified authz failure;
 * `Unknown` means "couldn't verify" — never a false deny. */
export type AccessStatus = "Ok" | "Denied" | "Unknown";

/** The remedy kind for a denied capability. Only `ArmRole` can carry an az command.
 * Wire values are PascalCase like `AccessStatus` (the sidecar's enum converters emit
 * member names unchanged). */
export type AccessRemedyKind =
    | "ArmRole"
    | "SqlGrant"
    | "RedisAcl"
    | "KubeRbac"
    | "Other";

/** Structured remedy attached to a denial (optional — older denials lack it). */
export interface AccessRemedy {
    kind: AccessRemedyKind;
    roleName: string;
    grantStatement: string | null;
    instructions: string;
}

export interface AccessDenial {
    featureArea: string;
    capability: string;
    requiredAccess: string;
    guidance: string;
    detail: string;
    remedy?: AccessRemedy | null;
    assignmentKind?: string;
}

export interface AccessProbeResult {
    featureArea: string;
    connectionKey: string;
    capability: string;
    status: AccessStatus;
    checkedAt: string;
    denial?: AccessDenial | null;
    errorSummary?: string | null;
    /** `"connectionString"` = not Entra-backed — no ARM scope or az artifacts apply. */
    authMode?: string | null;
}

export interface AccessReportEntry {
    featureArea: string;
    connectionKey: string;
    label: string;
    scopeResourceId: string | null;
    capabilities: AccessProbeResult[];
}

export interface AccessReport {
    entries: AccessReportEntry[];
    generatedAt: string;
}

export interface ResolvedPrincipal {
    objectId: string | null;
    upn: string | null;
    tenantId: string | null;
    appId: string | null;
    displayName: string | null;
}

export interface AccessRequestArtifactInput {
    featureArea: string;
    connectionKey: string;
    capability: string;
}

/** What `POST /api/access/request` returns — the copyable request block. `scope`,
 * `principal`, `azCommand` and `grantStatement` degrade to null rather than fabricate. */
export interface AccessRequestArtifact {
    role: string;
    scope: string | null;
    principal: ResolvedPrincipal | null;
    resource: string;
    summaryText: string;
    azCommand: string | null;
    grantStatement: string | null;
    remedyKind: AccessRemedyKind;
    assignmentKind: string;
    webhookConfigured: boolean;
}

// ── Phase 4 — request webhook (Power Automate / Teams Power App trigger) ─────

/** `GET /api/access/webhook` view. The trigger URL itself is never returned — it lives
 * in the OS credential store; `hasUrl` reports whether a secret is actually there.
 * `effectiveBodyTemplate` is the stored template or the built-in default. */
export interface AccessRequestWebhookConfig {
    enabled: boolean;
    hasUrl: boolean;
    urlCredentialKey: string | null;
    bodyTemplate: string | null;
    effectiveBodyTemplate: string;
}

/** `PUT /api/access/webhook` input. `url` replaces the stored secret; `clearUrl` deletes
 * it; neither leaves the stored URL untouched. */
export interface AccessRequestWebhookInput {
    enabled: boolean;
    bodyTemplate?: string | null;
    url?: string;
    clearUrl?: boolean;
}

export interface AccessRequestSendInput extends AccessRequestArtifactInput {
    justification?: string;
    /** Render the body and report back without POSTing — the settings "test" path. */
    dryRun?: boolean;
}

/** Honest outcome of `POST /api/access/request/send`. `sent` means "the trigger accepted
 * the call" — never that access was granted. `demo` marks the labeled simulated failure
 * demo mode answers with instead of a real POST. */
export interface AccessRequestSendResult {
    sent: boolean;
    dryRun: boolean;
    demo: boolean;
    statusCode: number | null;
    error: string | null;
    renderedBody: string | null;
}
