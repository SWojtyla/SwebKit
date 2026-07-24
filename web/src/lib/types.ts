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
}

export interface SbMessageTemplate {
  id: string;
  name: string;
  body: string;
  contentType: string;
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
