using System.Text.Json;
using SwebKit.Azure.ServiceBus;
using SwebKit.Core.Diagnostics;
using SwebKit.Core.Serialization;
using SwebKit.Azure.Storage;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Agents.Tools.Aks;
using SwebKit.Agents.Tools.ApiClient;
using SwebKit.Agents.Tools.Monitoring;
using SwebKit.Agents.Tools.Redis;
using SwebKit.Agents.Tools.Sql;
using SwebKit.Agents.Tools.Storage;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Kubernetes.AksClient;
using SwebKit.Observability;
using SwebKit.Redis;
using SwebKit.Sql;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to use a fixed dev port by default.
// Allow override via --urls or ASPNETCORE_URLS (used by Tauri and Playwright tests).
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5199");

// Structured file logging + crash handlers — wired as early as possible so no other
// startup work can throw/log before this is in place. In a windowless release build the
// sidecar previously had nowhere for its logs to go (default console logging is discarded),
// leaving a production crash with no diagnostic trail.
var userSettingsRepository = new UserSettingsRepository();
var fileLoggerProvider = AppBootstrap.ConfigureCrashHandlers(userSettingsRepository);
builder.Logging.AddProvider(fileLoggerProvider);
// The FileLoggerProvider does its own level filtering based on user settings
// (LoggingSettings.MinimumLevel) — without this filter, the factory's default minimum level
// silently blocks entries the user explicitly enabled, and no log files are ever created.
builder.Logging.AddFilter<FileLoggerProvider>(_ => true);

// Register core configuration repositories
builder.Services.AddSingleton<ProfileRepository>();
builder.Services.AddSingleton<EnvironmentRepository>();
builder.Services.AddSingleton<CollectionRepository>();
// The same instance the file logger above reads settings from, so a change to logging
// settings via PUT /api/config/user-settings takes effect without a restart.
builder.Services.AddSingleton(userSettingsRepository);
builder.Services.AddSingleton<UiStateRepository>();

builder.Services.AddSingleton<SwebKit.Core.Services.AppStateService>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IAppEventBus, SwebKit.Core.Services.AppEventBus>();
builder.Services.AddSingleton<ConfigurationBundleService>();
builder.Services.AddSingleton<SwebKit.Core.Services.SwebKitCollectionImporter>();
builder.Services.AddSingleton<SwebKit.Core.Services.PostmanCollectionImporter>();
builder.Services.AddSingleton<SwebKit.Core.Services.SwebKitEnvironmentImporter>();
builder.Services.AddSingleton<SwebKit.Core.Services.BrunoFolderImporter>();
builder.Services.AddSingleton<SwebKit.Core.Services.CollectionImportService>();
builder.Services.AddSingleton<IServiceBusClientFactory, ServiceBusClientFactory>();
builder.Services.AddSingleton<IRedisClientFactory, RedisClientFactory>();
builder.Services.AddSingleton<IStorageClientFactory, StorageClientFactory>();
builder.Services.AddSingleton<SwebKit.Sidecar.Services.SidecarStorageConnectionPool>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IStorageConnectionPool>(
    sp => sp.GetRequiredService<SwebKit.Sidecar.Services.SidecarStorageConnectionPool>());
builder.Services.AddSingleton<IAksClientFactory, AksClientFactory>();
builder.Services.AddSingleton<DemoModeService>();
// Redis and Service Bus get the same per-request-client treatment storage already had: without these
// every endpoint hit opened (and leaked) a ConnectionMultiplexer / ServiceBusClient of its own.
builder.Services.AddSingleton<SwebKit.Sidecar.Services.SidecarRedisConnectionPool>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IRedisConnectionPool>(
    sp => sp.GetRequiredService<SwebKit.Sidecar.Services.SidecarRedisConnectionPool>());
builder.Services.AddSingleton<SwebKit.Sidecar.Services.SidecarServiceBusConnectionPool>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IServiceBusConnectionPool>(
    sp => sp.GetRequiredService<SwebKit.Sidecar.Services.SidecarServiceBusConnectionPool>());
