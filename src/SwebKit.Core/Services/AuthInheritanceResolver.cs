using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Resolves auth by walking: request.Auth → nearest folder ancestor.DefaultAuth → collection.DefaultAuth.
/// Returns the first non-null, non-Inherited config, together with the ancestor's display name.
/// </summary>
public sealed class AuthInheritanceResolver : IAuthInheritanceResolver
{
    private static readonly AuthConfig _noAuth = new() { Type = AuthType.None };

    public (AuthConfig ResolvedAuth, string? InheritedFromName) Resolve(
        HttpRequestEntry request,
        ApiCollection collection)
    {
        // Request has its own explicit auth (anything other than Inherited/null)
        if (request.Auth is { Type: not AuthType.Inherited })
            return (request.Auth, null);

        // Walk the folder tree looking for the request node, accumulating path
        var path = new List<ApiCollectionNode>();
        if (FindRequest(collection.Nodes, request.Id, path))
        {
            // Walk ancestors nearest-first (path ends at the direct parent folder)
            for (var i = path.Count - 1; i >= 0; i--)
            {
                var node = path[i];
                if (node.DefaultAuth is { Type: not AuthType.None and not AuthType.Inherited })
                    return (node.DefaultAuth, node.Name);
            }
        }

        // Fall back to collection default
        if (collection.DefaultAuth is { Type: not AuthType.None and not AuthType.Inherited })
            return (collection.DefaultAuth, collection.Name);

        return (_noAuth, null);
    }

    private static bool FindRequest(List<ApiCollectionNode> nodes, string requestId, List<ApiCollectionNode> path)
    {
        foreach (var node in nodes)
        {
            if (node.Type == ApiCollectionNodeType.Request && node.Request?.Id == requestId)
                return true;

            if (node.Type == ApiCollectionNodeType.Folder)
            {
                path.Add(node);
                if (FindRequest(node.Children, requestId, path))
                    return true;
                path.RemoveAt(path.Count - 1);
            }
        }
        return false;
    }
}
