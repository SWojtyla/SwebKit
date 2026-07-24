using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using Microsoft.Extensions.Logging;

namespace SwebKit.Core.Services;

/// <summary>
/// Implementation of <see cref="IApiClientAgentService"/> that operates on both local
/// and linked collections. Used by agent tools and (eventually) the API Client page.
/// </summary>
public sealed class ApiClientAgentService : IApiClientAgentService
{
    private readonly CollectionRepository _localRepo;
    private readonly LinkedCollectionRootRepository _linkedRootRepo;
    private readonly LinkedCollectionFileService _linkedFileService;
    private readonly IAppEventBus _events;
    private readonly ILogger<ApiClientAgentService>? _logger;

    public ApiClientAgentService(
        CollectionRepository localRepo,
        LinkedCollectionRootRepository linkedRootRepo,
        LinkedCollectionFileService linkedFileService,
        IAppEventBus events,
        ILogger<ApiClientAgentService>? logger = null)
    {
        _localRepo = localRepo;
        _linkedRootRepo = linkedRootRepo;
        _linkedFileService = linkedFileService;
        _events = events;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ApiRequestSummary>> SearchRequestsAsync(string? query = null, CancellationToken ct = default)
    {
        var results = new List<ApiRequestSummary>();
        var allCollections = await GetAllCollectionsAsync(ct);

        foreach (var (collection, origin, linkedRootId) in allCollections)
        {
            CollectRequests(collection, collection.Nodes, "", origin, linkedRootId, collection.Name, results);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            results = results
                .Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            r.Url.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            r.Method.ToString().Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return results;
    }

    public async Task<ApiRequestSnapshot?> GetRequestAsync(string requestId, CancellationToken ct = default)
    {
        var allCollections = await GetAllCollectionsAsync(ct);

        foreach (var (collection, origin, linkedRootId) in allCollections)
        {
            var (node, folderPath) = FindRequestNode(collection.Nodes, requestId, "");
            if (node?.Request is not null)
            {
                return BuildSnapshot(
                    node.Request, collection, origin, linkedRootId, folderPath);
            }
        }

        return null;
    }

    public async Task<ApiClientMutationResult> CreateRequestAsync(
        string collectionId,
        string? folderPath,
        string name,
        ApiRequestMethod method,
        string url,
        CancellationToken ct = default)
    {
        var (collection, origin, linkedRootId) = await FindCollectionAsync(collectionId, ct);
        if (collection is null)
            return new ApiClientMutationResult { IsSuccess = false, ErrorMessage = $"Collection '{collectionId}' not found." };

        var request = new HttpRequestEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Method = method,
            Url = url,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var node = new ApiCollectionNode
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = ApiCollectionNodeType.Request,
            Name = name,
            Request = request,
        };

        if (string.IsNullOrEmpty(folderPath))
        {
            collection.Nodes.Add(node);
        }
        else
        {
            var folder = FindFolder(collection.Nodes, folderPath);
            if (folder is null)
                return new ApiClientMutationResult { IsSuccess = false, ErrorMessage = $"Folder '{folderPath}' not found." };
            folder.Children.Add(node);
        }

        await PersistAsync(collection, origin, linkedRootId, ct);

        await _events.PublishAsync(new ApiClientDataChanged
        {
            CollectionId = collectionId,
            RequestId = request.Id,
            ChangeType = "create"
        });

        return new ApiClientMutationResult
        {
            IsSuccess = true,
            RequestId = request.Id,
            CollectionId = collectionId
        };
    }

    public async Task<ApiClientMutationResult> UpdateRequestAsync(
        string requestId,
        string? name = null,
        ApiRequestMethod? method = null,
        string? url = null,
        CancellationToken ct = default)
    {
        var (collection, origin, linkedRootId) = await FindCollectionByRequestAsync(requestId, ct);
        if (collection is null)
            return new ApiClientMutationResult { IsSuccess = false, ErrorMessage = $"Request '{requestId}' not found." };

        var (node, _) = FindRequestNode(collection.Nodes, requestId, "");
        if (node?.Request is null)
            return new ApiClientMutationResult { IsSuccess = false, ErrorMessage = $"Request '{requestId}' not found." };

        if (name is not null) { node.Request.Name = name; node.Name = name; }
        if (method is not null) node.Request.Method = method.Value;
        if (url is not null) node.Request.Url = url;
        node.Request.UpdatedAt = DateTimeOffset.UtcNow;

        await PersistAsync(collection, origin, linkedRootId, ct);

        await _events.PublishAsync(new ApiClientDataChanged
        {
            CollectionId = collection.Id,
            RequestId = requestId,
            ChangeType = "update"
        });

        return new ApiClientMutationResult { IsSuccess = true, RequestId = requestId, CollectionId = collection.Id };
    }

    public async Task<ApiClientMutationResult> DuplicateRequestAsync(string requestId, CancellationToken ct = default)
    {
        var (collection, origin, linkedRootId) = await FindCollectionByRequestAsync(requestId, ct);
        if (collection is null)
            return new ApiClientMutationResult { IsSuccess = false, ErrorMessage = $"Request '{requestId}' not found." };

        var (node, folderPath) = FindRequestNode(collection.Nodes, requestId, "");
        if (node?.Request is null)
            return new ApiClientMutationResult { IsSuccess = false, ErrorMessage = $"Request '{requestId}' not found." };

        var copy = new HttpRequestEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = node.Request.Name + " (copy)",
            Method = node.Request.Method,
            Url = node.Request.Url,
            Headers = node.Request.Headers.Select(h => new KeyValuePair<string> { Key = h.Key, Value = h.Value, IsEnabled = h.IsEnabled }).ToList(),
            QueryParams = node.Request.QueryParams.Select(q => new KeyValuePair<string> { Key = q.Key, Value = q.Value, IsEnabled = q.IsEnabled }).ToList(),
            Body = new RequestBody
            {
                Mode = node.Request.Body.Mode,
                RawContent = node.Request.Body.RawContent,
                ContentType = node.Request.Body.ContentType,
            },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var copyNode = new ApiCollectionNode
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = ApiCollectionNodeType.Request,
            Name = copy.Name,
            Request = copy,
        };

        // Insert after original in same folder
        if (string.IsNullOrEmpty(folderPath))
        {
            var idx = collection.Nodes.IndexOf(node);
            collection.Nodes.Insert(idx + 1, copyNode);
        }
        else
        {
            var folder = FindFolder(collection.Nodes, folderPath);
            if (folder is not null)
            {
                var idx = folder.Children.IndexOf(node);
                folder.Children.Insert(idx + 1, copyNode);
            }
            else
            {
                collection.Nodes.Add(copyNode);
            }
        }

        await PersistAsync(collection, origin, linkedRootId, ct);

        await _events.PublishAsync(new ApiClientDataChanged
        {
            CollectionId = collection.Id,
            RequestId = copy.Id,
            ChangeType = "create"
        });

        return new ApiClientMutationResult { IsSuccess = true, RequestId = copy.Id, CollectionId = collection.Id };
    }

