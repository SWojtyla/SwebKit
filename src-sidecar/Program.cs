using SwebKit.Azure.ServiceBus;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to use a fixed dev port.
// In production, Tauri will pass --urls http://127.0.0.1:0 for OS-assigned port.
builder.WebHost.UseUrls("http://127.0.0.1:5199");

// Register core configuration repositories (same as MauiProgram.cs)
builder.Services.AddSingleton<ProfileRepository>();
builder.Services.AddSingleton<EnvironmentRepository>();
builder.Services.AddSingleton<CollectionRepository>();
builder.Services.AddSingleton<UserSettingsRepository>();
builder.Services.AddSingleton<IServiceBusClientFactory, ServiceBusClientFactory>();

// CORS for the Tauri WebView (dev mode uses http://localhost:1420)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
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

// ── Config: Profiles ─────────────────────────────────────────────────────────

app.MapGet("/api/config/profiles", (ProfileRepository repo) => Results.Ok(repo.GetProfileData()));

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

app.Run();
