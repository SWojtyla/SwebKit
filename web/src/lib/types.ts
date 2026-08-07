// TypeScript types matching the .NET sidecar domain models.
// These mirror the C# classes in SwebKit.Core.Domain and SwebKit.Core.Configuration.

// ── Profile / Config ─────────────────────────────────────────────────────────

export interface ProfileData {
  config: AppConfig;
  serviceBusNamespaces: ServiceBusNamespace[];
  messageTemplates: SbMessageTemplate[];
  schemaVersion: number;
}

export interface AppConfig {
  name: string;
  isProduction: boolean;
  aksConfig: AksConfig | null;
  redisConfig: RedisConfig | null;
  storageAccounts: StorageConfig[];
  devOpsConfig: DevOpsConfig | null;
  observabilityConfig: ObservabilityConfig | null;
  favoriteEntities: FavoriteEntity[];
  favoriteResources: FavoriteResource[];
  keyVaults: KeyVaultEntry[];
  topology: WorkspaceTopology;
}

// ── Workspace topology (workspace-intelligence Module 1) ────────────────────

export type WorkspaceResourceArea = "Aks" | "ServiceBus" | "Redis" | "Storage";

export interface WorkspaceResourceNode {
  id: string;
  area: WorkspaceResourceArea;
  resourceKey: string;
  displayLabel: string;
}

export interface WorkspaceResourceRelationship {
  id: string;
  fromNodeId: string;
  toNodeId: string;
  label: string | null;
}

export interface WorkspaceTopology {
  nodes: WorkspaceResourceNode[];
  relationships: WorkspaceResourceRelationship[];
}

/** Not-yet-added node the user can pick from — computed by the sidecar from existing config, never
 * persisted itself. See `GET /api/workspace/topology/candidates`. */
export interface WorkspaceResourceCandidate {
  area: WorkspaceResourceArea;
  resourceKey: string;
  displayLabel: string;
}

/** A candidate relationship the heuristic scan found but nobody has confirmed yet
 * (workspace-intelligence Module 2) — never persisted; recomputed each time the Map view asks for
 * it. See `GET /api/workspace/topology/suggestions`. */
export interface WorkspaceRelationshipSuggestion {
  fromNodeId: string;
  toNodeId: string;
  reason: string;
}

export interface ServiceBusNamespace {
  id: string;
  alias: string;
  fullyQualifiedNamespace: string;
  authMode: "ConnectionString" | "Entra";
  credentialKey: string;
  transportType: "Amqp" | "AmqpWebSockets";
  createdAt: string;
}

export interface AksConfig {
  kubeconfigPath: string | null;
  kubeconfigContext: string | null;
  defaultNamespace: string;
  watchedDeployments: string[];
  logBufferSize: number;
  autoRefreshIntervalSeconds: number;
  monitoringEnabled: boolean;
  monitoredNamespaces: string[];
}

export interface RedisConfig {
  caches: RedisCacheEntry[];
  activeCacheId: string | null;
  namespaceSeparator: string;
}

export interface RedisCacheEntry {
  id: string;
  displayName: string;
  connectionString: string;
  database: number;
}

export interface StorageConfig {
  id: string;
  displayName: string;
  accountName: string;
  connectionStringRef: string | null;
  useAad: boolean;
  allowMutations: boolean;
}

export interface DevOpsConfig {
  organization: string;
  authenticationMode: "Pat" | "Entra";
  patCredentialKey: string;
  pinnedProjects: string[];
  pipelineGroups: PipelineGroup[];
  releaseGroups: ReleaseGroup[];
  defaultStageAliases: Record<string, string>;
}

export interface PipelineGroup {
  id: string;
  name: string;
  pipelines: PipelineGroupEntry[];
}

export interface PipelineGroupEntry {
  projectName: string;
  pipelineId: number;
  pipelineName: string;
}

export type MergeStrategy = "FastForward" | "MergeCommit" | "Squash" | "Rebase";

export interface ReleaseGroup {
  id: string;
  name: string;
  description: string | null;
  defaultMergeStrategy: MergeStrategy;
  stageAliases: Record<string, string>;
  components: ReleaseGroupComponent[];
}

