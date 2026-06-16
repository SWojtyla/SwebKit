using Moq;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class ApiClientFlowRunnerServiceTests
{
    private readonly Mock<IHttpRequestExecutor> _requestExecutorMock = new();
    private readonly Mock<IVariableSubstitutionService> _substitutionMock = new();
    private readonly Mock<IApiFlowRepository> _flowRepoMock = new();
    private readonly CollectionRepository _collectionRepo;
    private readonly EnvironmentRepository _envRepo;
    private readonly LinkedCollectionRootRepository _linkedRootRepo;
    private readonly ApiClientFlowRunnerService _runner;

    private readonly string _testRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SwebKit-Test",
        Guid.NewGuid().ToString("N"));

    public ApiClientFlowRunnerServiceTests()
    {
        Directory.CreateDirectory(_testRoot);
        Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", _testRoot);

        _collectionRepo = new CollectionRepository();
        _envRepo = new EnvironmentRepository();
        _linkedRootRepo = new LinkedCollectionRootRepository();

        _runner = new ApiClientFlowRunnerService(
            _requestExecutorMock.Object,
            _substitutionMock.Object,
            _flowRepoMock.Object,
            _collectionRepo,
            _envRepo,
            _linkedRootRepo);
    }

    // ─── IsSecretLookingKey Tests ───────────────────────────────────────────────

    [Theory]
    [InlineData("token")]
    [InlineData("apiKey")]
    [InlineData("password")]
    [InlineData("MySecretValue")]
    [InlineData("AUTH_TOKEN")]
    [InlineData("refreshToken")]
    public void IsSecretLookingKey_ReturnsTrueForSecretPatterns(string key)
    {
        Assert.True(_runner.IsSecretLookingKey(key));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("userId")]
    [InlineData("count")]
    [InlineData("description")]
    public void IsSecretLookingKey_ReturnsFalseForNonSecretPatterns(string key)
    {
        Assert.False(_runner.IsSecretLookingKey(key));
    }

    // ─── MaskSecrets Tests ─────────────────────────────────────────────────────

    [Fact]
    public void MaskSecrets_MasksSecretLookingKeys()
    {
        var values = new Dictionary<string, string>
        {
            ["token"] = "abc123",
            ["name"] = "John",
            ["password"] = "secret123",
            ["userId"] = "42"
        };

        var masked = _runner.MaskSecrets(values);

        Assert.Equal("***MASKED***", masked["token"]);
        Assert.Equal("John", masked["name"]);
        Assert.Equal("***MASKED***", masked["password"]);
        Assert.Equal("42", masked["userId"]);
    }

    [Fact]
    public void MaskSecrets_PreservesNonSecretValues()
    {
        var values = new Dictionary<string, string>
        {
            ["firstName"] = "Alice",
            ["lastName"] = "Smith",
            ["age"] = "30"
        };

        var masked = _runner.MaskSecrets(values);

        Assert.Equal("Alice", masked["firstName"]);
        Assert.Equal("Smith", masked["lastName"]);
        Assert.Equal("30", masked["age"]);
    }

    // ─── RunStepAsync Tests ────────────────────────────────────────────────────

    [Fact]
    public async Task RunStepAsync_ResolvesRequestAndExecutes()
    {
        // Setup test data
        var request = new HttpRequestEntry
        {
            Id = "req1",
            Name = "Test Request",
            Method = ApiRequestMethod.Get,
            Url = "https://api.example.com/test"
        };

        var collection = new ApiCollection
        {
            Id = "col1",
            Name = "Test Collection",
            Nodes = new List<ApiCollectionNode>
            {
                new ApiCollectionNode
                {
                    Id = "node1",
                    Type = ApiCollectionNodeType.Request,
                    Name = "Test Request",
                    Request = request
                }
            }
        };

        // Add collection to repository
        await _collectionRepo.AddCollectionAsync("Test Collection");
        var cols = _collectionRepo.Collections.ToList();
        var col = cols[0];
        col.Id = "col1";
        col.Nodes = collection.Nodes;

        var flow = new ApiFlowDefinition
        {
            Id = "flow1",
            Name = "Test Flow",
            Steps = new List<ApiFlowStep>
            {
                new ApiFlowStep
                {
                    Id = "step1",
                    Order = 0,
                    IsEnabled = true,
                    RequestReference = new ApiRequestReference
                    {
                        SourceKind = ApiRequestReferenceKind.LocalCollection,
                        SourceId = "col1",
                        RequestId = "req1",
                        RequestName = "Test Request",
                        SourceName = "Test Collection"
                    }
                }
            }
        };

        var requestResult = new HttpRequestResult
        {
            StatusCode = 200,
            StatusText = "OK",
            ResponseBody = "{\"token\":\"abc123\"}",
            Headers = new List<KeyValuePair<string>>(),
            Elapsed = TimeSpan.FromMilliseconds(100)
        };

        _requestExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequestEntry>(), It.IsAny<ApiCollection>(), It.IsAny<ApiEnvironment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(requestResult);

        _substitutionMock
            .Setup(x => x.Substitute(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string?>>()))
            .Returns<string, IReadOnlyDictionary<string, string?>>((input, _) => input);

        var stepResult = await _runner.RunStepAsync(flow, flow.Steps[0], new Dictionary<string, string>(), CancellationToken.None);

        Assert.Equal(ApiFlowStepState.Completed, stepResult.State);
        Assert.Equal(200, stepResult.StatusCode);
        Assert.Equal("OK", stepResult.StatusText);
        Assert.Equal(100, stepResult.ElapsedMilliseconds);
    }

    [Fact]
    public async Task RunStepAsync_FailsWhenRequestNotFound()
    {
        var flow = new ApiFlowDefinition
        {
            Id = "flow1",
            Name = "Test Flow",
            Steps = new List<ApiFlowStep>
            {
                new ApiFlowStep
                {
                    Id = "step1",
                    Order = 0,
                    IsEnabled = true,
                    RequestReference = new ApiRequestReference
                    {
                        SourceKind = ApiRequestReferenceKind.LocalCollection,
                        SourceId = "col1",
                        RequestId = "non-existent",
                        RequestName = "Non-existent Request"
                    }
                }
            }
        };

        var stepResult = await _runner.RunStepAsync(flow, flow.Steps[0], new Dictionary<string, string>(), CancellationToken.None);

        Assert.Equal(ApiFlowStepState.Failed, stepResult.State);
        Assert.Contains("Request not found", stepResult.ErrorMessage);
    }

    [Fact]
    public async Task RunStepAsync_HandlesRequestError()
    {
        var request = new HttpRequestEntry
        {
            Id = "req1",
            Name = "Test Request",
            Method = ApiRequestMethod.Get,
            Url = "https://api.example.com/test"
        };

        var collection = new ApiCollection
        {
            Id = "col1",
            Name = "Test Collection",
            Nodes = new List<ApiCollectionNode>
            {
                new ApiCollectionNode
                {
                    Id = "node1",
                    Type = ApiCollectionNodeType.Request,
                    Name = "Test Request",
                    Request = request
                }
            }
        };

        await _collectionRepo.AddCollectionAsync("Test Collection");
        var cols = _collectionRepo.Collections.ToList();
        var col = cols[0];
        col.Id = "col1";
        col.Nodes = collection.Nodes;

        var flow = new ApiFlowDefinition
        {
            Id = "flow1",
            Name = "Test Flow",
            Steps = new List<ApiFlowStep>
            {
                new ApiFlowStep
                {
                    Id = "step1",
                    Order = 0,
                    IsEnabled = true,
                    RequestReference = new ApiRequestReference
                    {
                        SourceKind = ApiRequestReferenceKind.LocalCollection,
                        SourceId = "col1",
                        RequestId = "req1",
                        RequestName = "Test Request",
                        SourceName = "Test Collection"
                    }
                }
            }
        };

        var requestResult = new HttpRequestResult
        {
            StatusCode = 404,
            StatusText = "Not Found",
            ErrorMessage = "Resource not found",
            Elapsed = TimeSpan.FromMilliseconds(50)
        };

        _requestExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequestEntry>(), It.IsAny<ApiCollection>(), It.IsAny<ApiEnvironment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(requestResult);

        _substitutionMock
            .Setup(x => x.Substitute(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string?>>()))
            .Returns<string, IReadOnlyDictionary<string, string?>>((input, _) => input);

        var stepResult = await _runner.RunStepAsync(flow, flow.Steps[0], new Dictionary<string, string>(), CancellationToken.None);

        Assert.Equal(ApiFlowStepState.Failed, stepResult.State);
        Assert.Equal(404, stepResult.StatusCode);
        Assert.Equal("Resource not found", stepResult.ErrorMessage);
    }

    // ─── ExtractCapturesAsync Tests ────────────────────────────────────────────

    [Fact]
    public async Task ExtractCapturesAsync_ExtractsJsonPathValue()
    {
        var step = new ApiFlowStep
        {
            Id = "step1",
            CaptureMappings = new List<ApiFlowCaptureMapping>
            {
                new ApiFlowCaptureMapping
                {
                    Id = "cap1",
                    Source = ApiFlowCaptureSource.BodyJsonPath,
                    JsonPath = "$.token",
                    TargetVariable = "authToken",
                    IsEnabled = true
                }
            }
        };

        var requestResult = new HttpRequestResult
        {
            ResponseBody = "{\"token\":\"abc123\",\"user\":\"john\"}",
            StatusCode = 200
        };

        var captures = await _runner.ExtractCapturesAsync(step, requestResult);

        Assert.Single(captures);
        Assert.Equal("authToken", captures.Keys.First());
        Assert.Equal("abc123", captures.Values.First());
    }

    [Fact]
    public async Task ExtractCapturesAsync_ExtractsHeaderValue()
    {
        var step = new ApiFlowStep
        {
            Id = "step1",
            CaptureMappings = new List<ApiFlowCaptureMapping>
            {
                new ApiFlowCaptureMapping
                {
                    Id = "cap1",
                    Source = ApiFlowCaptureSource.ResponseHeader,
                    HeaderName = "X-Correlation-ID",
                    TargetVariable = "correlationId",
                    IsEnabled = true
                }
            }
        };

        var requestResult = new HttpRequestResult
        {
            Headers = new List<KeyValuePair<string>>
            {
                new KeyValuePair<string> { Key = "X-Correlation-ID", Value = "corr-123", IsEnabled = true }
            },
            StatusCode = 200
        };

        var captures = await _runner.ExtractCapturesAsync(step, requestResult);

        Assert.Single(captures);
        Assert.Equal("correlationId", captures.Keys.First());
        Assert.Equal("corr-123", captures.Values.First());
    }

    [Fact]
    public async Task ExtractCapturesAsync_ExtractsStatusCode()
    {
        var step = new ApiFlowStep
        {
            Id = "step1",
            CaptureMappings = new List<ApiFlowCaptureMapping>
            {
                new ApiFlowCaptureMapping
                {
                    Id = "cap1",
                    Source = ApiFlowCaptureSource.StatusCode,
                    TargetVariable = "status",
                    IsEnabled = true
                }
            }
        };

        var requestResult = new HttpRequestResult
        {
            StatusCode = 201
        };

        var captures = await _runner.ExtractCapturesAsync(step, requestResult);

        Assert.Single(captures);
        Assert.Equal("status", captures.Keys.First());
        Assert.Equal("201", captures.Values.First());
    }

    [Fact]
    public async Task ExtractCapturesAsync_UsesDefaultValueWhenCaptureFails()
    {
        var step = new ApiFlowStep
        {
            Id = "step1",
            CaptureMappings = new List<ApiFlowCaptureMapping>
            {
                new ApiFlowCaptureMapping
                {
                    Id = "cap1",
                    Source = ApiFlowCaptureSource.BodyJsonPath,
                    JsonPath = "$.non.existent",
                    TargetVariable = "missingValue",
                    DefaultValue = "default-value",
                    IsEnabled = true
                }
            }
        };

        var requestResult = new HttpRequestResult
        {
            ResponseBody = "{\"other\":\"value\"}",
            StatusCode = 200
        };

        var captures = await _runner.ExtractCapturesAsync(step, requestResult);

        Assert.Single(captures);
        Assert.Equal("missingValue", captures.Keys.First());
        Assert.Equal("default-value", captures.Values.First());
    }

    // ─── RunFlowAsync Tests ───────────────────────────────────────────────────

    [Fact]
    public async Task RunFlowAsync_ExecutesAllStepsInOrder()
    {
        var request = new HttpRequestEntry
        {
            Id = "req1",
            Name = "Test Request",
            Method = ApiRequestMethod.Get,
            Url = "https://api.example.com/test"
        };

        var collection = new ApiCollection
        {
            Id = "col1",
            Name = "Test Collection",
            Nodes = new List<ApiCollectionNode>
            {
                new ApiCollectionNode
                {
                    Id = "node1",
                    Type = ApiCollectionNodeType.Request,
                    Name = "Test Request",
                    Request = request
                }
            }
        };

        await _collectionRepo.AddCollectionAsync("Test Collection");
        var cols = _collectionRepo.Collections.ToList();
        var col = cols[0];
        col.Id = "col1";
        col.Nodes = collection.Nodes;

        var flow = new ApiFlowDefinition
        {
            Id = "flow1",
            Name = "Test Flow",
            FailurePolicy = ApiFlowFailurePolicy.StopOnFailure,
            Steps = new List<ApiFlowStep>
            {
                new ApiFlowStep { Id = "step1", Order = 0, IsEnabled = true, RequestReference = new ApiRequestReference { SourceKind = ApiRequestReferenceKind.LocalCollection, SourceId = "col1", RequestId = "req1" } },
                new ApiFlowStep { Id = "step2", Order = 1, IsEnabled = true, RequestReference = new ApiRequestReference { SourceKind = ApiRequestReferenceKind.LocalCollection, SourceId = "col1", RequestId = "req1" } },
                new ApiFlowStep { Id = "step3", Order = 2, IsEnabled = true, RequestReference = new ApiRequestReference { SourceKind = ApiRequestReferenceKind.LocalCollection, SourceId = "col1", RequestId = "req1" } }
            }
        };

        var requestResult = new HttpRequestResult
        {
            StatusCode = 200,
            StatusText = "OK",
            Elapsed = TimeSpan.FromMilliseconds(10)
        };

        _requestExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequestEntry>(), It.IsAny<ApiCollection>(), It.IsAny<ApiEnvironment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(requestResult);

        _substitutionMock
            .Setup(x => x.Substitute(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string?>>()))
            .Returns<string, IReadOnlyDictionary<string, string?>>((input, _) => input);

        var flowResult = await _runner.RunFlowAsync(flow, CancellationToken.None);

        Assert.Equal(ApiFlowRunState.Completed, flowResult.State);
        Assert.Equal(3, flowResult.StepResults.Count);
        Assert.Equal("step1", flowResult.StepResults[0].StepId);
        Assert.Equal("step2", flowResult.StepResults[1].StepId);
        Assert.Equal("step3", flowResult.StepResults[2].StepId);
    }

    [Fact]
    public async Task RunFlowAsync_StopsOnFailureWithStopPolicy()
    {
        var request = new HttpRequestEntry
        {
            Id = "req1",
            Name = "Test Request",
            Method = ApiRequestMethod.Get,
            Url = "https://api.example.com/test"
        };

        var collection = new ApiCollection
        {
            Id = "col1",
            Name = "Test Collection",
            Nodes = new List<ApiCollectionNode>
            {
                new ApiCollectionNode
                {
                    Id = "node1",
                    Type = ApiCollectionNodeType.Request,
                    Name = "Test Request",
                    Request = request
                }
            }
        };

        await _collectionRepo.AddCollectionAsync("Test Collection");
        var cols = _collectionRepo.Collections.ToList();
        var col = cols[0];
        col.Id = "col1";
        col.Nodes = collection.Nodes;

        var flow = new ApiFlowDefinition
        {
            Id = "flow1",
            Name = "Test Flow",
            FailurePolicy = ApiFlowFailurePolicy.StopOnFailure,
            Steps = new List<ApiFlowStep>
            {
                new ApiFlowStep { Id = "step1", Order = 0, IsEnabled = true, RequestReference = new ApiRequestReference { SourceKind = ApiRequestReferenceKind.LocalCollection, SourceId = "col1", RequestId = "req1" } },
                new ApiFlowStep { Id = "step2", Order = 1, IsEnabled = true, RequestReference = new ApiRequestReference { SourceKind = ApiRequestReferenceKind.LocalCollection, SourceId = "col1", RequestId = "req1" } },
                new ApiFlowStep { Id = "step3", Order = 2, IsEnabled = true, RequestReference = new ApiRequestReference { SourceKind = ApiRequestReferenceKind.LocalCollection, SourceId = "col1", RequestId = "req1" } }
            }
        };

        var successResult = new HttpRequestResult { StatusCode = 200, Elapsed = TimeSpan.FromMilliseconds(10) };
        var failResult = new HttpRequestResult { StatusCode = 500, ErrorMessage = "Server error", Elapsed = TimeSpan.FromMilliseconds(10) };

        var callCount = 0;
        _requestExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequestEntry>(), It.IsAny<ApiCollection>(), It.IsAny<ApiEnvironment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1 ? successResult : failResult;
            });

        _substitutionMock
            .Setup(x => x.Substitute(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string?>>()))
            .Returns<string, IReadOnlyDictionary<string, string?>>((input, _) => input);

        var flowResult = await _runner.RunFlowAsync(flow, CancellationToken.None);

        Assert.Equal(ApiFlowRunState.Failed, flowResult.State);
        Assert.Equal(3, flowResult.StepResults.Count); // All steps have results (remaining are skipped)
        Assert.Equal(ApiFlowStepState.Completed, flowResult.StepResults[0].State);
        Assert.Equal(ApiFlowStepState.Failed, flowResult.StepResults[1].State);
        Assert.Equal(ApiFlowStepState.Skipped, flowResult.StepResults[2].State);
    }

    [Fact]
    public async Task RunFlowAsync_ContinuesOnFailureWithContinuePolicy()
    {
        var request = new HttpRequestEntry
        {
            Id = "req1",
            Name = "Test Request",
            Method = ApiRequestMethod.Get,
            Url = "https://api.example.com/test"
        };

        var collection = new ApiCollection
        {
            Id = "col1",
            Name = "Test Collection",
            Nodes = new List<ApiCollectionNode>
            {
                new ApiCollectionNode
                {
                    Id = "node1",
                    Type = ApiCollectionNodeType.Request,
                    Name = "Test Request",
                    Request = request
                }
            }
        };

        await _collectionRepo.AddCollectionAsync("Test Collection");
        var cols = _collectionRepo.Collections.ToList();
        var col = cols[0];
        col.Id = "col1";
        col.Nodes = collection.Nodes;

        var flow = new ApiFlowDefinition
        {
            Id = "flow1",
            Name = "Test Flow",
            FailurePolicy = ApiFlowFailurePolicy.ContinueOnFailure,
            Steps = new List<ApiFlowStep>
            {
                new ApiFlowStep { Id = "step1", Order = 0, IsEnabled = true, RequestReference = new ApiRequestReference { SourceKind = ApiRequestReferenceKind.LocalCollection, SourceId = "col1", RequestId = "req1" } },
                new ApiFlowStep { Id = "step2", Order = 1, IsEnabled = true, RequestReference = new ApiRequestReference { SourceKind = ApiRequestReferenceKind.LocalCollection, SourceId = "col1", RequestId = "req1" } },
                new ApiFlowStep { Id = "step3", Order = 2, IsEnabled = true, RequestReference = new ApiRequestReference { SourceKind = ApiRequestReferenceKind.LocalCollection, SourceId = "col1", RequestId = "req1" } }
            }
        };

        var failResult = new HttpRequestResult { StatusCode = 500, ErrorMessage = "Server error", Elapsed = TimeSpan.FromMilliseconds(10) };

        _requestExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequestEntry>(), It.IsAny<ApiCollection>(), It.IsAny<ApiEnvironment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failResult);

        _substitutionMock
            .Setup(x => x.Substitute(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string?>>()))
            .Returns<string, IReadOnlyDictionary<string, string?>>((input, _) => input);

        var flowResult = await _runner.RunFlowAsync(flow, CancellationToken.None);

        Assert.Equal(ApiFlowRunState.CompletedWithFailures, flowResult.State);
        Assert.Equal(3, flowResult.StepResults.Count);
        Assert.All(flowResult.StepResults, sr => Assert.Equal(ApiFlowStepState.Failed, sr.State));
    }

    // ─── Cleanup ──────────────────────────────────────────────────────────────

    public async Task DisposeAsync()
    {
        try
        {
            Directory.Delete(_testRoot, true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
