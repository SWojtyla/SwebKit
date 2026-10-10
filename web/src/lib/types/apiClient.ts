export interface EnvironmentsResponse {
    environments: ApiEnvironment[];
    uiState: ApiClientUiState;
}

export interface ApiEnvironment {
    id: string;
    name: string;
    collectionId: string | null;
    variables: EnvironmentVariable[];
    createdAt: string;
    updatedAt: string;
    /**
     * Frontend-assigned provenance — set when environments from linked roots are
     * merged into the workspace list. Never persisted; strip it before sending an
     * environment back to any endpoint (`origin.kind === "linked"` envs route to
     * `/api/linked-roots/{rootId}/environments`, not the internal PUT).
     */
    origin?: ApiEnvironmentOrigin;
}

export interface ApiEnvironmentOrigin {
    kind: "internal" | "linked";
    /** Owning linked root — present when `kind === "linked"`. */
    rootId?: string;
    /** The `.swebenv.json` this environment was read from (linked only). */
    filePath?: string;
}

export interface EnvironmentVariable {
    key: string;
    value: string | null;
    secretSource:
        | "Plain"
        | "WindowsCredentialStore"
        | "AzureKeyVault"
        | "Generated";
    credentialKey: string | null;
    keyVaultName: string | null;
    generator?: VariableGeneratorDefinition | null;
    isEnabled: boolean;
}

export interface ApiClientUiState {
    activeEnvironmentId: string | null;
    activeEnvironmentIdByCollection: Record<string, string>;
    lastSelectedRequestIdByCollection: Record<string, string>;
}

// ── API Client ───────────────────────────────────────────────────────────────

export type ApiRequestMethod =
    | "Get"
    | "Post"
    | "Put"
    | "Patch"
    | "Delete"
    | "Head"
    | "Options"
    | "GraphQl"
    | "WebSocket";

export type RequestBodyMode =
    | "None"
    | "Json"
    | "Xml"
    | "Text"
    | "FormData"
    | "Binary";

export type ApiCollectionNodeType = "Folder" | "Request";

export type AuthType =
    | "None"
    | "Inherited"
    | "BearerToken"
    | "ApiKey"
    | "Basic"
    | "OAuth2";

export type ApiKeyLocation = "Header" | "QueryParam";

export interface ApiCollection {
    id: string;
    name: string;
    nodes: ApiCollectionNode[];
    variables: CollectionVariable[];
    defaultAuth: AuthConfig | null;
    createdAt: string;
    updatedAt: string;
    /**
     * Frontend-assigned provenance — set while flattening the store response into
     * the workspace tree (`internal`, the synthetic `demo` collection, or a
     * collection living under a linked root). Never persisted; strip it before any
     * whole-store PUT.
     */
    origin?: ApiCollectionOrigin;
}

export interface ApiCollectionOrigin {
    kind: "internal" | "linked" | "demo";
    /** Owning linked root — present when `kind === "linked"`. */
    rootId?: string;
    rootName?: string;
    rootPath?: string;
}

export interface ApiCollectionNode {
    id: string;
    type: ApiCollectionNodeType;
    name: string;
    isExpanded: boolean;
    children: ApiCollectionNode[];
    defaultAuth: AuthConfig | null;
    request: HttpRequestEntry | null;
}

export interface CollectionsStoreResponse {
    schemaVersion: number;
    collections: ApiCollection[];
    concurrencyToken: string | null;
    /**
     * Linked collection roots — folders on disk holding a `.swebkit-api/` tree.
     * Empty in demo mode; disabled roots appear but carry no collections. Absent
     * from the whole-store PUT response, so clients merging `setQueryData` writes
     * must preserve the previous array.
     */
    linkedRoots?: LinkedRootInfo[];
}

// ── Linked collection roots (Slice B — /api/linked-roots) ────────────────────