// SQL: same pooled-client pattern — the client holds the Entra credential; ADO.NET does the
// underlying connection pooling.
builder.Services.AddSingleton<ISqlClientFactory, SqlClientFactory>();
builder.Services.AddSingleton<SwebKit.Sidecar.Services.SidecarSqlConnectionPool>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.ISqlConnectionPool>(
    sp => sp.GetRequiredService<SwebKit.Sidecar.Services.SidecarSqlConnectionPool>());
builder.Services.AddSingleton<SqlQueryRepository>();
builder.Services.AddSingleton<RedisKeyspaceHealthAnalyzer>();
builder.Services.AddSingleton<ScheduledMessageRepository>();

// Monitoring: persisted alert rules + evaluation engine + signal sources
builder.Services.AddSingleton<SwebKit.Core.Configuration.AlertRuleRepository>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IAlertRuleRepository>(
    sp => sp.GetRequiredService<SwebKit.Core.Configuration.AlertRuleRepository>());

// Persisted AI insight reports (ai-insight-reports) — the permanent record behind the
// Monitoring "AI Reports" tab; the seeded chat session it points at stays in-memory.
builder.Services.AddSingleton<SwebKit.Core.Configuration.ProactiveInsightReportRepository>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IProactiveInsightReportRepository>(
    sp => sp.GetRequiredService<SwebKit.Core.Configuration.ProactiveInsightReportRepository>());
builder.Services.AddSingleton<SwebKit.Sidecar.Services.SidecarMonitoringConnectionPool>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IMonitoringConnectionPool>(
    sp => sp.GetRequiredService<SwebKit.Sidecar.Services.SidecarMonitoringConnectionPool>());

// Each signal source is registered both as its concrete type and as IAlertSignalSource so the
// engine can resolve the full IAlertSignalSource list via DI.
builder.Services.AddSingleton<SwebKit.Kubernetes.AksClient.AksPodHealthSignalSource>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IAlertSignalSource>(
    sp => sp.GetRequiredService<SwebKit.Kubernetes.AksClient.AksPodHealthSignalSource>());
builder.Services.AddSingleton<SwebKit.Kubernetes.AksClient.AksPodRestartRateSignalSource>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IAlertSignalSource>(
    sp => sp.GetRequiredService<SwebKit.Kubernetes.AksClient.AksPodRestartRateSignalSource>());
builder.Services.AddSingleton<SwebKit.Kubernetes.AksClient.AksNamespaceHealthScoreSignalSource>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IAlertSignalSource>(
    sp => sp.GetRequiredService<SwebKit.Kubernetes.AksClient.AksNamespaceHealthScoreSignalSource>());
builder.Services.AddSingleton<SwebKit.Azure.ServiceBus.ServiceBusDlqSignalSource>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IAlertSignalSource>(
    sp => sp.GetRequiredService<SwebKit.Azure.ServiceBus.ServiceBusDlqSignalSource>());
builder.Services.AddSingleton<SwebKit.Azure.ServiceBus.ServiceBusActiveDepthSignalSource>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IAlertSignalSource>(
    sp => sp.GetRequiredService<SwebKit.Azure.ServiceBus.ServiceBusActiveDepthSignalSource>());
builder.Services.AddSingleton<SwebKit.Azure.ServiceBus.ServiceBusDeadSubscriptionSignalSource>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IAlertSignalSource>(
    sp => sp.GetRequiredService<SwebKit.Azure.ServiceBus.ServiceBusDeadSubscriptionSignalSource>());
builder.Services.AddSingleton<SwebKit.Redis.RedisMemorySignalSource>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IAlertSignalSource>(
    sp => sp.GetRequiredService<SwebKit.Redis.RedisMemorySignalSource>());
builder.Services.AddSingleton<SwebKit.Redis.RedisConnectedClientsSignalSource>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IAlertSignalSource>(
    sp => sp.GetRequiredService<SwebKit.Redis.RedisConnectedClientsSignalSource>());