    public async Task<ApiClientMutationResult> MoveRequestAsync(
        string requestId,
        string? targetFolderPath,
        int? newIndex,
        CancellationToken ct = default)
    {
        var (collection, origin, linkedRootId) = await FindCollectionByRequestAsync(requestId, ct);
        if (collection is null)
            return new ApiClientMutationResult { IsSuccess = false, ErrorMessage = $"Request '{requestId}' not found." };

        var (node, currentFolderPath) = FindRequestNode(collection.Nodes, requestId, "");
        if (node is null)
            return new ApiClientMutationResult { IsSuccess = false, ErrorMessage = $"Request '{requestId}' not found." };

        // Validate target folder BEFORE removing from current location
        ApiCollectionNode? targetFolder = null;
        if (!string.IsNullOrEmpty(targetFolderPath))
        {
            targetFolder = FindFolder(collection.Nodes, targetFolderPath);
            if (targetFolder is null)
                return new ApiClientMutationResult { IsSuccess = false, ErrorMessage = $"Target folder '{targetFolderPath}' not found." };
        }

        // Remove from current location
        if (string.IsNullOrEmpty(currentFolderPath))
            collection.Nodes.Remove(node);
        else
        {
            var currentFolder = FindFolder(collection.Nodes, currentFolderPath);
            currentFolder?.Children.Remove(node);
        }

        // Insert at target
        if (targetFolder is null)
        {
            if (newIndex is int idx && idx >= 0 && idx <= collection.Nodes.Count)
                collection.Nodes.Insert(idx, node);
            else
                collection.Nodes.Add(node);
        }
        else
        {
            if (newIndex is int idx && idx >= 0 && idx <= targetFolder.Children.Count)
                targetFolder.Children.Insert(idx, node);
            else
                targetFolder.Children.Add(node);
        }

        await PersistAsync(collection, origin, linkedRootId, ct);

        await _events.PublishAsync(new ApiClientDataChanged
        {
            CollectionId = collection.Id,
            RequestId = requestId,
            ChangeType = "move"
        });

        return new ApiClientMutationResult { IsSuccess = true, RequestId = requestId, CollectionId = collection.Id };
    }