/** Wire shape of one linked root — `LinkedCollectionRootSummary` on the backend. */
export interface LinkedRootInfo {
    id: string;
    name: string;
    path: string;
    /** `<path>/.swebkit-api` — where the collections/environments trees live. */
    apiRootPath: string;
    isEnabled: boolean;
    isGitRepository: boolean;
    repositoryRoot: string | null;
    branch: string | null;
    changedFileCount: number;
    isValid: boolean;
    diagnostics: string[];
    collections: ApiCollection[];
    environments: ApiEnvironment[];
    requestFiles: LinkedRequestFileState[];
    environmentFiles: LinkedEnvironmentFileState[];
    brunoSyncFolderPath?: string | null;
    brunoSyncEnabled?: boolean;
}

/** Per-request file bookkeeping — the content stamp drives save-conflict detection. */
export interface LinkedRequestFileState {
    requestId: string;
    requestFilePath: string;
    contentStamp: string;
}

export interface LinkedEnvironmentFileState {
    environmentId: string;
    environmentFilePath: string;
}

/** `LinkedCollectionMutationResult` — create-collection response. */
export interface LinkedCollectionMutationResult {
    root: LinkedRootInfo;
    collectionId: string;
}

/** `LinkedRequestMutationResult` — create/save-request response. `requestId` is
 *  the request node's tree id (the file's stable id), `contentStamp` the stamp to
 *  send on the next save. */
export interface LinkedRequestMutationResult {
    root: LinkedRootInfo;
    requestId: string;
    requestFilePath: string;
    contentStamp: string;
}

/** The 409 body `PUT .../requests/{id}` returns when the file changed on disk. */
export interface LinkedRequestConflict {
    error?: string;
    currentContentStamp: string | null;
    requestFilePath: string | null;
}

export interface CollectionImportResult {
    collections: ApiCollection[];
    environments: ApiEnvironment[];
    requestCount: number;
    captureRuleCount: number;
    authConfigsRequiringReEntry: number;
    variablesExtractedAsEnvironment: number;
    warnings: string[];
}

/** `POST /api/api-client/import-curl` — one parsed request per pasted command,
 *  plus non-fatal notes for flags the parser ignored (e.g. `--insecure`). */
export interface CurlImportResult {
    requests: HttpRequestEntry[];
    warnings: string[];
}

export interface HttpRequestEntry {
    id: string;
    name: string;
    method: ApiRequestMethod;
    url: string;
    headers: KeyValuePair<string>[];
    queryParams: KeyValuePair<string>[];
    body: RequestBody;
    auth: AuthConfig | null;
    captureRules: CaptureRule[];
    graphQlQuery: string | null;
    graphQlVariables: string | null;
    graphQlSelectedOperation: string | null;
    savedMessages: WebSocketSavedMessage[];
    wsSubProtocol: string | null;
    responseExamples: ResponseExample[];
    createdAt: string;
    updatedAt: string;
    preRequestActions: RequestAction[];
    postRequestActions: RequestAction[];
    /**
     * "Runs after" — ids of same-collection request nodes that must run before
     * this one when sent via "Send with dependencies". Ids are tree node ids
     * (for internal collections these equal `request.id`; for linked requests
     * they are the file's stable id). Optional for backward compatibility with
     * collections.json and `.swebreq.json` files written before runs existed.
     */
    dependsOnRequestIds?: string[];
}

export interface RequestBody {
    mode: RequestBodyMode;
    rawContent: string | null;
    contentType: string | null;
    formData: FormDataField[];
    filePath: string | null;
}

/** One multipart form-data row. `isFile` sends `value` (a local path, {{vars}}
 * allowed) as a real file part instead of a text field. */
export interface FormDataField {
    key: string;
    value: string | null;
    isEnabled: boolean;
    isFile?: boolean;
}

export interface KeyValuePair<T> {
    key: string;
    value: T | null;
    isEnabled: boolean;
}

export interface AuthConfig {
    type: AuthType;
    /** Reference key into the persisted secret store. Never contains the actual secret. */
    credentialKey: string | null;
    /** Transient secret material for the current session; never persisted to collections.json. */
    credentialSecret?: string | null;
    apiKeyParamName: string | null;
    apiKeyLocation: ApiKeyLocation;
    basicUsername: string | null;
    oAuth2ClientId: string | null;
    oAuth2GrantType: "ClientCredentials" | "AuthorizationCode";
    oAuth2TokenUrl: string | null;
    oAuth2AuthUrl: string | null;
    oAuth2Scopes: string | null;
    /** Authorization-code flow only: credential-store key holding the signed-in token record
     * (access + refresh + expiry as JSON). Set by the loopback PKCE flow, never entered by hand. */
    oAuth2TokenCredentialKey?: string | null;
}

