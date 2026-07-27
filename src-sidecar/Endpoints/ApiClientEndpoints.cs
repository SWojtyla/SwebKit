using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Sidecar.Endpoints;

public static class ApiClientEndpoints
{
    public static void MapApiClientEndpoints(this WebApplication app)
    {
        app.MapPost("/api/api-client/execute", async (
            ExecuteRequestRequest req,
            IHttpRequestExecutor executor,
            CollectionRepository collections,
            EnvironmentRepository environments,
            DemoModeService demo) =>
        {
            var collection = await ResolveCollectionAsync(req.CollectionId, collections, demo);
            if (collection is null && req.CollectionId is not null)
                return Results.NotFound("Collection not found");

            collection ??= new ApiCollection();

            ApiEnvironment? activeEnvironment = null;
            if (!string.IsNullOrWhiteSpace(req.EnvironmentId))
            {
                activeEnvironment = environments.Environments.FirstOrDefault(e => e.Id == req.EnvironmentId);
                if (activeEnvironment is null)
                    return Results.NotFound("Environment not found");
            }

            try
            {
                var result = await executor.ExecuteAsync(req.Request, collection, activeEnvironment);
                return Results.Ok(Map(result));
            }
            catch (Exception ex)
            {
                return Results.Problem($"Request failed: {ex.Message}");
            }
        });
    }

    private static async Task<ApiCollection?> ResolveCollectionAsync(string? collectionId, CollectionRepository collections, DemoModeService demo)
    {
        if (string.IsNullOrWhiteSpace(collectionId))
            return null;

        if (demo.IsDemoMode && collectionId == DemoApiCollectionFactory.DemoCollectionId)
            return DemoApiCollectionFactory.CreateDemoCollection();

        // Load the latest persisted store so we get the full tree and variables.
        await collections.LoadAsync().ConfigureAwait(false);
        return collections.Collections.FirstOrDefault(c => c.Id == collectionId);
    }

    private static ApiClientExecutionResponse Map(HttpRequestResult result) =>
        new(
            result.ResolvedUrl,
            result.Method,
            result.StatusCode,
            result.StatusText,
            result.ErrorMessage,
            result.Elapsed.TotalMilliseconds,
            result.ContentLength,
            result.ContentType,
            result.ResponseBody,
            result.ResponseBodyTruncated,
            result.ResponseHeaders.Select(h => new ResponseHeaderDto(h.Name, h.Value)).ToList(),
            result.CaptureWarnings.ToList(),
            result.GraphQlErrors);
}

public sealed class ExecuteRequestRequest
{
    public HttpRequestEntry Request { get; set; } = new();
    public string? CollectionId { get; set; }
    public string? EnvironmentId { get; set; }
}

public sealed record ApiClientExecutionResponse(
    string ResolvedUrl,
    string Method,
    int StatusCode,
    string StatusText,
    string? ErrorMessage,
    double ElapsedMs,
    long ContentLength,
    string? ContentType,
    string? ResponseBody,
    bool ResponseBodyTruncated,
    IReadOnlyList<ResponseHeaderDto> Headers,
    IReadOnlyList<string> CaptureWarnings,
    IReadOnlyList<GraphQlError>? GraphQlErrors);

public sealed record ResponseHeaderDto(string Name, string Value);
