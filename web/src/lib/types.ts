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
  organizationUrl: string;
  project: string;
  credentialKey: string;
}

export interface ObservabilityConfig {
  applicationInsightsResourceId: string;
  credentialKey: string;
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
  warmupConnectionsOnStartup: boolean;
  verifyApiClientSsl: boolean;
  apiClientRequestTabs: boolean;
  autoSaveRequests: boolean;
  agent: AgentConfig;
  logging: LoggingSettings;
}

export interface AgentConfig {
  isEnabled: boolean;
  profiles: AgentProfile[];
  activeProfileId: string;
  maxHistoryMessages: number;
  historyWarningThresholdPercent: number;
}

export interface AgentProfile {
  id: string;
  provider: string;
  displayName: string;
  endpointUrl: string;
  model: string;
  credentialKey: string;
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
  isEnabled: boolean;
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
  age: string;
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

export interface AgentReply {
  text: string;
  elapsedMs: number;
  status: string;
  error: boolean;
}

export interface AgentStatus {
  historyCount: number;
}

export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  elapsedMs?: number;
  error?: boolean;
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
