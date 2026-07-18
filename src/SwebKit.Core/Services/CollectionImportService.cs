using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Orchestrates collection and environment import: handles name-collision resolution,
/// delegates parsing to the appropriate <see cref="ICollectionImporter"/>, and persists
/// results via the repositories.
/// </summary>
public sealed class CollectionImportService(
    CollectionRepository collectionRepo,
    EnvironmentRepository environmentRepo,
    SwebKitCollectionImporter swebKitImporter,
    PostmanCollectionImporter postmanImporter,
    SwebKitEnvironmentImporter environmentImporter,
    BrunoFolderImporter brunoFolderImporter,
    LinkedCollectionFileService linkedFileService)
{
    private readonly IReadOnlyList<ICollectionImporter> _importers = [swebKitImporter, postmanImporter];

    /// <summary>
    /// Detects the format from the file payload and imports the collections and environments,
    /// resolving name collisions by appending " (2)", " (3)", etc.
    /// </summary>
    public async Task<CollectionImportResult> ImportCollectionAsync(
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        var importer = DetectImporter(payload);
        if (importer is null)
            return new CollectionImportResult { Warnings = ["Unrecognised file format. Supported: SwebKit JSON, Postman v2.1"] };

        var result = await importer.ImportAsync(payload, cancellationToken).ConfigureAwait(false);
        if (result.Collections.Count == 0)
            return result;

        var existingNames = collectionRepo.Collections.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingEnvNames = environmentRepo.Environments.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var collection in result.Collections)
        {
            collection.Name = ResolveNameCollision(collection.Name, existingNames);
            existingNames.Add(collection.Name);
            await collectionRepo.AddImportedCollectionAsync(collection).ConfigureAwait(false);
        }

        foreach (var env in result.Environments)
        {
            env.Name = ResolveNameCollision(env.Name, existingEnvNames);
            existingEnvNames.Add(env.Name);
            await environmentRepo.AddImportedEnvironmentAsync(env).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>Imports a Bruno collection from a folder on disk.</summary>
    public async Task<CollectionImportResult> ImportBrunoFolderAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        var result = await brunoFolderImporter.ImportFromFolderAsync(folderPath, cancellationToken).ConfigureAwait(false);
        if (result.Collections.Count == 0)
            return result;

        var existingNames = collectionRepo.Collections.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // AddImportedCollectionAsync assigns a fresh collection Id, so remember the importer's
        // temporary Id → persisted Id mapping to re-point each environment's CollectionId scope.
        var collectionIdRemap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var collection in result.Collections)
        {
            var temporaryId = collection.Id;
            collection.Name = ResolveNameCollision(collection.Name, existingNames);
            existingNames.Add(collection.Name);
            await collectionRepo.AddImportedCollectionAsync(collection).ConfigureAwait(false);
            collectionIdRemap[temporaryId] = collection.Id;
        }

        // Environment names are deduped per scope (per collection, or the global bucket), so two
        // collections may each keep an environment called "DEV".
        var existingEnvNamesByScope = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var env in environmentRepo.Environments)
            ScopeNameSet(existingEnvNamesByScope, env.CollectionId).Add(env.Name);

        foreach (var env in result.Environments)
        {
            if (env.CollectionId is not null && collectionIdRemap.TryGetValue(env.CollectionId, out var persistedId))
                env.CollectionId = persistedId;

            var scopeNames = ScopeNameSet(existingEnvNamesByScope, env.CollectionId);
            env.Name = ResolveNameCollision(env.Name, scopeNames);
            scopeNames.Add(env.Name);
            await environmentRepo.AddImportedEnvironmentAsync(env).ConfigureAwait(false);
        }

        return result;
    }

    private static HashSet<string> ScopeNameSet(Dictionary<string, HashSet<string>> byScope, string? collectionId)
    {
        var key = collectionId ?? string.Empty;
        if (!byScope.TryGetValue(key, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            byScope[key] = set;
        }
        return set;
    }

    /// <summary>
    /// Imports a Bruno collection from a folder on disk directly into a linked repo root,
    /// writing .swebreq.json files to the repo rather than local SwebKit storage.
    /// </summary>
    public async Task<CollectionImportResult> ImportBrunoFolderToLinkedRootAsync(
        string folderPath,
        string apiRootPath,
        CancellationToken cancellationToken = default)
    {
        var result = await brunoFolderImporter.ImportFromFolderAsync(folderPath, cancellationToken).ConfigureAwait(false);
        if (result.Collections.Count == 0)
            return result;

        await WriteImportToLinkedRootAsync(apiRootPath, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Writes each imported collection into the linked root, then routes environments to disk by scope:
    /// an environment whose <see cref="ApiEnvironment.CollectionId"/> matches a written collection is
    /// stored under that collection's <c>environments/</c> folder; unscoped ones go to the root.
    /// </summary>
    private async Task WriteImportToLinkedRootAsync(string apiRootPath, CollectionImportResult result, CancellationToken cancellationToken)
    {
        var collectionDirectoriesById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var collection in result.Collections)
        {
            var directory = await linkedFileService.WriteCollectionToLinkedRootAsync(apiRootPath, collection, cancellationToken).ConfigureAwait(false);
            collectionDirectoriesById[collection.Id] = directory;
        }

        foreach (var env in result.Environments)
        {
            if (env.CollectionId is not null && collectionDirectoriesById.TryGetValue(env.CollectionId, out var collectionDirectory))
                await linkedFileService.WriteEnvironmentToCollectionAsync(apiRootPath, collectionDirectory, env, cancellationToken).ConfigureAwait(false);
            else
                await linkedFileService.WriteEnvironmentToLinkedRootAsync(apiRootPath, env, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Detects the format from the file payload and imports the collections and environments
    /// directly into a linked repo root, writing .swebreq.json files to the repo rather than
    /// local SwebKit storage.
    /// </summary>
    public async Task<CollectionImportResult> ImportCollectionToLinkedRootAsync(
        byte[] payload,
        string apiRootPath,
        CancellationToken cancellationToken = default)
    {
        var importer = DetectImporter(payload);
        if (importer is null)
            return new CollectionImportResult { Warnings = ["Unrecognised file format. Supported: SwebKit JSON, Postman v2.1"] };

        var result = await importer.ImportAsync(payload, cancellationToken).ConfigureAwait(false);
        if (result.Collections.Count == 0)
            return result;

        await WriteImportToLinkedRootAsync(apiRootPath, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Imports a standalone environment file.</summary>
    public async Task<EnvironmentImportResult> ImportEnvironmentAsync(
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        if (!environmentImporter.CanImport(payload))
            return new EnvironmentImportResult { Warnings = ["Unrecognised environment file format."] };

        var result = await environmentImporter.ImportAsync(payload, cancellationToken).ConfigureAwait(false);
        var existingNames = environmentRepo.Environments.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var env in result.Environments)
        {
            env.Name = ResolveNameCollision(env.Name, existingNames);
            existingNames.Add(env.Name);
            await environmentRepo.AddImportedEnvironmentAsync(env).ConfigureAwait(false);
        }

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ICollectionImporter? DetectImporter(byte[] payload)
    {
        foreach (var importer in _importers)
        {
            if (importer.CanImport(payload))
                return importer;
        }
        return null;
    }

    public static string ResolveNameCollision(string name, ISet<string> existing)
    {
        if (!existing.Contains(name)) return name;

        for (var i = 2; ; i++)
        {
            var candidate = $"{name} ({i})";
            if (!existing.Contains(candidate))
                return candidate;
        }
    }
}