// The evaluation engine is a singleton shared between the hosted-service lifetime and the
// endpoint handlers (which call ReloadRulesAsync after CRUD mutations).
builder.Services.AddSingleton<SwebKit.Sidecar.Services.MonitoringAlertEvaluationService>();
builder.Services.AddHostedService(
    sp => sp.GetRequiredService<SwebKit.Sidecar.Services.MonitoringAlertEvaluationService>());

// Workspace topology: heuristic relationship suggestions (workspace-intelligence Module 2). Reuses
// the same IMonitoringConnectionPool the alert engine already resolves demo-vs-real AKS clients
// through, rather than building its own connection logic.
builder.Services.AddSingleton<SwebKit.Sidecar.Services.WorkspaceRelationshipSuggestionService>();

// Proactive insights (workspace-intelligence Module 4) — subscribes to
// MonitoringAlertEvaluationService.AlertFired in its own constructor, so it must be resolved once
// at startup below (a plain AddSingleton alone only registers it, it doesn't instantiate it).
builder.Services.AddSingleton<SwebKit.Sidecar.Services.ProactiveInvestigationRunner>();
builder.Services.AddSingleton<SwebKit.Sidecar.Services.ProactiveInsightService>();

// Agent: OpenAI-compatible LLM client + ACP external-agent host, dispatched per active profile
// by AgentModelClientRouter (per-call resolution — switching profiles needs no restart).
builder.Services.AddHttpClient<OpenAiCompatibleAgentClient>();
builder.Services.AddSingleton<SwebKit.Sidecar.Services.Acp.AcpPermissionStore>();
builder.Services.AddSingleton<SwebKit.Sidecar.Services.Acp.AcpAgentHost>();
builder.Services.AddSingleton<SwebKit.Sidecar.Services.Acp.OutOfScopeCallTracker>();
builder.Services.AddSingleton<SwebKit.Sidecar.Services.Acp.AcpAgentModelClient>();
builder.Services.AddSingleton<IAgentModelClient, AgentModelClientRouter>();

// MCP bridge: exposes IAgentToolRegistry to external ACP agents over streamable HTTP
// (session/new → mcpServers). Stateless mode — the per-session tool allowlist travels in the
// ?tools= query param baked into the URL, so no MCP session state is needed.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<SwebKit.Sidecar.Services.Acp.SwebKitToolsMcpBridge>();
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless);
builder.Services.AddOptions<ModelContextProtocol.Server.McpServerOptions>()
    .Configure<SwebKit.Sidecar.Services.Acp.SwebKitToolsMcpBridge>(SwebKit.Sidecar.Services.Acp.SwebKitToolsMcpBridge.Configure);

// Capability tester: probes a profile's endpoint for reachability/tool-calling support, backing
// POST /api/agent/profiles/{id}/test. Separate HttpClient from the model client above since a
// capability test may run against a profile that isn't the active one.
builder.Services.AddHttpClient<AgentCapabilityTester>();

// Observability: agent-tool-only capability (workspace-intelligence plan, 2026-08-03) — no
// dedicated page/nav item (that part of the earlier product decision stands), but get_metrics/
// query_logs give the agent Application Insights context when a resource is configured.
// ObservabilityProviderFactory picks demo vs. real Azure App Insights per AppStateService.UseDemoData
// the same way the tools themselves already branch on it — no separate demo wiring needed here.
builder.Services.AddSingleton<IObservabilityProviderFactory, ObservabilityProviderFactory>();
builder.Services.AddSingleton<AppInsightsDiscoveryService>();
builder.Services.AddSingleton<IObservabilityResourceDiscovery, SwebKit.Sidecar.Services.ObservabilityResourceDiscoverySelector>();
// SQL server discovery via ARM — demo-aware selector, same pattern as observability.
builder.Services.AddSingleton<SqlServerDiscoveryService>();
builder.Services.AddSingleton<ISqlResourceDiscovery, SwebKit.Sidecar.Services.SqlResourceDiscoverySelector>();
builder.Services.AddSingleton<IAgentTool, GetMetricsTool>();
builder.Services.AddSingleton<IAgentTool, QueryLogsTool>();