export interface ReleaseGroupComponent {
  projectName: string;
  repositoryId: string;
  repositoryName: string;
  sourceBranch: string;
  targetBranch: string;
  pipelineId: number;
  pipelineName: string | null;
  mergeStrategy: MergeStrategy;
  stageAliases: Record<string, string>;
  versionPrefix: string | null;
}

export type ReleaseTrainStatus =
  | "Draft"
  | "Preflight"
  | "CreatingTags"
  | "CreatingPullRequests"
  | "AwaitingMerge"
  | "MergeCompleted"
  | "RunningPipelines"
  | "Monitoring"
  | "Completed"
  | "Failed"
  | "Cancelled";

export type ReleaseTrainComponentStatus =
  | "NotStarted"
  | "Tagged"
  | "PullRequestCreated"
  | "PullRequestMerged"
  | "TstPending"
  | "TstRunning"
  | "TstSucceeded"
  | "TstFailed"
  | "StgPendingApproval"
  | "StgRunning"
  | "StgSucceeded"
  | "StgFailed"
  | "PrdPendingApproval"
  | "PrdRunning"
  | "PrdSucceeded"
  | "PrdFailed"
  | "Completed"
  | "Failed"
  | "Blocked";

export interface ReleaseTrainRecord {
  id: string;
  name: string;
  label: string | null;
  groupId: string | null;
  groupName: string | null;
  createdAt: string;
  createdBy: string | null;
  status: ReleaseTrainStatus;
  overallRemarks: string | null;
  components: ReleaseTrainComponent[];
  auditLog: ReleaseTrainAuditEvent[];
}

export interface ReleaseTrainComponent {
  id: string;
  componentName: string;
  projectName: string;
  repositoryId: string;
  repositoryName: string;
  sourceBranch: string;
  targetBranch: string;
  sourceVersion: string | null;
  targetVersion: string | null;
  tagName: string | null;
  tagObjectId: string | null;
  pullRequestId: number | null;
  pullRequestUrl: string | null;
  mergeCommitId: string | null;
  pipelineRunId: string | null;
  pipelineRunUrl: string | null;
  status: ReleaseTrainComponentStatus;
  remarks: string | null;
  stages: ReleaseTrainStage[];
  auditLog: ReleaseTrainAuditEvent[];
}

export interface ReleaseTrainStage {
  slot: string;
  stageName: string;
  state: string;
  result: string | null;
  runId: string | null;
  runUrl: string | null;
  approvalId: string | null;
  approvalUrl: string | null;
  approvedBy: string | null;
  startedAt: string | null;
  finishedAt: string | null;
}

export interface ReleaseTrainAuditEvent {
  timestamp: string;
  action: string;
  componentId: string | null;
  message: string;
  actor: string | null;
}

/**
 * Mirrors `SwebKit.Core.Domain.ObservabilityConfig` — only the two fields the agent-tool-only
 * integration actually needs (which Application Insights resource to query) are surfaced in the
 * UI; the rest of the real C# shape (SavedQueries, SLOs, guided-query drafts, thresholds) backs
 * the Observability *browsing* page that was dropped from this rewrite, so there's nothing here to
 * bind to. Auth is ambient `DefaultAzureCredential` (Azure CLI/VS login) — there's deliberately no
 * `credentialKey` field; Observability doesn't use the OS credential store the way Redis/Service
 * Bus/DevOps do.
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

export interface KeyVaultEntry {
  id: string;
  name: string;
  url: string;
}

export interface FavoriteEntity {
  namespaceId: string;
  entityPath: string;
  label: string;
}

export interface FavoriteResource {
  name: string;
  pinnedAt: string;
  snapshot: WorkspaceSnapshot;
}

export interface WorkspaceSnapshot {
  resource: OperatorResourceReference;
  restoreState: Record<string, string>;
  capturedAt: string;
}

export interface OperatorResourceReference {
  key: string;
  area: string;
  kind: string;
  displayName: string;
  displayPath?: string | null;
  summary?: string | null;
  icon?: string | null;
  metadata: Record<string, string>;
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

export interface UserSettings {
  theme: string;
  fontSize: "small" | "medium" | "large";
  density: "comfortable" | "compact";
  warmupConnectionsOnStartup: boolean;
  verifyApiClientSsl: boolean;
  apiClientRequestTabs: boolean;
  autoSaveRequests: boolean;
  agent: AgentConfig;
  logging: LoggingSettings;
  /** Incremented once per app launch by the sidecar; drives the Fathom theme's unlock progress. */
  sessionCount: number;
  /** Sticky once true — the Fathom theme, once earned, stays available even if sessionCount is later reset. */
  fathomUnlocked: boolean;
  /** Set only via the hidden six-click gesture on the status bar version number — no other UI surfaces it. */
  fathomDeveloperOverride: boolean;
  /** Port-forward pins set elsewhere in the app; surfaced here so saves from this page don't erase them. */
  pinnedPortForwards: Record<string, { label: string; namespace?: string; podLabelSelector?: string; remotePort: number; localPort: number; pinnedAt: string }[]>;
}

