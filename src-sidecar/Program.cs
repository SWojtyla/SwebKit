var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to use a dynamic port (port 0 = OS-assigned).
// In production, Tauri reads the actual port from stdout.
builder.WebHost.UseUrls("http://127.0.0.1:5199");

// TODO: Register DI services from existing SwebKit projects
// builder.Services.AddSingleton<ProfileRepository>();
// builder.Services.AddSingleton<EnvironmentRepository>();
// builder.Services.AddScoped<IServiceBusClientFactory, ServiceBusClientFactory>();
// etc.

// Add CORS for the Tauri WebView (dev mode uses http://localhost:1420)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();

// Health check endpoint
app.MapGet("/health", () => new { status = "ok", version = "0.1.0" });

// Config endpoints (Phase 2)
// app.MapGet("/api/config/profiles", ...);
// app.MapPut("/api/config/profiles", ...);

// Service Bus endpoints (Phase 3)
// app.MapGet("/api/servicebus/{nsId}/info", ...);

// AKS endpoints (Phase 4)
// app.MapGet("/api/aks/contexts", ...);

// Redis endpoints (Phase 6)
// app.MapGet("/api/redis/{cacheId}/test", ...);

// Storage endpoints (Phase 7)
// app.MapGet("/api/storage/{accountId}/test", ...);

// Agent endpoints (Phase 8)
// app.MapPost("/api/agent/chat", ...);

app.Run();