    public async Task<ApiClientMutationResult> RenameFolderAsync(
        string collectionId,
        string folderPath,
        string newName,
        CancellationToken ct = default)
    {
        var (collection, origin, linkedRootId) = await FindCollectionAsync(collectionId, ct);
        if (collection is null)
            return new ApiClientMutationResult { IsSuccess = false, ErrorMessage = $"Collection '{collectionId}' not found." };

        var folder = FindFolder(collection.Nodes, folderPath);
        if (folder is null)
            return new ApiClientMutationResult { IsSuccess = false, ErrorMessage = $"Folder '{folderPath}' not found." };

        folder.Name = newName.Trim();
        await PersistAsync(collection, origin, linkedRootId, ct);

        await _events.PublishAsync(new ApiClientDataChanged
        {
            CollectionId = collectionId,
            RequestId = null,
            ChangeType = "update"
        });

        return new ApiClientMutationResult { IsSuccess = true, CollectionId = collectionId };
    }

    public async Task<ApiClientMutationResult> DeleteRequestAsync(string requestId, CancellationToken ct = default)
    {
        var (collection, origin, linkedRootId) = await FindCollectionByRequestAsync(requestId, ct);
        if (collection is null)
            return new ApiClientMutationResult { IsSuccess = false, ErrorMessage = $"Request '{requestId}' not found." };

        var (node, folderPath) = FindRequestNode(collection.Nodes, requestId, "");
        if (node is null)
            return new ApiClientMutationResult { IsSuccess = false, ErrorMessage = $"Request '{requestId}' not found." };

        if (string.IsNullOrEmpty(folderPath))
            collection.Nodes.Remove(node);
        else
        {
            var folder = FindFolder(collection.Nodes, folderPath);
            folder?.Children.Remove(node);
        }

        await PersistAsync(collection, origin, linkedRootId, ct);

        await _events.PublishAsync(new ApiClientDataChanged
        {
            CollectionId = collection.Id,
            RequestId = requestId,
            ChangeType = "delete"
        });

        return new ApiClientMutationResult { IsSuccess = true, RequestId = requestId, CollectionId = collection.Id };
    }

    public async Task<ApiClientMutationResult> DeleteFolderAsync(
        string collectionId,
        string folderPath,
        CancellationToken ct = default)
    {
        var (collection, origin, linkedRootId) = await FindCollectionAsync(collectionId, ct);
        if (collection is null)
            return new ApiClientMutationResult { IsSuccess = false, ErrorMessage = $"Collection '{collectionId}' not found." };

        var folder = FindFolder(collection.Nodes, folderPath);
        if (folder is null)
            return new ApiClientMutationResult { IsSuccess = false, ErrorMessage = $"Folder '{folderPath}' not found." };

        // Find and remove from parent
        RemoveNode(collection.Nodes, folder);

        await PersistAsync(collection, origin, linkedRootId, ct);

        await _events.PublishAsync(new ApiClientDataChanged
        {
            CollectionId = collectionId,
            RequestId = null,
            ChangeType = "delete"
        });

        return new ApiClientMutationResult { IsSuccess = true, CollectionId = collectionId };
    }

