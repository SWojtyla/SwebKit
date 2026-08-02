using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Endpoints;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this WebApplication app)
    {
        app.MapGet("/health", GetHealth);

        app.MapGet("/api/demo-mode", GetDemoMode);
        app.MapPost("/api/demo-mode", SetDemoMode);
    }

    internal static IResult GetHealth() => Results.Ok(new { status = "ok", version = "0.1.0" });

    internal static IResult GetDemoMode(DemoModeService demo) =>
        Results.Ok(new { isDemoMode = demo.IsDemoMode });

    internal static IResult SetDemoMode(DemoModeService demo, bool enabled)
    {
        demo.IsDemoMode = enabled;
        return Results.Ok(new { isDemoMode = demo.IsDemoMode });
    }
}