// Agent tools — Kubernetes, Service Bus, Redis, Storage, and (now that Module 3's confirm-flow
// exists below) API Client.
builder.Services.AddSingleton<DemoAksClient>();
builder.Services.AddSingleton<IAgentTool, GetPodStatusTool>();
builder.Services.AddSingleton<IAgentTool, ListNamespacesTool>();
builder.Services.AddSingleton<IAgentTool, ListPodsTool>();
builder.Services.AddSingleton<IAgentTool, GetPodLogsTool>();
builder.Services.AddSingleton<IAgentTool, GetPodEventsTool>();
builder.Services.AddSingleton<IAgentTool, InvestigatePodIssueTool>();
builder.Services.AddSingleton<IAgentTool, ResolvePodEnvTool>();
builder.Services.AddSingleton<IAgentTool, GetAksResourceYamlTool>();
builder.Services.AddSingleton<IAgentTool, ProposeApplyAksYamlTool>();
builder.Services.AddSingleton<IAgentTool, GetQueueStatsTool>();
builder.Services.AddSingleton<IAgentTool, GetQueueMessagesTool>();
builder.Services.AddSingleton<IAgentTool, AnalyzeQueueHealthTool>();
builder.Services.AddSingleton<IAgentTool, GetRedisKeyInfoTool>();
builder.Services.AddSingleton<IAgentTool, ListRedisKeysTool>();
builder.Services.AddSingleton<IAgentTool, AnalyzeCacheHealthTool>();
builder.Services.AddSingleton<IAgentTool, ProposeDeleteRedisKeyTool>();
builder.Services.AddSingleton<IAgentTool, ProposeSetRedisKeyTtlTool>();
builder.Services.AddSingleton<IAgentTool, ListStorageBlobsTool>();
builder.Services.AddSingleton<IAgentTool, GetStorageBlobPropertiesTool>();
builder.Services.AddSingleton<IAgentTool, AnalyzeStorageHealthTool>();
builder.Services.AddSingleton<IAgentTool, ProposeCopyBlobTool>();
builder.Services.AddSingleton<IAgentTool, SearchApiRequestsTool>();
builder.Services.AddSingleton<IAgentTool, GetApiRequestTool>();
builder.Services.AddSingleton<IAgentTool, ProposeApiRequestChangeTool>();
builder.Services.AddSingleton<IAgentTool, ProposeApiRequestDeleteTool>();
builder.Services.AddSingleton<IAgentTool, PrepareApiRequestExecutionTool>();
builder.Services.AddSingleton<IAgentTool, ListSqlConnectionsTool>();
builder.Services.AddSingleton<IAgentTool, ListSqlDatabasesTool>();
builder.Services.AddSingleton<IAgentTool, ListSqlTablesTool>();
builder.Services.AddSingleton<IAgentTool, DescribeSqlTableTool>();
builder.Services.AddSingleton<IAgentTool, QuerySqlTool>();
builder.Services.AddSingleton<IAgentTool, CheckSqlHealthTool>();
builder.Services.AddSingleton<IAgentTool, ProposeExecuteSqlTool>();

// Cross-area correlation (workspace-intelligence Module 3) — resolves IAgentToolRegistry lazily via
// IServiceProvider to avoid a circular dependency (the registry is itself built from every
// registered IAgentTool, including this one). Registered last among IAgentTool entries purely for
// readability — registration order has no bearing on the circular-dependency fix.
builder.Services.AddSingleton<IAgentTool, InvestigateWorkspaceIssueTool>();

// Screen state (agent-workspace-awareness Module 1) — the store lives in SwebKit.Agents so the
// tool can inject it; the endpoint publishes into it, get_screen_state reads it.
builder.Services.AddSingleton<ScreenStateStore>();
builder.Services.AddSingleton<IAgentTool, GetScreenStateTool>();
builder.Services.AddSingleton<IAgentTool, ProposeCreateAlertRuleTool>();
builder.Services.AddSingleton<IAgentTool, ListAlertRulesTool>();
// Lives in the sidecar — the alert-history ring buffer is held by MonitoringAlertEvaluationService.
builder.Services.AddSingleton<IAgentTool, SwebKit.Sidecar.Services.GetAlertHistoryTool>();