    public async Task<IReadOnlyList<(string Id, string Name, string Origin, string? LinkedRootId)>> GetCollectionsAsync(CancellationToken ct = default)
    {
        var result = (await GetAllCollectionsAsync(ct))
            .Select(c => (c.Collection.Id, c.Collection.Name, c.Origin, c.LinkedRootId))
            .ToList();
        return result;
    }

    // ── Helpers ──

    private async Task<List<(ApiCollection Collection, string Origin, string? LinkedRootId)>> GetAllCollectionsAsync(CancellationToken ct = default)
    {
        var list = new List<(ApiCollection, string, string?)>();

        // Local collections
        foreach (var c in _localRepo.Collections)
            list.Add((c, "local", null));

        // Linked collections
        foreach (var root in _linkedRootRepo.Roots.Where(r => r.IsEnabled))
        {
            var loaded = await _linkedFileService.LoadRootAsync(root, ct);
            foreach (var c in loaded.Collections)
                list.Add((c, "linked", root.Id));
        }

        return list;
    }

    private async Task<(ApiCollection? Collection, string Origin, string? LinkedRootId)> FindCollectionAsync(string collectionId, CancellationToken ct)
    {
        foreach (var (collection, origin, linkedRootId) in await GetAllCollectionsAsync(ct))
        {
            if (collection.Id == collectionId)
                return (collection, origin, linkedRootId);
        }
        return (null, "", null);
    }

    private async Task<(ApiCollection? Collection, string Origin, string? LinkedRootId)> FindCollectionByRequestAsync(string requestId, CancellationToken ct)
    {
        foreach (var (collection, origin, linkedRootId) in await GetAllCollectionsAsync(ct))
        {
            var (node, _) = FindRequestNode(collection.Nodes, requestId, "");
            if (node is not null)
                return (collection, origin, linkedRootId);
        }
        return (null, "", null);
    }

    private static (ApiCollectionNode? Node, string FolderPath) FindRequestNode(
        List<ApiCollectionNode> nodes, string requestId, string currentPath)
    {
        foreach (var node in nodes)
        {
            if (node.Type == ApiCollectionNodeType.Request && node.Request?.Id == requestId)
                return (node, currentPath);

            if (node.Type == ApiCollectionNodeType.Folder)
            {
                var childPath = string.IsNullOrEmpty(currentPath) ? node.Name : $"{currentPath}/{node.Name}";
                var result = FindRequestNode(node.Children, requestId, childPath);
                if (result.Node is not null)
                    return result;
            }
        }
        return (null, "");
    }

    private static ApiCollectionNode? FindFolder(List<ApiCollectionNode> nodes, string folderPath)
    {
        var parts = folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        ApiCollectionNode? currentFolder = null;
        var current = nodes;

        foreach (var part in parts)
        {
            currentFolder = current.FirstOrDefault(n => n.Type == ApiCollectionNodeType.Folder && n.Name == part);
            if (currentFolder is null) return null;
            current = currentFolder.Children;
        }

        return currentFolder;
    }

    private static bool RemoveNode(List<ApiCollectionNode> nodes, ApiCollectionNode target)
    {
        if (nodes.Remove(target)) return true;
        foreach (var node in nodes.Where(n => n.Type == ApiCollectionNodeType.Folder))
        {
            if (RemoveNode(node.Children, target)) return true;
        }
        return false;
    }

