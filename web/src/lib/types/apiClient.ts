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
}

export interface RequestBody {
    mode: RequestBodyMode;
    rawContent: string | null;
    contentType: string | null;
    formData: KeyValuePair<string>[];
    filePath: string | null;
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

// ── Service Bus ──────────────────────────────────────────────────────────────