builder.Services.AddSingleton<IAgentToolRegistry, AgentToolRegistry>();

builder.Services.AddSingleton<SidecarAgentChatService>();
builder.Services.AddSingleton<ExternalMcpToolSource>();

// Agent action confirm-before-execute flow (ai-augmented-app technical-plan.md Module 3). Wired
// here as infrastructure even though nothing in the sidecar can propose an action yet — the API
// Client propose tools (ApiClientTools.cs) land in Module 4, now that this exists for them to
// target. IApiClientAgentService needs the linked-collection chain;
// LinkedCollectionRootRepository's LoadAsync() is
// deliberately not called at sidecar startup below (linked collections aren't a sidecar feature
// yet), so it stays empty and ApiClientAgentService correctly sees local collections only.
builder.Services.AddSingleton<SwebKit.Core.Services.LinkedGitService>();
builder.Services.AddSingleton<SwebKit.Core.Services.LinkedCollectionFileService>();
builder.Services.AddSingleton<SwebKit.Core.Configuration.LinkedCollectionRootRepository>();
builder.Services.AddSingleton<IApiClientAgentService, SwebKit.Core.Services.ApiClientAgentService>();
builder.Services.AddSingleton<IAgentActionCoordinator, AgentActionCoordinator>();
builder.Services.AddSingleton<IAgentActionExecutor, ApiClientActionExecutor>();
builder.Services.AddSingleton<IAgentActionExecutor, RedisActionExecutor>();
builder.Services.AddSingleton<IAgentActionExecutor, StorageActionExecutor>();
builder.Services.AddSingleton<IAgentActionExecutor, SqlActionExecutor>();
builder.Services.AddSingleton<IAgentActionExecutor, AksActionExecutor>();
// Lives in the sidecar (not SwebKit.Agents) — applying an alert-rule action needs the
// sidecar-hosted MonitoringAlertEvaluationService for the post-upsert reload.
builder.Services.AddSingleton<IAgentActionExecutor, SwebKit.Sidecar.Services.MonitoringActionExecutor>();
builder.Services.AddSingleton<IAgentActionExecutor, SwebKit.Sidecar.Services.ExternalMcpActionExecutor>();
builder.Services.AddSingleton<AgentActionApplier>();

// HTTP client used by the API client request executor
builder.Services.AddHttpClient();

// The executor's named client. The Settings → General "verify SSL" toggle must reach it — the
// callback reads the live repository per request rather than snapshotting at handler creation,
// so a toggle takes effect immediately (the handler is cached for minutes by the factory).
builder.Services.AddHttpClient(HttpRequestExecutor.ClientName)
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var settings = sp.GetRequiredService<UserSettingsRepository>();
        return new HttpClientHandler
        {
            AllowAutoRedirect = true,
            ServerCertificateCustomValidationCallback = (_, _, _, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None || !settings.Settings.VerifyApiClientSsl,
        };
    });

// API client request execution pipeline
builder.Services.AddSingleton<ICredentialStore, SidecarCredentialStore>();
builder.Services.AddSingleton<IKeyVaultSecretResolver, SidecarKeyVaultResolver>();
builder.Services.AddSingleton<IVariableGeneratorService, VariableGeneratorService>();
builder.Services.AddSingleton<IVariableSubstitutionService, VariableSubstitutionService>();
builder.Services.AddSingleton<IAuthInheritanceResolver, AuthInheritanceResolver>();
builder.Services.AddSingleton<IAuthHeaderBuilder, SidecarAuthHeaderBuilder>();
builder.Services.AddSingleton<OAuth2PkceFlowService>();
builder.Services.AddSingleton<IPostRequestCaptureExecutor, PostRequestCaptureExecutor>();
builder.Services.AddSingleton<IHttpRequestExecutor, HttpRequestExecutor>();