    private static void CollectRequests(
        ApiCollection collection,
        List<ApiCollectionNode> nodes,
        string currentPath,
        string origin,
        string? linkedRootId,
        string collectionName,
        List<ApiRequestSummary> results)
    {
        foreach (var node in nodes)
        {
            if (node.Type == ApiCollectionNodeType.Request && node.Request is not null)
            {
                results.Add(new ApiRequestSummary
                {
                    Id = node.Request.Id,
                    Name = node.Request.Name,
                    CollectionId = collection.Id,
                    CollectionName = collectionName,
                    CollectionOrigin = origin,
                    LinkedRootId = linkedRootId,
                    FolderPath = string.IsNullOrEmpty(currentPath) ? null : currentPath,
                    Method = node.Request.Method,
                    Url = node.Request.Url,
                });
            }
            else if (node.Type == ApiCollectionNodeType.Folder)
            {
                var childPath = string.IsNullOrEmpty(currentPath) ? node.Name : $"{currentPath}/{node.Name}";
                CollectRequests(collection, node.Children, childPath, origin, linkedRootId, collectionName, results);
            }
        }
    }

    private static ApiRequestSnapshot BuildSnapshot(
        HttpRequestEntry request,
        ApiCollection collection,
        string origin,
        string? linkedRootId,
        string folderPath)
    {
        return new ApiRequestSnapshot
        {
            Id = request.Id,
            Name = request.Name,
            CollectionId = collection.Id,
            CollectionName = collection.Name,
            CollectionOrigin = origin,
            LinkedRootId = linkedRootId,
            FolderPath = string.IsNullOrEmpty(folderPath) ? null : folderPath,
            Method = request.Method,
            Url = request.Url,
            Headers = request.Headers
                .Where(h => h.IsEnabled)
                .Select(h => (h.Key, MaskIfSecret(h.Key, h.Value)))
                .ToList(),
            QueryParams = request.QueryParams
                .Where(q => q.IsEnabled)
                .Select(q => (q.Key, q.Value))
                .ToList(),
            BodyContentType = request.Body.ContentType,
            BodyPreview = request.Body.RawContent is { Length: > 200 }
                ? request.Body.RawContent[..200] + "…"
                : request.Body.RawContent,
            AuthType = request.Auth?.Type.ToString() ?? collection.DefaultAuth?.Type.ToString(),
            UpdatedAt = request.UpdatedAt,
        };
    }

    private static string? MaskIfSecret(string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var lower = key.ToLowerInvariant();
        if (lower.Contains("authorization") || lower.Contains("api-key") || lower.Contains("apikey") ||
            lower.Contains("x-api-key") || lower.Contains("token") || lower.Contains("secret") ||
            lower.Contains("password") || lower.Contains("credential") ||
            lower.Contains("cookie") || lower.Contains("set-cookie"))
            return "***";
        return value;
    }

    private async Task PersistAsync(ApiCollection collection, string origin, string? linkedRootId, CancellationToken ct)
    {
        if (origin == "local")
        {
            await _localRepo.UpdateCollectionAsync(collection);
        }
        else if (origin == "linked" && linkedRootId is not null)
        {
            // For linked collections, save each request file individually
            // The LinkedCollectionFileService handles the file-based persistence
            await SaveLinkedCollectionAsync(collection, linkedRootId, ct);
        }
    }

    private async Task SaveLinkedCollectionAsync(ApiCollection collection, string linkedRootId, CancellationToken ct)
    {
        var root = _linkedRootRepo.Roots.FirstOrDefault(r => r.Id == linkedRootId);
        if (root is null) return;

        var loaded = await _linkedFileService.LoadRootAsync(root, ct);
        var apiRootPath = loaded.ApiRootPath;

        // Recursively save all requests in the collection
        SaveNodesRecursive(apiRootPath, collection, collection.Nodes, ct);
    }

    private void SaveNodesRecursive(string apiRootPath, ApiCollection collection, List<ApiCollectionNode> nodes, CancellationToken ct)
    {
        foreach (var node in nodes)
        {
            if (node.Type == ApiCollectionNodeType.Request && node.Request is not null)
            {
                _linkedFileService.SaveRequestAsync(apiRootPath, collection, node.Request, null, ct).GetAwaiter().GetResult();
            }
            else if (node.Type == ApiCollectionNodeType.Folder)
            {
                SaveNodesRecursive(apiRootPath, collection, node.Children, ct);
            }
        }
    }
}