export interface CollectionVariable {
    key: string;
    value: string | null;
    generator?: VariableGeneratorDefinition | null;
    isEnabled: boolean;
}

export type VariableGeneratorKind =
    | "Integer"
    | "Decimal"
    | "Boolean"
    | "Guid"
    | "DateTime"
    | "List"
    | "Faker";

export interface VariableGeneratorDefinition {
    kind: VariableGeneratorKind;
    minInt?: number | null;
    maxInt?: number | null;
    minDecimal?: number | null;
    maxDecimal?: number | null;
    decimalPlaces?: number;
    trueWeightPercent?: number | null;
    fakerCategory?: string | null;
    /** Optional ISO lower bound (inclusive) for date.* faker categories. */
    fakerDateAfter?: string | null;
    /** Optional ISO upper bound (inclusive) for date.* faker categories. */
    fakerDateBefore?: string | null;
    values?: string[];
}

export interface CaptureRule {
    id: string;
    targetVariable: string;
    targetScope: string;
    source: "BodyJsonPath" | "ResponseHeader" | "StatusCode";
    jsonPath: string | null;
    headerName: string | null;
    isEnabled: boolean;
}

export type RequestActionKind = "CopyToClipboard" | "Delay";

export type RequestActionSource =
    | "RequestUrl"
    | "RequestMethod"
    | "RequestBody"
    | "ResponseStatusCode"
    | "ResponseStatusText"
    | "ResponseBody"
    | "ResponseHeader";

export interface RequestAction {
    id: string;
    kind: RequestActionKind;
    name: string;
    isEnabled: boolean;
    source: RequestActionSource;
    selector: string | null;
    delayMs: number;
}

export interface ResponseExample {
    id: string;
    name: string;
    statusCode: number;
    statusText: string;
    contentType: string | null;
    body: string | null;
    headers: KeyValuePair<string>[];
    capturedAt: string;
    environmentName: string | null;
}

export interface WebSocketSavedMessage {
    id: string;
    name: string;
    content: string;
    frameType: "Text" | "Binary";
}

export interface ApiClientExecutionResponse {
    resolvedUrl: string;
    method: string;
    statusCode: number;
    statusText: string;
    errorMessage: string | null;
    elapsedMs: number;
    contentLength: number;
    contentType: string | null;
    responseBody: string | null;
    responseBodyTruncated: boolean;
    headers: ResponseHeaderDto[];
    captureWarnings: string[];
    graphQlErrors: GraphQlError[] | null;
    /** Headers exactly as sent, echoed by the sidecar so the cURL panel can be truthful. */
    sentHeaders?: ResponseHeaderDto[] | null;
    /** The request body as it went out, post-substitution. Null for binary/oversized bodies. */
    sentBody?: string | null;
}

export interface ResponseHeaderDto {
    name: string;
    value: string;
}

export interface GraphQlError {
    message: string;
    locations: GraphQlErrorLocation[] | null;
    path: string[] | null;
}

export interface GraphQlErrorLocation {
    line: number;
    column: number;
}

// ── Request runs (POST /api/api-client/run — SSE stream) ─────────────────────
// See docs/features/active/api-client-request-runs.md. One `data:` frame per
// event, flat JSON with a `type` discriminator.

export type ApiRunMode = "requestWithDeps" | "subtree" | "explicit" | "chain";

/** Body of `POST /api/api-client/run`. */
export interface ApiRunRequest {
    mode: ApiRunMode;
    /**
     * The collection the run resolves against. Absent for `chain` mode — each
     * chain step carries its own collection reference and the backend resolves
     * per-step environments, so chain runs send no env ids either.
     */
    collectionId?: string;
    /** `chain`: the persisted chain to expand and run. */
    chainId?: string;
    /** Set when the collection lives under a linked root (`collection.origin.rootId`). */
    linkedRootId?: string | null;
    /** requestWithDeps: the request node id the chain resolves and ends with. */
    requestId?: string;
    /** subtree: folder node id — or the collection id for a whole-collection run. */
    nodeId?: string;
    /** explicit: caller-supplied request node ids, run in the given order. */
    requestIds?: string[];
    /** The collection-scoped environment layer. */
    activeEnvironmentId?: string | null;
    /** The global environment layer, applied underneath the scoped one. */
    globalEnvironmentId?: string | null;
    /** Abort the run after the first failed step. */
    stopOnError: boolean;
    /** Sleep between steps (server caps at 10s). */
    delayMs: number;
}

