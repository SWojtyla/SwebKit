using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class ApiClientCollectionRunnerServiceTests
{
    [Fact]
    public async Task RunAsync_ExecutesRequestsSequentially()
    {
        var executor = new RecordingExecutor();
        var service = new ApiClientCollectionRunnerService(executor);
        var collection = new ApiCollection
        {
            Nodes =
            [
                RequestNode("one"),
                new ApiCollectionNode
                {
                    Id = "folder",
                    Type = ApiCollectionNodeType.Folder,
                    Children = [RequestNode("two")],
                },
            ],
        };

        var results = await service.RunAsync(collection, null);

        Assert.Equal(["one", "two"], executor.RequestNames);
        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Null(result.ErrorMessage));
    }

    [Fact]
    public async Task RunAsync_SkipsWebSocketRequests()
    {
        var executor = new RecordingExecutor();
        var service = new ApiClientCollectionRunnerService(executor);
        var collection = new ApiCollection
        {
            Nodes =
            [
                RequestNode("socket", ApiRequestMethod.WebSocket),
                RequestNode("rest"),
            ],
        };

        var results = await service.RunAsync(collection, null);

        Assert.Equal(["rest"], executor.RequestNames);
        Assert.Contains(results, result => result.RequestName == "socket" && result.ErrorMessage!.Contains("skipped"));
    }

    private static ApiCollectionNode RequestNode(string name, ApiRequestMethod method = ApiRequestMethod.Get)
    {
        return new ApiCollectionNode
        {
            Id = name,
            Type = ApiCollectionNodeType.Request,
            Name = name,
            Request = new HttpRequestEntry
            {
                Id = name,
                Name = name,
                Method = method,
                Url = "https://example.com",
            },
        };
    }

    private sealed class RecordingExecutor : IHttpRequestExecutor
    {
        public List<string> RequestNames { get; } = [];

        public Task<HttpRequestResult> ExecuteAsync(
            HttpRequestEntry request,
            ApiCollection collection,
            ApiEnvironment? activeEnvironment,
            CancellationToken cancellationToken = default)
        {
            RequestNames.Add(request.Name);
            return Task.FromResult(new HttpRequestResult
            {
                StatusCode = 200,
                StatusText = "200 OK",
                Method = request.Method.ToString().ToUpperInvariant(),
                ResolvedUrl = request.Url,
            });
        }
    }
}
