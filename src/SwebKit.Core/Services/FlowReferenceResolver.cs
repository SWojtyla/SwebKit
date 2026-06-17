using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Resolves request and environment references for flow execution.
/// </summary>
public sealed class FlowReferenceResolver
{
    private readonly CollectionRepository _collectionRepository;
    private readonly EnvironmentRepository _environmentRepository;
    private readonly LinkedCollectionRootRepository _linkedRootRepository;

    public FlowReferenceResolver(
        CollectionRepository collectionRepository,
        EnvironmentRepository environmentRepository,
        LinkedCollectionRootRepository linkedRootRepository)
    {
        _collectionRepository = collectionRepository;
        _environmentRepository = environmentRepository;
        _linkedRootRepository = linkedRootRepository;
    }

    /// <summary>
    /// Resolves a request reference to its actual request definition.
    /// </summary>
    public async Task<HttpRequestEntry?> ResolveRequestAsync(ApiRequestReference reference)
    {
        if (reference.SourceKind == ApiRequestReferenceKind.LocalCollection)
        {
            var (_, request) = _collectionRepository.FindRequest(reference.RequestId);
            return request;
        }
        else if (reference.SourceKind == ApiRequestReferenceKind.LinkedRoot)
        {
            var root = await _linkedRootRepository.GetByIdAsync(reference.SourceId);
            if (root is null || string.IsNullOrWhiteSpace(root.LocalPath)) return null;

            // Load linked collections for this root
            var linkedCollections = await _linkedRootRepository.LoadLinkedCollectionsAsync(root.Id);
            foreach (var collection in linkedCollections)
            {
                var (_, request) = FindRequestInNodes(collection.Nodes, reference.RequestId);
                if (request is not null) return request;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves an environment reference to its actual environment definition.
    /// </summary>
    public async Task<ApiEnvironment?> ResolveEnvironmentAsync(ApiEnvironmentReference reference)
    {
        if (reference.SourceKind == ApiEnvironmentReferenceKind.Local)
        {
            var environments = await _environmentRepository.GetAllAsync();
            return environments.FirstOrDefault(e => e.Id == reference.EnvironmentId);
        }
        else if (reference.SourceKind == ApiEnvironmentReferenceKind.LinkedRoot)
        {
            if (reference.SourceId is null) return null;
            var root = await _linkedRootRepository.GetByIdAsync(reference.SourceId);
            if (root is null || string.IsNullOrWhiteSpace(root.LocalPath)) return null;

            var envDir = System.IO.Path.Combine(root.LocalPath, ".swebkit-api", "environments");
            if (!System.IO.Directory.Exists(envDir)) return null;

            foreach (var file in System.IO.Directory.GetFiles(envDir, "*.swebenv.json"))
            {
                try
                {
                    var json = await System.IO.File.ReadAllTextAsync(file);
                    var env = JsonSerializer.Deserialize<ApiEnvironment>(json);
                    if (env?.Id == reference.EnvironmentId) return env;
                }
                catch { }
            }
        }

        return null;
    }

    private static (ApiCollection? Collection, HttpRequestEntry? Request) FindRequestInNodes(
        List<ApiCollectionNode> nodes,
        string requestId)
    {
        foreach (var node in nodes)
        {
            if (node.Type == ApiCollectionNodeType.Request && node.Request?.Id == requestId)
                return (null, node.Request);

            if (node.Type == ApiCollectionNodeType.Folder)
            {
                var found = FindRequestInNodes(node.Children, requestId);
                if (found.Request is not null)
                    return found;
            }
        }
        return (null, null);
    }
}