// CORS for the Tauri WebView only — this sidecar listens on 127.0.0.1 and would
// otherwise be reachable by *any* website open in the user's regular browser
// ("localhost CORS drive-by"). The threat model is a REMOTE origin driving the
// sidecar via a browser tab; any origin on localhost/127.0.0.1 is trusted
// regardless of port (Vite's dev port, the Playwright e2e port, a future port
// change — all still fine, since something already running locally has
// equivalent access to this machine either way) plus the Tauri webview's own
// fixed origins. No wildcard, no remote origin ever matches.
bool IsAllowedOrigin(string origin)
{
    if (origin is "http://tauri.localhost" or "tauri://localhost")
        return true;
    return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && uri.Scheme == "http"
        && (uri.Host is "localhost" or "127.0.0.1");
}

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(IsAllowedOrigin).AllowAnyHeader().AllowAnyMethod();
    });
});

// Match the JSON options used by the core repositories (camelCase + string enums)
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var app = builder.Build();

app.UseCors();

// Global exception handler — ensures all error responses include CORS headers
// and return JSON instead of a bare 500 that the browser blocks.
app.UseExceptionHandler(ex =>
{
    ex.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;

        // The client aborted (e.g. switched Redis cache or closed the page mid-request) — there is
        // nobody to answer, and logging it as an unhandled error is pure noise.
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            return;

        var statusCode = exception switch
        {
            InvalidOperationException => 400,
            ArgumentException => 400,
            UnauthorizedAccessException => 401,
            // Both AKS auth exceptions carry messages that are built to be user-facing and free of
            // secrets, so they map to real auth statuses and pass their message through rather than
            // collapsing into an opaque 500 "Internal server error".
            AksAuthenticationException => 401,
            AksAccessDeniedException => 403,
            // Azure.Identity's failure for "nobody is signed in / the credential chain is broken".
            // Mapped centrally rather than per-endpoint so every Azure-backed route answers the same
            // way — and so it is logged, which the old endpoint-local `catch` never did.
            Azure.Identity.AuthenticationFailedException => 401,
            // The same failure wrapped inside an SDK exception — e.g. a ServiceBusException whose
            // inner is the token acquisition failing.
            _ when ServiceBusExceptionClassifier.IsAuthenticationFailure(exception!) => 401,
            // Upstream-connection failures — an unreachable or misbehaving Redis/Service Bus server
            // is an expected operational condition (e.g. switching to a dead cache or a throttled
            // namespace), not a bug, so a 502 tells the UI "the backend couldn't reach it" instead
            // of a generic 500.
            StackExchange.Redis.RedisException or System.Net.Sockets.SocketException or TimeoutException => 502,
            global::Azure.Messaging.ServiceBus.ServiceBusException => 502,
            // An OCE that isn't a client abort (handled above) is a downstream call timing out —
            // HttpClient or an SDK retry ceiling — which is exactly a "couldn't reach it" answer.
            OperationCanceledException => 502,
            _ => 500,
        };
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        var origin = context.Request.Headers.Origin.ToString();
        if (IsAllowedOrigin(origin))
        {
            context.Response.Headers.AccessControlAllowOrigin = origin;
        }

        // 400/401/403s here are deliberate, user-actionable messages the app throws itself (e.g.
        // "AKS is not configured...", or an AKS auth failure naming the broken credential plugin),
        // safe to return as-is. A 500 means something unexpected blew
        // up — often an Azure/K8s/Redis SDK exception whose message can contain connection
        // strings, internal paths, or other detail that shouldn't reach the client. Log the real
        // exception server-side and return a generic message instead.
        //
        // Azure.Identity is the one exception to "pass the message through": its
        // AuthenticationFailedException message is a multi-paragraph credential-chain dump carrying
        // tenant/client ids, so it gets a fixed replacement.
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        string message;
        if (statusCode == 500)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
            message = "Internal server error";
        }
        else
        {
            // Every handled failure is logged too: endpoints no longer catch these themselves, so
            // this is the only place a 400/401/403 leaves a server-side trace.
            logger.LogWarning(exception, "Request failed with {StatusCode} on {Method} {Path}", statusCode, context.Request.Method, context.Request.Path);
            message = exception switch
            {
                Azure.Identity.AuthenticationFailedException => "Azure authentication failed. Sign in again (for example `az login`) and retry.",
                // SDK messages can embed endpoints or connection config — the same sanitized
                // classification the connection-test endpoints return.
                _ when statusCode == 502 => ConnectionTestError.Describe(exception!),
                // A 401 reached via the classifier means the real credential failure is wrapped
                // inside an SDK exception — use the fixed message, not that exception's Message.
                // Direct UnauthorizedAccessException keeps its deliberate user-facing message.
                _ when statusCode == 401 && exception is not UnauthorizedAccessException =>
                    "Azure authentication failed. Sign in again (for example `az login`) and retry.",
                not null => exception.Message,
                null => "Internal server error",
            };
        }

        var payload = System.Text.Json.JsonSerializer.Serialize(new { error = message });
        await context.Response.WriteAsync(payload);
    });
});

