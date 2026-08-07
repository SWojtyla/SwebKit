using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.DevOps;

namespace SwebKit.Sidecar.Endpoints;

public static class ReleaseTrainEndpoints
{
    public static void MapReleaseTrainEndpoints(this WebApplication app)
    {
        // ── Release trains ───────────────────────────────────────────────────────

        app.MapGet("/api/release-trains", async (IReleaseTrainService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ct).ConfigureAwait(false)));

        app.MapPost("/api/release-trains", async (
            ReleaseTrainCreateEndpointRequest req,
            IReleaseTrainService service,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.ProfileId) || string.IsNullOrWhiteSpace(req.GroupId))
                return Results.BadRequest(new { error = "ProfileId and GroupId are required." });

            var createReq = new ReleaseTrainCreateRequest(
                req.Name,
                req.Label,
                req.OverallRemarks,
                req.Components.Select(c => new ReleaseTrainComponentCreateRequest(c.ComponentName, c.Version, c.Remarks)).ToList());

            var train = await service.CreateFromGroupAsync(req.ProfileId, req.GroupId, createReq, ct).ConfigureAwait(false);
            return Results.Created($"/api/release-trains/{train.Id}", train);
        });

        app.MapGet("/api/release-trains/{id:guid}", async (Guid id, IReleaseTrainService service, CancellationToken ct) =>
        {
            var train = await service.GetAsync(id, ct).ConfigureAwait(false);
            return train is null ? Results.NotFound() : Results.Ok(train);
        });

        app.MapDelete("/api/release-trains/{id:guid}", async (Guid id, IReleaseTrainService service, CancellationToken ct) =>
        {
            await service.ArchiveAsync(id, ct).ConfigureAwait(false);
            return Results.NoContent();
        });

        app.MapPost("/api/release-trains/{id:guid}/preflight", async (Guid id, IReleaseTrainService service, CancellationToken ct) =>
            Results.Ok(await service.PreflightAsync(id, ct).ConfigureAwait(false)));

        app.MapPost("/api/release-trains/{id:guid}/execute", async (Guid id, IReleaseTrainService service, CancellationToken ct) =>
            Results.Ok(await service.ExecuteAsync(id, ct).ConfigureAwait(false)));

        app.MapPost("/api/release-trains/{id:guid}/refresh", async (Guid id, IReleaseTrainService service, CancellationToken ct) =>
            Results.Ok(await service.RefreshAsync(id, ct).ConfigureAwait(false)));

        app.MapPost("/api/release-trains/{id:guid}/complete", async (Guid id, IReleaseTrainService service, CancellationToken ct) =>
        {
            await service.CompleteAsync(id, ct).ConfigureAwait(false);
            return Results.NoContent();
        });

        app.MapPost("/api/release-trains/{id:guid}/advance-demo", async (
            Guid id,
            string? failComponent,
            IReleaseTrainService service,
            CancellationToken ct) =>
            Results.Ok(await service.AdvanceDemoAsync(id, failComponent, ct).ConfigureAwait(false)));

        app.MapPost("/api/release-trains/{id:guid}/components/{componentId:guid}/attach-run", async (
            Guid id,
            Guid componentId,
            ReleaseTrainAttachRunEndpointRequest req,
            IReleaseTrainService service,
            CancellationToken ct) =>
            Results.Ok(await service.AttachRunAsync(id, componentId, new ReleaseTrainAttachRunRequest(
                req.ProjectName,
                req.PipelineId,
                req.RunId,
                req.SourceVersion), ct).ConfigureAwait(false)));

        app.MapPut("/api/release-trains/{id:guid}/remarks", async (
            Guid id,
            ReleaseTrainRemarksEndpointRequest req,
            IReleaseTrainService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateRemarksAsync(id, new ReleaseTrainRemarksRequest(
                req.OverallRemarks,
                req.ComponentRemarks), ct).ConfigureAwait(false)));

        // ── PAT credentials ──────────────────────────────────────────────────────

        app.MapPost("/api/devops/pat", (SavePatRequest req, ICredentialStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Key) || string.IsNullOrWhiteSpace(req.Pat))
                return Results.BadRequest(new { error = "Key and PAT are required." });

            store.Save($"ado:pat:{req.Key.Trim()}", req.Pat.Trim());
            return Results.Ok(new { key = req.Key.Trim() });
        });

        app.MapDelete("/api/devops/pat/{key}", (string key, ICredentialStore store) =>
        {
            store.Delete($"ado:pat:{key.Trim()}");
            return Results.NoContent();
        });

        app.MapGet("/api/devops/pat-keys", (ICredentialStore store) =>
            Results.Ok(store.ListKeys("ado:pat:").Select(k => k["ado:pat:".Length..]).ToList()));

        // ── DevOps connection test ───────────────────────────────────────────────

        app.MapPost("/api/devops/test-connection", async (
            ProfileRepository profile,
            AppStateService appState,
            IDevOpsClientFactory clientFactory,
            DemoDevOpsClient demoClient,
            CancellationToken ct) =>
        {
            if (appState.UseDemoData)
                return Results.Ok(new { connected = true, mode = "demo" });

            var config = profile.Config.DevOpsConfig;
            if (config is null || string.IsNullOrWhiteSpace(config.Organization))
                return Results.BadRequest(new { error = "DevOps organization is not configured." });

            try
            {
                var client = clientFactory.Create(config);
                var ok = await client.TestConnectionAsync(ct).ConfigureAwait(false);
                return Results.Ok(new { connected = ok, mode = "live" });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { connected = false, mode = "live", error = ex.Message });
            }
        });
    }
}

public sealed class ReleaseTrainCreateEndpointRequest
{
    public string ProfileId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? OverallRemarks { get; set; }
    public List<ReleaseTrainComponentCreateEndpointRequest> Components { get; set; } = [];
}

public sealed class ReleaseTrainComponentCreateEndpointRequest
{
    public string ComponentName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}

public sealed class ReleaseTrainAttachRunEndpointRequest
{
    public string ProjectName { get; set; } = string.Empty;
    public int PipelineId { get; set; }
    public int RunId { get; set; }
    public string? SourceVersion { get; set; }
}

public sealed class ReleaseTrainRemarksEndpointRequest
{
    public string? OverallRemarks { get; set; }
    public Dictionary<string, string>? ComponentRemarks { get; set; }
}

public sealed class SavePatRequest
{
    public string Key { get; set; } = string.Empty;
    public string Pat { get; set; } = string.Empty;
}
