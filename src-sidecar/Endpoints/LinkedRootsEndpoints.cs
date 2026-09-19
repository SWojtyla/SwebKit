using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Endpoints;

/// <summary>
/// Linked API project roots — folders on disk (typically inside a git repo) that hold
/// collections and environments as files under <c>.swebkit-api/</c>, with optional two-way
/// Bruno <c>.bru</c> sync.
/// </summary>
public static class LinkedRootsEndpoints
{
    public static void MapLinkedRootsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/api-client/linked-roots", GetLinkedRootsAsync);
        app.MapPost("/api/api-client/linked-roots", AddLinkedRootAsync);
        app.MapPut("/api/api-client/linked-roots/{id}", UpdateLinkedRootAsync);
        app.MapDelete("/api/api-client/linked-roots/{id}", RemoveLinkedRootAsync);
        app.MapPost("/api/api-client/linked-roots/reload", ReloadLinkedRootsAsync);
    }

    internal static async Task<IResult> GetLinkedRootsAsync(
        LinkedCollectionsService linked, CancellationToken ct)
    {
        await linked.ReloadAsync(ct).ConfigureAwait(false);
        return Results.Ok(linked.AllRoots.Select(ToSummary).ToList());
    }

    internal static async Task<IResult> ReloadLinkedRootsAsync(
        LinkedCollectionsService linked, CancellationToken ct)
    {
        await linked.ReloadAsync(ct).ConfigureAwait(false);
        return Results.Ok(linked.AllRoots.Select(ToSummary).ToList());
    }

    internal static async Task<IResult> AddLinkedRootAsync(
        AddLinkedRootRequest req,
        LinkedCollectionRootRepository roots,
        LinkedCollectionFileService files,
        CollectionImportService importer,
        LinkedCollectionsService linked,
        DemoModeService demo,
        CancellationToken ct)
    {
        if (demo.IsDemoMode)
        {
            return ApiErrors.BadRequest("Linked folders are not available in demo mode.");
        }
        if (string.IsNullOrWhiteSpace(req.Path))
        {
            return ApiErrors.BadRequest("Folder path is required.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(req.Path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ApiErrors.BadRequest($"Invalid folder path: {ex.Message}");
        }

        // Validate the Bruno folder first — a bad path must not leave a half-created root behind.
        string? brunoPath = null;
        if (!string.IsNullOrWhiteSpace(req.BrunoFolderPath))
        {
            try
            {
                brunoPath = Path.GetFullPath(req.BrunoFolderPath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return ApiErrors.BadRequest($"Invalid Bruno folder path: {ex.Message}");
            }
            if (!Directory.Exists(brunoPath))
            {
                return ApiErrors.BadRequest($"Bruno folder not found: {brunoPath}");
            }
        }

        // Create the .swebkit-api structure inside the picked folder (no-op if it exists).
        var apiRootPath = await files.EnsureRootAsync(fullPath, req.Name ?? string.Empty, ct).ConfigureAwait(false);
        var root = await roots.AddRootAsync(fullPath, req.Name).ConfigureAwait(false);

        // Optional Bruno link: import the folder into the new root and enable .bru write-back.
        if (brunoPath is not null)
        {
            await importer.ImportBrunoFolderToLinkedRootAsync(brunoPath, apiRootPath, ct).ConfigureAwait(false);
            await roots.UpdateBrunoSyncSettingsAsync(root.Id, brunoPath, true).ConfigureAwait(false);
        }

        await linked.ReloadAsync(ct).ConfigureAwait(false);
        return Results.Ok(linked.AllRoots.Select(ToSummary).ToList());
    }

    internal static async Task<IResult> UpdateLinkedRootAsync(
        string id,
        UpdateLinkedRootRequest req,
        LinkedCollectionRootRepository roots,
        LinkedCollectionsService linked,
        CancellationToken ct)
    {
        if (req.Name is not null && !await roots.RenameRootAsync(id, req.Name).ConfigureAwait(false))
        {
            return ApiErrors.NotFound("Linked folder not found.");
        }
        if (req.IsEnabled is not null || req.BrunoSyncFolderPath is not null || req.BrunoSyncEnabled is not null)
        {
            var existing = roots.Roots.FirstOrDefault(r => r.Id == id);
            if (existing is null)
            {
                return ApiErrors.NotFound("Linked folder not found.");
            }
            existing.IsEnabled = req.IsEnabled ?? existing.IsEnabled;
            if (req.BrunoSyncFolderPath is not null || req.BrunoSyncEnabled is not null)
            {
                await roots.UpdateBrunoSyncSettingsAsync(
                    id,
                    req.BrunoSyncFolderPath ?? existing.BrunoSyncFolderPath,
                    req.BrunoSyncEnabled ?? existing.BrunoSyncEnabled).ConfigureAwait(false);
            }
            await roots.SaveAsync().ConfigureAwait(false);
        }

        await linked.ReloadAsync(ct).ConfigureAwait(false);
        return Results.Ok(linked.AllRoots.Select(ToSummary).ToList());
    }

    internal static async Task<IResult> RemoveLinkedRootAsync(
        string id,
        LinkedCollectionRootRepository roots,
        LinkedCollectionsService linked,
        CancellationToken ct)
    {
        // Unlink only — the folder's files are left on disk.
        if (!await roots.RemoveRootAsync(id).ConfigureAwait(false))
        {
            return ApiErrors.NotFound("Linked folder not found.");
        }

        await linked.ReloadAsync(ct).ConfigureAwait(false);
        return Results.Ok(linked.AllRoots.Select(ToSummary).ToList());
    }

    private static LinkedRootSummary ToSummary((LinkedCollectionRootConfig Config, LinkedCollectionRootLoadResult? Result) entry)
    {
        var (config, result) = entry;
        return new LinkedRootSummary
        {
            Id = config.Id,
            Name = config.Name,
            Path = config.Path,
            IsEnabled = config.IsEnabled,
            BrunoSyncFolderPath = config.BrunoSyncFolderPath,
            BrunoSyncEnabled = config.BrunoSyncEnabled,
            DisplayName = result?.DisplayName ?? config.Name,
            ApiRootPath = result?.ApiRootPath,
            Diagnostics = result?.Diagnostics ?? [],
            CollectionCount = result?.Collections.Count ?? 0,
            EnvironmentCount = result?.Environments.Count ?? 0,
            IsGitRepository = result?.GitStatus.IsGitRepository ?? false,
            RepositoryRoot = result?.GitStatus.RepositoryRoot,
            Branch = result?.GitStatus.Branch,
            ChangedFileCount = result?.GitStatus.ChangedFileCount ?? 0,
        };
    }

    public sealed record AddLinkedRootRequest(string? Path, string? Name, string? BrunoFolderPath);

    public sealed record UpdateLinkedRootRequest(
        string? Name, bool? IsEnabled, string? BrunoSyncFolderPath, bool? BrunoSyncEnabled);

    public sealed class LinkedRootSummary
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public bool IsEnabled { get; init; }
        public string? BrunoSyncFolderPath { get; init; }
        public bool BrunoSyncEnabled { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public string? ApiRootPath { get; init; }
        public IReadOnlyList<string> Diagnostics { get; init; } = [];
        public int CollectionCount { get; init; }
        public int EnvironmentCount { get; init; }
        public bool IsGitRepository { get; init; }
        public string? RepositoryRoot { get; init; }
        public string? Branch { get; init; }
        public int ChangedFileCount { get; init; }
    }
}
