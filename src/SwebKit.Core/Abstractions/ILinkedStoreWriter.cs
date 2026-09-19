using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Persistence routing for entities that live in a linked API project root instead of app-local
/// JSON stores. Implemented by the host (sidecar's linked-collections state); returns false when
/// the entity is not linked so callers can fall back to the local repositories.
/// </summary>
public interface ILinkedStoreWriter
{
    /// <summary>Persists collection-level data (manifest) to the linked folder. False when the collection is not linked.</summary>
    Task<bool> TryWriteCollectionAsync(ApiCollection collection, CancellationToken cancellationToken = default);

    /// <summary>Persists an environment to its linked file. False when the environment is not linked.</summary>
    Task<bool> TryWriteEnvironmentAsync(ApiEnvironment environment, CancellationToken cancellationToken = default);
}
