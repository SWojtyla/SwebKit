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

        var result = await importer.ImportAsync(payload, cancellationToken);
        if (result.Collections.Count == 0)
            return result;

        var existingNames = collectionRepo.Collections.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingEnvNames = environmentRepo.Environments.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var collection in result.Collections)
        {
            collection.Name = ResolveNameCollision(collection.Name, existingNames);
            existingNames.Add(collection.Name);
            await collectionRepo.AddImportedCollectionAsync(collection);
        }

        foreach (var env in result.Environments)
        {
            env.Name = ResolveNameCollision(env.Name, existingEnvNames);
            existingEnvNames.Add(env.Name);
            await environmentRepo.AddImportedEnvironmentAsync(env);
        }

        return result;
    }

    /// <summary>Imports a Bruno collection from a folder on disk.</summary>
    public async Task<CollectionImportResult> ImportBrunoFolderAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        var result = await brunoFolderImporter.ImportFromFolderAsync(folderPath, cancellationToken);
        if (result.Collections.Count == 0)
            return result;

        var existingNames = collectionRepo.Collections.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingEnvNames = environmentRepo.Environments.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var collection in result.Collections)
        {
            collection.Name = ResolveNameCollision(collection.Name, existingNames);
            existingNames.Add(collection.Name);
            await collectionRepo.AddImportedCollectionAsync(collection);
        }

        foreach (var env in result.Environments)
        {
            env.Name = ResolveNameCollision(env.Name, existingEnvNames);
            existingEnvNames.Add(env.Name);
            await environmentRepo.AddImportedEnvironmentAsync(env);
        }

        return result;
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
        var result = await brunoFolderImporter.ImportFromFolderAsync(folderPath, cancellationToken);
        if (result.Collections.Count == 0)
            return result;

        foreach (var collection in result.Collections)
            await linkedFileService.WriteCollectionToLinkedRootAsync(apiRootPath, collection, cancellationToken);

        foreach (var env in result.Environments)
            await linkedFileService.WriteEnvironmentToLinkedRootAsync(apiRootPath, env, cancellationToken);

        return result;
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

        var result = await importer.ImportAsync(payload, cancellationToken);
        if (result.Collections.Count == 0)
            return result;

        foreach (var collection in result.Collections)
            await linkedFileService.WriteCollectionToLinkedRootAsync(apiRootPath, collection, cancellationToken);

        foreach (var env in result.Environments)
            await linkedFileService.WriteEnvironmentToLinkedRootAsync(apiRootPath, env, cancellationToken);

        return result;
    }

    /// <summary>Imports a standalone environment file.</summary>
    public async Task<EnvironmentImportResult> ImportEnvironmentAsync(
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        if (!environmentImporter.CanImport(payload))
            return new EnvironmentImportResult { Warnings = ["Unrecognised environment file format."] };

        var result = await environmentImporter.ImportAsync(payload, cancellationToken);
        var existingNames = environmentRepo.Environments.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var env in result.Environments)
        {
            env.Name = ResolveNameCollision(env.Name, existingNames);
            existingNames.Add(env.Name);
            await environmentRepo.AddImportedEnvironmentAsync(env);
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
