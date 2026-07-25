using System.Text.Json;
using SwebKit.Azure.ServiceBus;
using SwebKit.Azure.Storage;
using SwebKit.Agents;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Redis;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to use a fixed dev port by default.
// Allow override via --urls or ASPNETCORE_URLS (used by Tauri and Playwright tests).
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5199");

// Register core configuration repositories (same as MauiProgram.cs)
builder.Services.AddSingleton<ProfileRepository>();
builder.Services.AddSingleton<EnvironmentRepository>();
builder.Services.AddSingleton<CollectionRepository>();
builder.Services.AddSingleton<UserSettingsRepository>();
builder.Services.AddSingleton<IServiceBusClientFactory, ServiceBusClientFactory>();
builder.Services.AddSingleton<IRedisClientFactory, RedisClientFactory>();
builder.Services.AddSingleton<IStorageClientFactory, StorageClientFactory>();
builder.Services.AddSingleton<DemoModeService>();

// Agent: OpenAI-compatible LLM client + sidecar chat service
builder.Services.AddHttpClient<IAgentModelClient, OpenAiCompatibleAgentClient>();
builder.Services.AddSingleton<SidecarAgentChatService>();

// HTTP client used by the API client request executor
builder.Services.AddHttpClient();

// API client request execution pipeline
builder.Services.AddSingleton<ICredentialStore, SidecarCredentialStore>();
builder.Services.AddSingleton<IKeyVaultSecretResolver, NoopKeyVaultSecretResolver>();
builder.Services.AddSingleton<IVariableGeneratorService, VariableGeneratorService>();
builder.Services.AddSingleton<IVariableSubstitutionService, VariableSubstitutionService>();
builder.Services.AddSingleton<IAuthInheritanceResolver, AuthInheritanceResolver>();
builder.Services.AddSingleton<IAuthHeaderBuilder, SidecarAuthHeaderBuilder>();
builder.Services.AddSingleton<IPostRequestCaptureExecutor, PostRequestCaptureExecutor>();
builder.Services.AddSingleton<IHttpRequestExecutor, HttpRequestExecutor>();

// CORS for the Tauri WebView (dev mode uses http://localhost:1420)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
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

// Load config repositories on startup
await app.Services.GetRequiredService<ProfileRepository>().LoadAsync();
await app.Services.GetRequiredService<EnvironmentRepository>().LoadAsync();
await app.Services.GetRequiredService<CollectionRepository>().LoadAsync();
await app.Services.GetRequiredService<UserSettingsRepository>().LoadAsync();

// ── Health ───────────────────────────────────────────────────────────────────

app.MapGet("/health", () => new { status = "ok", version = "0.1.0" });

// ── Demo Mode ────────────────────────────────────────────────────────────────

app.MapGet("/api/demo-mode", (DemoModeService demo) =>
    Results.Ok(new { isDemoMode = demo.IsDemoMode }));

app.MapPost("/api/demo-mode", (DemoModeService demo, bool enabled) =>
{
    demo.IsDemoMode = enabled;
    return Results.Ok(new { isDemoMode = demo.IsDemoMode });
});

// ── Config: Profiles ─────────────────────────────────────────────────────────

app.MapGet("/api/config/profiles", (ProfileRepository repo, DemoModeService demo) =>
{
    var data = repo.GetProfileData();
    if (demo.IsDemoMode)
    {
        data.ServiceBusNamespaces = [.. demo.GetDemoNamespaces()];
        var demoCache = demo.GetDemoRedisCache(DemoModeService.DemoRedisCacheId);
        if (demoCache is not null)
        {
            data.Config.RedisConfig = new RedisConfig
            {
                Caches = [demoCache],
                ActiveCacheId = demoCache.Id,
            };
        }
        var demoStorage = demo.GetDemoStorageConfig();
        if (demoStorage is not null)
        {
            data.Config.StorageAccounts = [demoStorage];
        }
    }
    return Results.Ok(data);
});

app.MapPut("/api/config/profiles", async (ProfileRepository repo, ProfileData data) =>
{
    repo.ReplaceProfileData(data);
    await repo.SaveAsync();
    return Results.Ok();
});

// ── Config: Environments ─────────────────────────────────────────────────────

app.MapGet("/api/config/environments", (EnvironmentRepository repo) =>
    Results.Ok(new { repo.Environments, repo.UiState }));

app.MapPut("/api/config/environments", async (EnvironmentRepository repo, EnvironmentsStore store) =>
{
    await repo.ReplaceStoreAsync(store);
    return Results.Ok();
});

// ── Config: Collections ──────────────────────────────────────────────────────

app.MapGet("/api/config/collections", (CollectionRepository repo) => Results.Ok(repo.Collections));

app.MapPut("/api/config/collections", async (CollectionRepository repo, CollectionsStore store) =>
{
    await repo.ReplaceStoreAsync(store);
    return Results.Ok();
});

// ── Config: User Settings ────────────────────────────────────────────────────

app.MapGet("/api/config/user-settings", (UserSettingsRepository repo) => Results.Ok(repo.Settings));

app.MapPut("/api/config/user-settings", async (UserSettingsRepository repo, UserSettings settings) =>
{
    repo.ReplaceSettings(settings);
    await repo.SaveAsync();
    return Results.Ok();
});

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

app.Run();