/** Sessions needed before Fathom unlocks. Mirrors UserSettings.FathomUnlockThreshold (server-enforced; this constant only drives the progress bar). */
export const FATHOM_UNLOCK_THRESHOLD = 100;

export interface AgentConfig {
  isEnabled: boolean;
  profiles: AgentProfile[];
  activeProfileId: string;
}

export type AgentCapability = "Unknown" | "ChatOnly" | "ToolCalling";

export interface AgentProfile {
  id: string;
  provider: "LmStudio" | "OpenAiCompatible" | "Mistral";
  displayName: string;
  baseUrl: string;
  model: string;
  credentialKey: string;
  timeoutSeconds: number;
  capability: AgentCapability;
  lastTestDiagnostic: string | null;
  requiresApiKey: boolean;
  /** Model's context window in tokens, used to scale when a growing conversation gets rolling
   * summarization (workspace-intelligence Module 5). Null = unknown; the sidecar falls back to a
   * conservative default rather than treating null as unlimited. */
  contextWindowTokens: number | null;
}

export interface AgentCapabilityTestResult {
  serverReachable: boolean;
  modelAvailable: boolean;
  chatValid: boolean;
  toolCallingValid: boolean;
  capability: AgentCapability;
  diagnostic: string | null;
  availableModels: string[] | null;
  /** Best-effort context window read from a non-standard /v1/models field (LM Studio in
   * particular) — null when the provider doesn't advertise one. */
  detectedContextWindowTokens: number | null;
}

export interface LoggingSettings {
  enabled: boolean;
  minimumLevel: string;
}

// ── API response helpers ─────────────────────────────────────────────────────

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
  secretSource: "Plain" | "WindowsCredentialStore" | "AzureKeyVault" | "Generated";
  credentialKey: string | null;
  keyVaultName: string | null;
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
  | "Faker"
  | "Template";