// Load config repositories on startup
var profileRepository = app.Services.GetRequiredService<ProfileRepository>();
await profileRepository.LoadAsync();
// Move any plaintext Redis connection strings persisted by older versions into the OS
// credential store, then write the profile back without the secrets. TrySaveAsync (not
// SaveAsync) — a blocked persistence (failed load) must not turn the migration into a
// profile-destroying write.
if (RedisCredentialMigration.MigrateCaches(profileRepository.Config.RedisConfig, app.Services.GetRequiredService<ICredentialStore>()))
    await profileRepository.TrySaveAsync();
await app.Services.GetRequiredService<EnvironmentRepository>().LoadAsync();
await app.Services.GetRequiredService<CollectionRepository>().LoadAsync();
await userSettingsRepository.LoadAsync();
// Fathom theme unlock progress: one increment per launch, and the unlock is sticky once earned
// (a later SessionCount reset — e.g. via settings import — must not re-lock a theme the user
// already reached, hence checking FathomUnlocked with ||= rather than recomputing from scratch).
userSettingsRepository.Settings.SessionCount++;
userSettingsRepository.Settings.FathomUnlocked |= userSettingsRepository.Settings.SessionCount >= UserSettings.FathomUnlockThreshold;
await userSettingsRepository.SaveAsync();
await app.Services.GetRequiredService<SwebKit.Core.Configuration.AlertRuleRepository>().GetAllAsync();
// Force-instantiate now so its constructor subscribes to MonitoringAlertEvaluationService.AlertFired
// before the first alert can possibly fire — a plain AddSingleton registration alone only makes it
// resolvable, it doesn't construct it until something asks for it.
app.Services.GetRequiredService<SwebKit.Sidecar.Services.ProactiveInsightService>();

// ── Health, Demo Mode ────────────────────────────────────────────────────────

app.MapSystemEndpoints();

// ── Config: Profiles, Environments, Collections, User Settings, Import/Export ─

app.MapConfigEndpoints();

// ── Service Bus ──────────────────────────────────────────────────────────────

app.MapServiceBusEndpoints();

// ── AKS / Kubernetes ─────────────────────────────────────────────────────────

app.MapAksEndpoints();

// ── API Client ───────────────────────────────────────────────────────────────

app.MapApiClientEndpoints();

// ── SQL ───────────────────────────────────────────────────────────────────────

app.MapSqlEndpoints();

// ── Redis ─────────────────────────────────────────────────────────────────────

app.MapRedisEndpoints();

// ── Storage ───────────────────────────────────────────────────────────────────

app.MapStorageEndpoints();

// ── Agent ─────────────────────────────────────────────────────────────────────

app.MapAgentEndpoints();

// MCP endpoint for external ACP agents (SwebKitToolsMcpBridge handlers; tool set is filtered
// per request via the ?tools= allowlist the ACP session URL carries).
app.MapMcp(SwebKit.Sidecar.Services.Acp.SwebKitToolsMcpBridge.EndpointPath);

// ── Monitoring ───────────────────────────────────────────────────────────────

app.MapMonitoringEndpoints();

// ── Workspace topology (workspace-intelligence Module 1) ────────────────────

app.MapWorkspaceTopologyEndpoints();

// ── Observability ───────────────────────────────────────────────────────────

app.MapObservabilityEndpoints();

app.Run();
