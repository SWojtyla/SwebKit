using System.Reflection;
using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Endpoints;

public static class SystemEndpoints
{
    // Read once — it's the assembly's own version, which can't change at runtime.
    private static readonly string Version = typeof(SystemEndpoints).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static void MapSystemEndpoints(this WebApplication app)
    {
        app.MapGet("/health", GetHealth);

        app.MapGet("/api/demo-mode", GetDemoMode);
        app.MapPost("/api/demo-mode", SetDemoMode);
    }

    internal static IResult GetHealth() => Results.Ok(new { status = "ok", version = Version });

    internal static IResult GetDemoMode(DemoModeService demo) =>
        Results.Ok(new { isDemoMode = demo.IsDemoMode });

    internal static IResult SetDemoMode(DemoModeService demo, bool enabled)
    {
        demo.IsDemoMode = enabled;
        return Results.Ok(new { isDemoMode = demo.IsDemoMode });
    }
}
