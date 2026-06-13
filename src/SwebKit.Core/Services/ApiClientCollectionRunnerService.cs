using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

public sealed class ApiClientCollectionRunnerService(IHttpRequestExecutor executor)
{
    public async Task<IReadOnlyList<CollectionRunItemResult>> RunAsync(
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        Func<CollectionRunItemResult, Task>? onResult = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<CollectionRunItemResult>();

        foreach (var request in FlattenRequests(collection.Nodes))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CollectionRunItemResult item;

            if (request.Method == ApiRequestMethod.WebSocket)
            {
                item = new CollectionRunItemResult
                {
                    RequestId = request.Id,
                    RequestName = request.Name,
                    Method = request.Method,
                    ErrorMessage = "WebSocket requests are skipped by the collection runner.",
                };
            }
            else
            {
                try
                {
                    var result = await executor.ExecuteAsync(request, collection, activeEnvironment, cancellationToken);
                    item = new CollectionRunItemResult
                    {
                        RequestId = request.Id,
                        RequestName = request.Name,
                        Method = request.Method,
                        Result = result,
                        ErrorMessage = result.ErrorMessage,
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    item = new CollectionRunItemResult
                    {
                        RequestId = request.Id,
                        RequestName = request.Name,
                        Method = request.Method,
                        ErrorMessage = ex.Message,
                    };
                }
            }

            results.Add(item);
            if (onResult is not null)
            {
                await onResult(item);
            }
        }

        return results;
    }

    private static IReadOnlyList<HttpRequestEntry> FlattenRequests(List<ApiCollectionNode> nodes)
    {
        var requests = new List<HttpRequestEntry>();
        AddRequests(nodes, requests);
        return requests;
    }

    private static void AddRequests(List<ApiCollectionNode> nodes, List<HttpRequestEntry> requests)
    {
        foreach (var node in nodes)
        {
            if (node.Request is not null)
            {
                requests.Add(node.Request);
            }

            if (node.Children.Count > 0)
            {
                AddRequests(node.Children, requests);
            }
        }
    }
}
