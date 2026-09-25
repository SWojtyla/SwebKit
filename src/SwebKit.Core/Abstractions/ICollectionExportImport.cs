using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Imports one or more collections from a byte payload.
/// </summary>
public interface ICollectionImporter
{
    /// <summary>
    /// Returns <c>true</c> when this importer can handle the given bytes.
    /// Used for auto-detection when the file extension is not definitive.
    /// </summary>
    bool CanImport(byte[] payload);

    /// <summary>
    /// Parses <paramref name="payload"/> and returns a structured import result.
    /// Never throws for parse-level errors — problems are reported in
    /// <see cref="CollectionImportResult.Warnings"/>.
    /// </summary>
    Task<CollectionImportResult> ImportAsync(
        byte[] payload,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Imports a standalone environment file (not attached to a collection).
/// </summary>
public interface IEnvironmentImporter
{
    bool CanImport(byte[] payload);

    Task<EnvironmentImportResult> ImportAsync(
        byte[] payload,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a collection import attempt.</summary>
public sealed class CollectionImportResult
{
    /// <summary>Collections parsed from the payload. May be empty on failure.</summary>
    public IReadOnlyList<ApiCollection> Collections { get; init; } = [];

    /// <summary>
    /// Environments extracted from the payload (e.g. Postman collection variables become
    /// a named environment).
    /// </summary>
    public IReadOnlyList<ApiEnvironment> Environments { get; init; } = [];

    /// <summary>Number of request entries parsed (across all collections).</summary>
    public int RequestCount { get; init; }

    /// <summary>Number of capture rules parsed.</summary>
    public int CaptureRuleCount { get; init; }

    /// <summary>Number of auth configs that require the user to re-enter credentials.</summary>
    public int AuthConfigsRequiringReEntry { get; init; }

    /// <summary>Number of collection-level variables promoted to a new environment.</summary>
    public int VariablesExtractedAsEnvironment { get; init; }

    /// <summary>Human-readable warnings produced during import (parse errors, skipped fields, etc.).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Outcome of a standalone environment import.</summary>
public sealed class EnvironmentImportResult
{
    public IReadOnlyList<ApiEnvironment> Environments { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
