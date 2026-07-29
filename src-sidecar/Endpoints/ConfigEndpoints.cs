using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Endpoints;

public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        app.MapGet("/api/config/export", (ConfigurationBundleService svc) =>
        {
            var bundle = svc.Export();
            return Results.Text(svc.Serialize(bundle), "application/json");
        });

        app.MapPost("/api/config/import", async (ConfigurationBundleService svc, HttpRequest req) =>
        {
            using var reader = new StreamReader(req.Body);
            var json = await reader.ReadToEndAsync().ConfigureAwait(false);
            var bundle = svc.Deserialize(json);
            await svc.ImportAsync(bundle).ConfigureAwait(false);
            return Results.Ok();
        });
    }
}