/**
 * Per-step context carried on `plan` steps and the step lifecycle events of a
 * `mode: "chain"` run: which collection the step resolved against, which chain
 * step (`stepId`) produced it, and whether it is a dependency pulled in by a
 * declared step (`isDependency` + `ownerStepId`) rather than a step itself.
 * All optional — absent on the collection-scoped run modes.
 */
export interface ApiRunStepContext {
    collectionId?: string;
    collectionName?: string;
    stepId?: string;
    isDependency?: boolean;
    /** For dependency steps: the declared chain step that pulled this dep in. */
    ownerStepId?: string;
}

export interface ApiRunPlanStep extends ApiRunStepContext {
    index: number;
    requestId: string;
    name: string;
}

/** One variable captured into scope by a completed step's capture rules. */
export interface ApiRunCapturedVariable {
    targetVariable: string;
    source: string;
    /**
     * `"run"` when the capture landed in the run-scoped variable overlay
     * (visible to later steps across collections, dies with the run);
     * `"environment"` when it wrote into its owning env/collection scope.
     * Absent on runs predating the overlay — treat as `"environment"`.
     */
    scope?: "run" | "environment";
}

export type ApiRunAbortReason = "stopOnError" | "cancelled";

export type ApiRunEvent =
    | { type: "plan"; runId: string; steps: ApiRunPlanStep[] }
    | ({
          type: "stepStarted";
          index: number;
          requestId: string;
          name: string;
      } & ApiRunStepContext)
    | ({
          type: "stepCompleted";
          index: number;
          requestId: string;
          status: number;
          durationMs: number;
          captured: ApiRunCapturedVariable[];
          response: ApiClientExecutionResponse;
      } & ApiRunStepContext)
    | ({
          type: "stepFailed";
          index: number;
          requestId: string;
          status?: number | null;
          /** Null when the step never reached the wire (plan/mid-step abort). */
          durationMs?: number | null;
          error: string;
          response?: ApiClientExecutionResponse | null;
      } & ApiRunStepContext)
    | { type: "aborted"; reason: ApiRunAbortReason; completedSteps: number }
    | {
          type: "done";
          completedSteps: number;
          failedSteps: number;
          durationMs: number;
      };

// ── Request chains (persisted, cross-collection ordered runs) ────────────────
// See docs/features/active/api-request-chains.md. Chains live in the internal
// store only (`chains.json`); steps may reference requests in any reachable
// collection — internal, linked-root, or demo.

/** One ordered step of a persisted chain. */
export interface ApiChainStep {
    /** Stable step id — SSE `stepId`/`ownerStepId` correlate back to it. */
    id: string;
    /** Internal or linked-root collection id. */
    collectionId: string;
    /** Set when the collection lives under a linked root. */
    linkedRootId?: string | null;
    /** The request's entry id (`node.request.id`). */
    requestId: string;
    enabled: boolean;
}

export interface ApiChain {
    id: string;
    name: string;
    description?: string | null;
    steps: ApiChainStep[];
    createdAt?: string;
    updatedAt?: string;
}

/** `GET /api/api-client/chains` list row — no step payload, just the count. */
export interface ApiChainSummary {
    id: string;
    name: string;
    description?: string | null;
    stepCount: number;
    updatedAt: string;
}

/**
 * POST/PUT body for `/api/api-client/chains`. The server validates but stores
 * steps verbatim — a broken request ref surfaces at plan time (`unknown_request`),
 * not at save time.
 */
export interface ApiChainUpsert {
    name: string;
    description?: string | null;
    steps: ApiChainStep[];
}

// ── Service Bus ──────────────────────────────────────────────────────────────
