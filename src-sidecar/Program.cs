using System.Text.Json;
using SwebKit.Azure.ServiceBus;
using SwebKit.Core.Diagnostics;
using SwebKit.Core.Serialization;
using SwebKit.Azure.Storage;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Agents.Tools.ApiClient;
using SwebKit.Agents.Tools.Redis;
using SwebKit.Agents.Tools.Storage;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Kubernetes.AksClient;
using SwebKit.Observability;
using SwebKit.Redis;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to use a fixed dev port by default.
// Allow override via --urls or ASPNETCORE_URLS (used by Tauri and Playwright tests).
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5199");

// Structured file logging + crash handlers — wired as early as possible, mirroring
// MauiProgram.cs's startup order, so no other startup work can throw/log before this is in
// place. In a windowless release build the sidecar previously had nowhere for its logs to go
// (default console logging is discarded), leaving a production crash with no diagnostic trail.
var userSettingsRepository = new UserSettingsRepository();
var fileLoggerProvider = AppBootstrap.ConfigureCrashHandlers(userSettingsRepository);
builder.Logging.AddProvider(fileLoggerProvider);
// The FileLoggerProvider does its own level filtering based on user settings
// (LoggingSettings.MinimumLevel) — without this filter, the factory's default minimum level
// silently blocks entries the user explicitly enabled, and no log files are ever created.
builder.Logging.AddFilter<FileLoggerProvider>(_ => true);

// Register core configuration repositories (same as MauiProgram.cs)
builder.Services.AddSingleton<ProfileRepository>();
builder.Services.AddSingleton<EnvironmentRepository>();
builder.Services.AddSingleton<CollectionRepository>();
// The same instance the file logger above reads settings from, so a change to logging
// settings via PUT /api/config/user-settings takes effect without a restart.
builder.Services.AddSingleton(userSettingsRepository);
builder.Services.AddSingleton<UiStateRepository>();
builder.Services.AddSingleton<ReleaseRepository>();
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
builder.Services.AddSingleton<IAksClientFactory, AksClientFactory>();
builder.Services.AddSingleton<DemoModeService>();
builder.Services.AddSingleton<RedisKeyspaceHealthAnalyzer>();
builder.Services.AddSingleton<ScheduledMessageRepository>();

// Monitoring: persisted alert rules + evaluation engine + signal sources
builder.Services.AddSingleton<SwebKit.Core.Configuration.AlertRuleRepository>();
builder.Services.AddSingleton<SwebKit.Core.Abstractions.IAlertRuleRepository>(
    sp => sp.GetRequiredService<SwebKit.Core.Configuration.AlertRuleRepository>());
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
builder.Services.AddSingleton<SwebKit.Sidecar.Services.ProactiveInsightService>();

// Agent: OpenAI-compatible LLM client + sidecar chat service
builder.Services.AddHttpClient<IAgentModelClient, OpenAiCompatibleAgentClient>();
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
builder.Services.AddSingleton<IAgentTool, ProposeCopyBlobTool>();
builder.Services.AddSingleton<IAgentTool, SearchApiRequestsTool>();
builder.Services.AddSingleton<IAgentTool, GetApiRequestTool>();
builder.Services.AddSingleton<IAgentTool, ProposeApiRequestChangeTool>();
builder.Services.AddSingleton<IAgentTool, ProposeApiRequestDeleteTool>();
builder.Services.AddSingleton<IAgentTool, PrepareApiRequestExecutionTool>();

// Cross-area correlation (workspace-intelligence Module 3) — resolves IAgentToolRegistry lazily via
// IServiceProvider to avoid a circular dependency (the registry is itself built from every
// registered IAgentTool, including this one). Registered last among IAgentTool entries purely for
// readability — registration order has no bearing on the circular-dependency fix.
builder.Services.AddSingleton<IAgentTool, InvestigateWorkspaceIssueTool>();
builder.Services.AddSingleton<IAgentToolRegistry, AgentToolRegistry>();

builder.Services.AddSingleton<SidecarAgentChatService>();

// Agent action confirm-before-execute flow (ai-augmented-app technical-plan.md Module 3). Wired
// here as infrastructure even though nothing in the sidecar can propose an action yet — the API
// Client propose tools (ApiClientTools.cs) land in Module 4, now that this exists for them to
// target. IApiClientAgentService needs the same linked-collection chain the MAUI app uses
// (SwebKitServiceCollectionExtensions.Agents.cs); LinkedCollectionRootRepository's LoadAsync() is
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
builder.Services.AddSingleton<AgentActionApplier>();

// HTTP client used by the API client request executor
builder.Services.AddHttpClient();

// API client request execution pipeline
builder.Services.AddSingleton<ICredentialStore, SidecarCredentialStore>();
builder.Services.AddSingleton<IKeyVaultSecretResolver, SidecarKeyVaultResolver>();
builder.Services.AddSingleton<IVariableGeneratorService, VariableGeneratorService>();
builder.Services.AddSingleton<IVariableSubstitutionService, VariableSubstitutionService>();
builder.Services.AddSingleton<IAuthInheritanceResolver, AuthInheritanceResolver>();
builder.Services.AddSingleton<IAuthHeaderBuilder, SidecarAuthHeaderBuilder>();
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
        var statusCode = exception switch
        {
            InvalidOperationException => 400,
            ArgumentException => 400,
            UnauthorizedAccessException => 401,
            _ => 500,
        };
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        var origin = context.Request.Headers.Origin.ToString();
        if (IsAllowedOrigin(origin))
        {
            context.Response.Headers.AccessControlAllowOrigin = origin;
        }

        // 400/401s here are deliberate, user-actionable messages the app throws itself (e.g.
        // "AKS is not configured..."), safe to return as-is. A 500 means something unexpected blew
        // up — often an Azure/K8s/Redis SDK exception whose message can contain connection
        // strings, internal paths, or other detail that shouldn't reach the client. Log the real
        // exception server-side and return a generic message instead.
        string message;
        if (statusCode == 500)
        {
            context.RequestServices.GetRequiredService<ILogger<Program>>()
                .LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
            message = "Internal server error";
        }
        else
        {
            message = exception?.Message ?? "Internal server error";
        }

        var payload = System.Text.Json.JsonSerializer.Serialize(new { error = message });
        await context.Response.WriteAsync(payload);
    });
});

// Load config repositories on startup
await app.Services.GetRequiredService<ProfileRepository>().LoadAsync();
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

// ── Redis ─────────────────────────────────────────────────────────────────────

app.MapRedisEndpoints();

// ── Storage ───────────────────────────────────────────────────────────────────

app.MapStorageEndpoints();

// ── Agent ─────────────────────────────────────────────────────────────────────

app.MapAgentEndpoints();

// ── Monitoring ───────────────────────────────────────────────────────────────

app.MapMonitoringEndpoints();

// ── Workspace topology (workspace-intelligence Module 1) ────────────────────

app.MapWorkspaceTopologyEndpoints();

// ── Observability ───────────────────────────────────────────────────────────

app.MapObservabilityEndpoints();

app.Run();
