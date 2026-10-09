using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Factory for creating demo API collections that appear in demo mode.
/// These collections contain sample requests for popular free APIs to help users
/// learn the tool and test functionality without configuration.
/// </summary>
public static class DemoApiCollectionFactory
{
    public const string DemoCollectionId = "__demo__samples";
    private const string DemoCollectionName = "Demo API Samples";

    /// <summary>
    /// Id of the synthetic chain the sidecar overlays onto <c>GET /api/api-client/chains</c> in
    /// demo mode (api-request-chains). It is produced on the fly by <see cref="CreateDemoChain"/>,
    /// never persisted — the same treatment the demo collection itself gets.
    /// </summary>
    public const string DemoChainId = "__demo__chain_jsonplaceholder_crud";

    /// <summary>
    /// Creates a demo API collection with sample requests for various free APIs.
    /// This collection is designed to appear automatically when demo mode is enabled.
    /// </summary>
    public static ApiCollection CreateDemoCollection()
    {
        return new ApiCollection
        {
            Id = DemoCollectionId,
            Name = DemoCollectionName,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Nodes = CreateDemoNodes()
        };
    }

    private static List<ApiCollectionNode> CreateDemoNodes()
    {
        var nodes = new List<ApiCollectionNode>();

        // JSONPlaceholder folder with REST API examples
        nodes.Add(CreateJsonPlaceholderFolder());

        // HTTPBin folder with HTTP testing examples
        nodes.Add(CreateHttpBinFolder());

        // GitHub API folder with real API examples
        nodes.Add(CreateGitHubApiFolder());

        // Request-chain folder demonstrating dependsOnRequestIds + capture rules
        nodes.Add(CreateRequestChainFolder());

        return nodes;
    }

    private static ApiCollectionNode CreateJsonPlaceholderFolder()
    {
        var folder = new ApiCollectionNode
        {
            Id = "__demo__jsonplaceholder",
            Name = "JSONPlaceholder",
            Type = ApiCollectionNodeType.Folder,
            IsExpanded = true,
            Children = new List<ApiCollectionNode>()
        };

        folder.Children.Add(CreateGetAllPostsRequest());
        folder.Children.Add(CreateGetPostByIdRequest());
        folder.Children.Add(CreateCreatePostRequest());
        folder.Children.Add(CreateUpdatePostRequest());
        folder.Children.Add(CreateDeletePostRequest());
        folder.Children.Add(CreatePatchPostRequest());
        folder.Children.Add(CreateGetCommentsRequest());
        folder.Children.Add(CreateGetUsersRequest());

        return folder;
    }

    private static ApiCollectionNode CreateHttpBinFolder()
    {
        var folder = new ApiCollectionNode
        {
            Id = "__demo__httpbin",
            Name = "HTTPBin",
            Type = ApiCollectionNodeType.Folder,
            IsExpanded = true,
            Children = new List<ApiCollectionNode>()
        };

        folder.Children.Add(CreateHttpBinGetRequest());
        folder.Children.Add(CreateHttpBinPostRequest());
        folder.Children.Add(CreateHttpBinPutRequest());
        folder.Children.Add(CreateHttpBinDeleteRequest());
        folder.Children.Add(CreateHttpBinStatusRequest());
        folder.Children.Add(CreateHttpBinDelayRequest());
        folder.Children.Add(CreateHttpBinHeadersRequest());

        return folder;
    }

    private static ApiCollectionNode CreateGitHubApiFolder()
    {
        var folder = new ApiCollectionNode
        {
            Id = "__demo__github",
            Name = "GitHub API",
            Type = ApiCollectionNodeType.Folder,
            IsExpanded = true,
            Children = new List<ApiCollectionNode>()
        };

        folder.Children.Add(CreateGitHubGetUserRequest());
        folder.Children.Add(CreateGitHubGetRepoRequest());
        folder.Children.Add(CreateGitHubListReposRequest());

        return folder;
    }

    #region JSONPlaceholder Requests

