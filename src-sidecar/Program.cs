using System.Text.Json;
using SwebKit.Azure.ServiceBus;
using SwebKit.Core.Diagnostics;
using SwebKit.Core.Serialization;
using SwebKit.Azure.Storage;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Kubernetes.AksClient;
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

// Agent: OpenAI-compatible LLM client + sidecar chat service
builder.Services.AddHttpClient<IAgentModelClient, OpenAiCompatibleAgentClient>();
// Capability tester: probes a profile's endpoint for reachability/tool-calling support, backing
// POST /api/agent/profiles/{id}/test. Separate HttpClient from the model client above since a
// capability test may run against a profile that isn't the active one.
builder.Services.AddHttpClient<AgentCapabilityTester>();

// Agent tools — read-only Kubernetes and Service Bus diagnostics only. Observability tools
// (QueryLogsTool/GetMetricsTool) are not wired here because the sidecar has no
// IObservabilityProviderFactory yet (that's a MAUI-only service, a separate feature gap of its
// own). The API Client mutation/proposal tools (ApiClientTools.cs) are not wired here either
// because they need the confirmation-card flow (IAgentActionCoordinator/AgentActionApplier) that
// the sidecar's chat UI doesn't implement — wiring them without it would let the model mutate
// collections with no user confirmation step.
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
builder.Services.AddSingleton<IAgentToolRegistry, AgentToolRegistry>();

builder.Services.AddSingleton<SidecarAgentChatService>();

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
await app.Services.GetRequiredService<UserSettingsRepository>().LoadAsync();
await app.Services.GetRequiredService<SwebKit.Core.Configuration.AlertRuleRepository>().GetAllAsync();

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

app.Run();