export interface VariableGeneratorDefinition {
  kind: VariableGeneratorKind;
  minInt?: number | null;
  maxInt?: number | null;
  minDecimal?: number | null;
  maxDecimal?: number | null;
  decimalPlaces?: number;
  trueWeightPercent?: number | null;
  fakerCategory?: string | null;
  template?: string | null;
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

export interface KubeContextInfo {
  name: string;
  cluster: string | null;
  user: string | null;
  namespace: string | null;
  isCurrent: boolean;
}

export interface DeploymentInfo {
  name: string;
  namespace: string;
  replicas: number;
  readyReplicas: number;
  status: string;
  imageTag: string | null;
  labels: Record<string, string>;
  selectorLabels: Record<string, string>;
}

export interface PodInfo {
  name: string;
  namespace: string;
  phase: string;
  status: string;
  ready: boolean;
  readyContainers: number;
  totalContainers: number;
  restartCount: number;
  lastRestartTime: string | null;
  lastRestartReason: string | null;
  podIP: string | null;
  nodeName: string | null;
  startTime: string | null;
  containers: string[];
  labels: Record<string, string>;
  readyDisplay: string;
}

export interface KubernetesEvent {
  name: string;
  namespace: string;
  type: string;
  reason: string | null;
  message: string | null;
  involvedObjectName: string | null;
  involvedObjectKind: string | null;
  lastTimestamp: string | null;
  count: number;
}

export interface ServiceInfo {
  name: string;
  namespace: string;
  type: string;
  clusterIp: string;
  externalAddresses: string[];
  ports: ServicePortInfo[];
  selectorLabels: Record<string, string>;
  labels: Record<string, string>;
}

export interface ServicePortInfo {
  name: string | null;
  protocol: string;
  port: number;
  targetPort: string | null;
  nodePort: number | null;
}

export interface HelmReleaseInfo {
  name: string;
  namespace: string;
  chart: string | null;
  appVersion: string | null;
  chartVersion: string | null;
  status: string;
  revision: number;
  updated: string | null;
}

export interface SecretInfo {
  name: string;
  namespace: string;
  type: string;
  keys: string[];
  labels: Record<string, string>;
}

export interface ConfigMapInfo {
  name: string;
  namespace: string;
  data: Record<string, string>;
  labels: Record<string, string>;
}

export interface StatefulSetInfo {
  name: string;
  namespace: string;
  replicas: number;
  readyReplicas: number;
  currentRevision: string | null;
  updateRevision: string | null;
  labels: Record<string, string>;
  selectorLabels: Record<string, string>;
}

export interface HpaInfo {
  name: string;
  namespace: string;
  targetKind: string;
  targetName: string;
  minReplicas: number;
  maxReplicas: number;
  currentReplicas: number;
  desiredReplicas: number;
  currentCpuUtilizationPercent: number | null;
  targetCpuUtilizationPercent: number | null;
  isKedaManaged: boolean;
  isScalingDisabled: boolean;
}

export interface CronJobInfo {
  name: string;
  namespace: string;
  schedule: string | null;
  suspend: boolean;
  activeCount: number;
  lastScheduleTime: string | null;
  lastSuccessfulTime: string | null;
}

export interface IngressInfo {
  name: string;
  namespace: string;
  ingressClass: string | null;
  rules: { host: string | null; paths: { path: string; pathType: string | null; serviceName: string | null; servicePort: number | null }[] }[];
  addresses: string[];
  labels: Record<string, string>;
}

export interface HttpRouteInfo {
  name: string;
  namespace: string;
  status: string;
  hostnames: string[];
  parentRefs: string[];
  backendRefs: string[];
  labels: Record<string, string>;
}

export interface GatewayInfo {
  name: string;
  namespace: string;
  gatewayClass: string;
  status: string;
  addresses: string[];
  attachedRoutes: number;
  labels: Record<string, string>;
}

export interface GatewayClassInfo {
  name: string;
  controllerName: string;
  status: string;
  labels: Record<string, string>;
}

export interface HelmHistoryEntry {
  revision: number;
  status: string;
  chart: string;
  appVersion: string;
  description: string;
  updated: string | null;
}

export interface HelmValuesResponse {
  userValues: string;
  computedValues: string;
}

export interface JobInfo {
  name: string;
  namespace: string;
  status: string;
  active: number;
  succeeded: number;
  failed: number;
  desiredCompletions: number | null;
  parallelism: number;
  startTime: string | null;
  completionTime: string | null;
  sourceKind: string | null;
  sourceName: string | null;
}

// ── Redis ─────────────────────────────────────────────────────────────────────

export interface RedisKeyScanResult {
  cursor: number;
  keys: string[];
  isComplete: boolean;
}

export interface RedisKeyInfo {
  key: string;
  type: string;
  ttl: string | null;
  memoryBytes: number | null;
  encoding: string | null;
  frequency: number | null;
  idleSeconds: number | null;
}

export interface RedisHashField {
  field: string;
  value: string;
}

export interface RedisSortedSetEntry {
  member: string;
  score: number;
}

export interface RedisSetMembersPage {
  members: string[];
  cursor: number;
  isComplete: boolean;
}

export interface RedisServerInfo {
  redisVersion: string;
  uptimeSeconds: number;
  connectedClients: number;
  usedMemoryBytes: number;
  maxMemoryBytes: number;
  usedMemoryHuman: string;
  totalCommandsProcessed: number;
  keyspaceHitRatio: number;
  databases: RedisDatabaseInfo[];
}

export interface RedisDatabaseInfo {
  index: number;
  keys: number;
  expires: number;
  avgTtl: number;
}

export interface RedisSlowLogEntry {
  id: number;
  executedAt: string;
  duration: string;
  command: string;
  arguments: string;
  clientName: string | null;
}

export interface RedisSlowLogSummary {
  entries: RedisSlowLogEntry[];
  truncated: boolean;
  maxReturned: number;
  capability: string;
}

export interface RedisPrefixMemoryBucket {
  prefix: string;
  keyCount: number;
  totalBytes: number;
  percentage: number;
}

export type RedisHealthSeverity = "Info" | "Warning" | "Critical";

export interface RedisHealthFinding {
  entityType: string;
  riskType: string;
  severity: RedisHealthSeverity;
  target: string;
  reason: string;
  memoryBytes: number | null;
  keyCount: number | null;
  sharePercent: number | null;
  drillKey: string | null;
}

export interface RedisKeyspaceHealthReport {
  generatedAtUtc: string;
  loadedKeyCount: number;
  estimatedKeyCount: number | null;
  coveragePercent: number;
  isPartialCoverage: boolean;
  confidenceLabel: string;
  hotKeySignalsAvailable: boolean;
  keysWithHotKeySignal: number;
  keysWithoutHotKeySignal: number;
  criticalCount: number;
  warningCount: number;
  infoCount: number;
  keyFindingCount: number;
  prefixFindingCount: number;
  findings: RedisHealthFinding[];
}

export interface RedisPubSubChannelInfo {
  channel: string;
  subscriberCount: number;
}

export interface RedisPubSubSnapshot {
  channels: RedisPubSubChannelInfo[];
  patternSubscriptionCount: number;
  truncated: boolean;
  maxChannels: number;
  capability: string;
}

// ── Storage ───────────────────────────────────────────────────────────────────

export interface StorageContainerItem {
  name: string;
  lastModified: string | null;
  publicAccess: string | null;
  leaseStatus: string | null;
}

export interface StorageBlobItem {
  name: string;
  isPrefix: boolean;
  sizeBytes: number | null;
  contentType: string | null;
  lastModified: string | null;
  etag: string | null;
}

export interface StorageBlobPage {
  items: StorageBlobItem[];
  continuationToken: string | null;
}

export interface BlobProperties {
  name: string;
  sizeBytes: number;
  contentType: string;
  lastModified: string;
  etag: string;
  leaseStatus: string | null;
  leaseState: string | null;
  accessTier: string | null;
  accessTierInferred: boolean | null;
  contentEncoding: string | null;
  contentLanguage: string | null;
  cacheControl: string | null;
  metadata: Record<string, string>;
  tags: Record<string, string>;
}

export interface StorageBlobContent {
  containerName: string;
  blobName: string;
  content: string;
  contentType: string | null;
  totalSizeBytes: number;
  wasTruncated: boolean;
  isBinary: boolean;
}

export interface BlobVersionComparison {
  baseVersionId: string;
  compareVersionId: string | null;
  metadataDiff: {
    before: Record<string, string | null>;
    after: Record<string, string | null>;
    addedKeys: string[];
    removedKeys: string[];
    changedKeys: string[];
  };
  contentComparePossible: boolean;
  baseSizeBytes: number | null;
  compareSizeBytes: number | null;
  textDiff: string | null;
}

export interface BlobMutationResult {
  success: boolean;
  errorMessage?: string | null;
  resultBlobPath?: string | null;
}

export type BlobRecoveryState = "Restored" | "Undeleted" | "Unsupported" | "Failed";

export interface BlobRecoveryResult {
  state: BlobRecoveryState;
  resultBlobPath?: string | null;
  errorMessage?: string | null;
}

// ── Agent ─────────────────────────────────────────────────────────────────────

/** One step in an assistant turn's tool-call trace — mirrors `SwebKit.Agents.AgentChatStep`. Type is
 * "tool_call" (about to run) or "tool_result" (finished); `summary` is a short, non-sensitive
 * preview, never the full result. */
export interface AgentChatStep {
  type: string;
  toolName?: string;
  summary?: string;
  elapsed?: string;
}

export interface AgentReply {
  text: string;
  elapsedMs: number;
  status: string;
  error: boolean;
  /** Per-tool-call trace for this turn (workspace-intelligence Module 6) — empty when no tools were
   * used. Rendered as a collapsed-by-default "Show reasoning" disclosure. */
  steps?: AgentChatStep[];
  /** True when this turn's history was rolling-summarized before being sent (Module 5) — render as
   * an inline "earlier parts of this conversation were summarized" notice. */
  summarized?: boolean;
  /** Percentage of the effective context window this turn's request used. */
  contextUsagePercent?: number;
}

/** One incremental event from POST /api/agent/chat/stream — see streamAgentChat in lib/api.ts and
 * IAgentModelClient.ChatStreamAsync (SwebKit.Agents) for the producing side. "done" always carries
 * `result` and is always the last event on success; "error" always carries `errorMessage` and is
 * always the last event on failure — nothing follows either. */
export type AgentStreamEventKind = "token" | "toolCallStarted" | "toolCallResult" | "done" | "error";

export interface AgentStreamEvent {
  kind: AgentStreamEventKind;
  token?: string;
  toolName?: string;
  result?: AgentReply;
  errorMessage?: string;
}

export interface AgentStatus {
  historyCount: number;
  /** Rough ~4-chars-per-token estimate over this session's history — not real tokenization, just
   * enough to let the user watch the conversation grow (see SidecarAgentChatService.GetEstimatedTokens). */
  estimatedTokens: number;
  /** Percentage of the active profile's effective context window the most recent turn's
   * fully-constructed request used (workspace-intelligence Module 5/6) — 0 if no turn has been
   * sent yet in this session. */
  contextUsagePercent: number;
  /** The percentage at which the context-usage indicator should switch to a warning color — the
   * same scaled threshold the backend uses to trigger rolling summarization
   * (workspace-intelligence Module 7). */
  contextUsageWarningPercent: number;
}

/** "ask" = read-only tools only. "ask_and_do" = mutating propose/prepare tools are also
 * available (still gated behind a confirm card — see PendingActionCard). */
export type AgentChatMode = "ask" | "ask_and_do";

/** "feature" (default) = tools scoped to the current contextual panel's area only. "workspace" =
 * the "search across my whole workspace" escalation (workspace-intelligence Module 3) — every
 * configured area's tools become visible for this turn. Orthogonal to `AgentChatMode`: scope gates
 * which area's tools are visible, mode gates whether mutate tools are available at all. */
export type AgentChatScope = "feature" | "workspace";

/** What the current page has open, passed to a contextual assistant conversation so the model can
 * be told what's on screen and so tool visibility scopes to that one feature area. `featureArea`
 * must match a backend FeatureArea enum member name (e.g. "Aks", "Redis") — see
 * SidecarAgentChatService.cs for the parsing side. */
export interface AgentChatContext {
  featureArea: string;
  selection?: Record<string, string>;
}

export interface PendingAction {
  id: string;
  type: string;
  summary: string;
  target: string;
  risk: "None" | "Low" | "High";
  preview: string;
  expiresAt: string;
}

export interface AgentActionApplyResult {
  isSuccess: boolean;
  errorMessage: string | null;
  resultSummary: string | null;
}

export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  elapsedMs?: number;
  error?: boolean;
  /** Tool-call trace for this reply, if any (workspace-intelligence Module 6). */
  steps?: AgentChatStep[];
  /** True if this reply's turn triggered rolling summarization of older history (Module 5). */
  summarized?: boolean;
}

export interface ContainerDetail {
  name: string;
  image: string;
  imageTag: string | null;
  resources: ResourceRequirements;
  envVars: EnvVarDetail[];
}

export interface ResourceRequirements {
  cpuRequest: string | null;
  memoryRequest: string | null;
  cpuLimit: string | null;
  memoryLimit: string | null;
}

export type EnvVarSourceKind = "Plain" | "ConfigMapRef" | "SecretRef" | "FieldRef";

export interface EnvVarDetail {
  name: string;
  value: string | null;
  source: EnvVarSourceKind;
  sourceName: string | null;
  sourceKey: string | null;
  isResolved: boolean;
}

export interface PodMetricInfo {
  podName: string;
  namespace: string;
  containers: PodMetricContainer[];
}

export interface PodMetricContainer {
  name: string;
  cpuCores: number;
  memoryBytes: number;
}