    private static ApiCollectionNode CreateGetAllPostsRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__jsonplaceholder_get_posts",
            Name = "GET /posts",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__jsonplaceholder_get_posts",
                Name = "List all posts",
                Method = ApiRequestMethod.Get,
                Url = "https://jsonplaceholder.typicode.com/posts",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateGetPostByIdRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__jsonplaceholder_get_post_1",
            Name = "GET /posts/1",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__jsonplaceholder_get_post_1",
                Name = "Get single post",
                Method = ApiRequestMethod.Get,
                Url = "https://jsonplaceholder.typicode.com/posts/1",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateCreatePostRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__jsonplaceholder_create_post",
            Name = "POST /posts",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__jsonplaceholder_create_post",
                Name = "Create new post",
                Method = ApiRequestMethod.Post,
                Url = "https://jsonplaceholder.typicode.com/posts",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Content-Type", Value = "application/json" },
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" }
                },
                Body = new RequestBody
                {
                    Mode = RequestBodyMode.Json,
                    ContentType = "application/json",
                    RawContent = "{\n  \"title\": \"foo\",\n  \"body\": \"bar\",\n  \"userId\": 1\n}"
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateUpdatePostRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__jsonplaceholder_update_post",
            Name = "PUT /posts/1",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__jsonplaceholder_update_post",
                Name = "Update existing post",
                Method = ApiRequestMethod.Put,
                Url = "https://jsonplaceholder.typicode.com/posts/1",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Content-Type", Value = "application/json" },
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" }
                },
                Body = new RequestBody
                {
                    Mode = RequestBodyMode.Json,
                    ContentType = "application/json",
                    RawContent = "{\n  \"id\": 1,\n  \"title\": \"updated title\",\n  \"body\": \"updated body\",\n  \"userId\": 1\n}"
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateDeletePostRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__jsonplaceholder_delete_post",
            Name = "DELETE /posts/1",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__jsonplaceholder_delete_post",
                Name = "Delete post",
                Method = ApiRequestMethod.Delete,
                Url = "https://jsonplaceholder.typicode.com/posts/1",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreatePatchPostRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__jsonplaceholder_patch_post",
            Name = "PATCH /posts/1",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__jsonplaceholder_patch_post",
                Name = "Partial update post",
                Method = ApiRequestMethod.Patch,
                Url = "https://jsonplaceholder.typicode.com/posts/1",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Content-Type", Value = "application/json" },
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" }
                },
                Body = new RequestBody
                {
                    Mode = RequestBodyMode.Json,
                    ContentType = "application/json",
                    RawContent = "{\n  \"title\": \"patched title\"\n}"
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateGetCommentsRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__jsonplaceholder_get_comments",
            Name = "GET /comments",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__jsonplaceholder_get_comments",
                Name = "List all comments",
                Method = ApiRequestMethod.Get,
                Url = "https://jsonplaceholder.typicode.com/comments",
                QueryParams = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "_limit", Value = "5" }
                },
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateGetUsersRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__jsonplaceholder_get_users",
            Name = "GET /users",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__jsonplaceholder_get_users",
                Name = "List all users",
                Method = ApiRequestMethod.Get,
                Url = "https://jsonplaceholder.typicode.com/users",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    #endregion

    #region HTTPBin Requests

    private static ApiCollectionNode CreateHttpBinGetRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__httpbin_get",
            Name = "GET /get",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__httpbin_get",
                Name = "Echo GET request",
                Method = ApiRequestMethod.Get,
                Url = "https://httpbin.org/get",
                QueryParams = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "test", Value = "value" }
                },
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" },
                    new KeyValuePair<string> { Key = "X-Custom-Header", Value = "Demo-Value" }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateHttpBinPostRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__httpbin_post",
            Name = "POST /post",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__httpbin_post",
                Name = "Echo POST request",
                Method = ApiRequestMethod.Post,
                Url = "https://httpbin.org/post",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Content-Type", Value = "application/json" },
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" },
                    new KeyValuePair<string> { Key = "X-Custom-Header", Value = "Demo-Post" }
                },
                Body = new RequestBody
                {
                    Mode = RequestBodyMode.Json,
                    ContentType = "application/json",
                    RawContent = "{\n  \"test\": \"data\",\n  \"timestamp\": \"2024-01-01T00:00:00Z\"\n}"
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateHttpBinPutRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__httpbin_put",
            Name = "PUT /put",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__httpbin_put",
                Name = "Echo PUT request",
                Method = ApiRequestMethod.Put,
                Url = "https://httpbin.org/put",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Content-Type", Value = "application/json" },
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" }
                },
                Body = new RequestBody
                {
                    Mode = RequestBodyMode.Json,
                    ContentType = "application/json",
                    RawContent = "{\n  \"method\": \"PUT\",\n  \"data\": \"test\"\n}"
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateHttpBinDeleteRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__httpbin_delete",
            Name = "DELETE /delete",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__httpbin_delete",
                Name = "Echo DELETE request",
                Method = ApiRequestMethod.Delete,
                Url = "https://httpbin.org/delete",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateHttpBinStatusRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__httpbin_status",
            Name = "GET /status/200",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__httpbin_status",
                Name = "Test status code 200",
                Method = ApiRequestMethod.Get,
                Url = "https://httpbin.org/status/200",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateHttpBinDelayRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__httpbin_delay",
            Name = "GET /delay/3",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__httpbin_delay",
                Name = "Test delay response",
                Method = ApiRequestMethod.Get,
                Url = "https://httpbin.org/delay/3",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateHttpBinHeadersRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__httpbin_headers",
            Name = "GET /headers",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__httpbin_headers",
                Name = "Echo request headers",
                Method = ApiRequestMethod.Get,
                Url = "https://httpbin.org/headers",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "X-Custom-Header", Value = "Test-Value" },
                    new KeyValuePair<string> { Key = "User-Agent", Value = "SwebKit-Demo" }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    #endregion

    #region GitHub API Requests

    private static ApiCollectionNode CreateGitHubGetUserRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__github_get_user",
            Name = "GET /users/octocat",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__github_get_user",
                Name = "Get GitHub user profile",
                Method = ApiRequestMethod.Get,
                Url = "https://api.github.com/users/octocat",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Accept", Value = "application/vnd.github+json" },
                    new KeyValuePair<string> { Key = "X-GitHub-Api-Version", Value = "2022-11-28" }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateGitHubGetRepoRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__github_get_repo",
            Name = "GET /repos/octocat/Hello-World",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__github_get_repo",
                Name = "Get GitHub repository info",
                Method = ApiRequestMethod.Get,
                Url = "https://api.github.com/repos/octocat/Hello-World",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Accept", Value = "application/vnd.github+json" },
                    new KeyValuePair<string> { Key = "X-GitHub-Api-Version", Value = "2022-11-28" }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateGitHubListReposRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__github_list_repos",
            Name = "GET /users/octocat/repos",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__github_list_repos",
                Name = "List user repositories",
                Method = ApiRequestMethod.Get,
                Url = "https://api.github.com/users/octocat/repos",
                QueryParams = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "per_page", Value = "5" }
                },
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Accept", Value = "application/vnd.github+json" },
                    new KeyValuePair<string> { Key = "X-GitHub-Api-Version", Value = "2022-11-28" }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    #endregion

    #region Request chain (dependsOnRequestIds demo)

    /// <summary>
    /// A two-step dependency chain: "Get token" captures a uuid into the <c>demoToken</c> collection
    /// variable and "List orders" depends on it and sends the captured value as a bearer token —
    /// httpbin echoes the headers back, so the hand-off is visible in the response body.
    /// </summary>
    private static ApiCollectionNode CreateRequestChainFolder()
    {
        var folder = new ApiCollectionNode
        {
            Id = "__demo__chain",
            Name = "Request chain",
            Type = ApiCollectionNodeType.Folder,
            IsExpanded = true,
            Children = new List<ApiCollectionNode>()
        };

        folder.Children.Add(CreateChainGetTokenRequest());
        folder.Children.Add(CreateChainListOrdersRequest());

        // Named-chain demo steps (api-request-chains): create → read → delete a JSONPlaceholder
        // post, where the create captures $.id into demoPostId and the follow-ups read it through
        // the run-scoped variable overlay via {{demoPostId}}.
        folder.Children.Add(CreateChainCreatePostRequest());
        folder.Children.Add(CreateChainGetCapturedPostRequest());
        folder.Children.Add(CreateChainDeleteCapturedPostRequest());

        return folder;
    }

    private static ApiCollectionNode CreateChainGetTokenRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__chain_get_token",
            Name = "GET /uuid (token)",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__chain_get_token",
                Name = "Get token",
                Method = ApiRequestMethod.Get,
                Url = "https://httpbin.org/uuid",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" }
                },
                CaptureRules = new List<CaptureRule>
                {
                    new CaptureRule
                    {
                        Id = "__demo__chain_capture_token",
                        TargetVariable = "demoToken",
                        TargetScope = "collection",
                        Source = CaptureSource.BodyJsonPath,
                        JsonPath = "$.uuid",
                    }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateChainListOrdersRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__chain_list_orders",
            Name = "GET /anything/orders",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__chain_list_orders",
                Name = "List orders",
                Method = ApiRequestMethod.Get,
                Url = "https://httpbin.org/anything/orders",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" },
                    new KeyValuePair<string> { Key = "Authorization", Value = "Bearer {{demoToken}}" }
                },
                DependsOnRequestIds = new List<string> { "__demo__chain_get_token" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateChainCreatePostRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__chain_create_post",
            Name = "POST /posts (create)",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__chain_create_post",
                Name = "Create post (captures id)",
                Method = ApiRequestMethod.Post,
                Url = "https://jsonplaceholder.typicode.com/posts",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Content-Type", Value = "application/json" },
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" }
                },
                Body = new RequestBody
                {
                    Mode = RequestBodyMode.Json,
                    ContentType = "application/json",
                    RawContent = "{\n  \"title\": \"swebkit demo post\",\n  \"body\": \"created by a request chain\",\n  \"userId\": 1\n}"
                },
                CaptureRules = new List<CaptureRule>
                {
                    new CaptureRule
                    {
                        Id = "__demo__chain_capture_post_id",
                        TargetVariable = "demoPostId",
                        TargetScope = "collection",
                        Source = CaptureSource.BodyJsonPath,
                        JsonPath = "$.id",
                    }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateChainGetCapturedPostRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__chain_get_post",
            Name = "GET /posts/{{demoPostId}}",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__chain_get_post",
                Name = "Get captured post",
                Method = ApiRequestMethod.Get,
                Url = "https://jsonplaceholder.typicode.com/posts/{{demoPostId}}",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApiCollectionNode CreateChainDeleteCapturedPostRequest()
    {
        return new ApiCollectionNode
        {
            Id = "__demo__chain_delete_post",
            Name = "DELETE /posts/{{demoPostId}}",
            Type = ApiCollectionNodeType.Request,
            Request = new HttpRequestEntry
            {
                Id = "__demo__chain_delete_post",
                Name = "Delete captured post",
                Method = ApiRequestMethod.Delete,
                Url = "https://jsonplaceholder.typicode.com/posts/{{demoPostId}}",
                Headers = new List<KeyValuePair<string>>
                {
                    new KeyValuePair<string> { Key = "Accept", Value = "application/json" }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    #endregion

    /// <summary>
    /// The demo named chain overlaid onto the chains list in demo mode (api-request-chains):
    /// JSONPlaceholder create → read → delete, where the create step's capture rule feeds
    /// <c>demoPostId</c> into the run-scoped overlay the later steps' URLs resolve against.
    /// </summary>
    public static ApiChain CreateDemoChain()
    {
        return new ApiChain
        {
            Id = DemoChainId,
            Name = "JSONPlaceholder CRUD flow",
            Description = "POST /posts → GET → DELETE, reusing the id captured from the create response.",
            Steps =
            [
                new ApiChainStep { Id = "__demo__chain_step_create", CollectionId = DemoCollectionId, RequestId = "__demo__chain_create_post" },
                new ApiChainStep { Id = "__demo__chain_step_get", CollectionId = DemoCollectionId, RequestId = "__demo__chain_get_post" },
                new ApiChainStep { Id = "__demo__chain_step_delete", CollectionId = DemoCollectionId, RequestId = "__demo__chain_delete_post" },
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Checks if a collection is the demo collection.
    /// </summary>
    public static bool IsDemoCollection(ApiCollection collection)
    {
        return collection.Id == DemoCollectionId;
    }

    /// <summary>
    /// Checks if a collection node is part of the demo collection.
    /// </summary>
    public static bool IsDemoNode(ApiCollectionNode node)
    {
        return node.Id.StartsWith("__demo__", StringComparison.Ordinal);
    }
}
