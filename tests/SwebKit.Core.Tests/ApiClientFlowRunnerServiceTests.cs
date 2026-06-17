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
    private readonly FlowReferenceResolver _referenceResolver;
    private readonly FlowVariableScopeBuilder _scopeBuilder;
    private readonly FlowCaptureExtractor _captureExtractor;
    private readonly FlowSecretsMasker _secretsMasker;
    private readonly FlowStepExecutor _stepExecutor;
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
        
        _referenceResolver = new FlowReferenceResolver(
            _collectionRepo, _envRepo, _linkedRootRepo);
        _scopeBuilder = new FlowVariableScopeBuilder(
            _collectionRepo, _substitutionMock.Object);
        _captureExtractor = new FlowCaptureExtractor();
        _secretsMasker = new FlowSecretsMasker();
        _stepExecutor = new FlowStepExecutor(
            _requestExecutorMock.Object,
            _substitutionMock.Object,
            _referenceResolver,
            _scopeBuilder,
            _captureExtractor,
            _collectionRepo);
        
        _runner = new ApiClientFlowRunnerService(
            _stepExecutor,
            _secretsMasker);
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

    [Fact]
    public void MaskSecrets_ReturnsNewDictionary()
    {
        var values = new Dictionary<string, string> { ["token"] = "abc123" };
        var masked = _runner.MaskSecrets(values);

        Assert.NotSame(values, masked);
    }

    // ─── RunFlowAsync Tests ────────────────────────────────────────────────────

    [Fact]
    public async Task RunFlowAsync_EmptyFlow_ReturnsCompletedResult()
    {
        var flow = new ApiFlowDefinition
        {
            Id = "test-flow",
            Name = "Test Flow",
            Steps = new List<ApiFlowStep>(),
            FailurePolicy = ApiFlowFailurePolicy.StopOnFailure
        };

        var result = await _runner.RunFlowAsync(flow);

        Assert.Equal("test-flow", result.FlowId);
        Assert.Equal("Test Flow", result.FlowName);
        Assert.Equal(ApiFlowRunState.Completed, result.State);
        Assert.Empty(result.StepResults);
    }

    [Fact]
    public async Task RunFlowAsync_WithDisabledSteps_SkipsDisabledSteps()
    {
        var flow = new ApiFlowDefinition
        {
            Id = "test-flow",
            Name = "Test Flow",
            Steps = new List<ApiFlowStep>
            {
                new ApiFlowStep { Id = "step1", Order = 1, IsEnabled = true },
                new ApiFlowStep { Id = "step2", Order = 2, IsEnabled = false },
                new ApiFlowStep { Id = "step3", Order = 3, IsEnabled = true }
            },
            FailurePolicy = ApiFlowFailurePolicy.StopOnFailure
        };

        // Setup mock to return a successful result
        _requestExecutorMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequestEntry>(), It.IsAny<ApiCollection>(), It.IsAny<ApiEnvironment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpRequestResult { StatusCode = "200", StatusText = "OK" });

        var result = await _runner.RunFlowAsync(flow);

        Assert.Equal(ApiFlowRunState.Completed, result.State);
        Assert.Equal(2, result.StepResults.Count); // Only enabled steps
    }

    [Fact]
    public async Task RunFlowAsync_StopOnFailure_StopsOnFirstFailure()
    {
        var flow = new ApiFlowDefinition
        {
            Id = "test-flow",
            Name = "Test Flow",
            Steps = new List<ApiFlowStep>
            {
                new ApiFlowStep { Id = "step1", Order = 1, IsEnabled = true },
                new ApiFlowStep { Id = "step2", Order = 2, IsEnabled = true },
                new ApiFlowStep { Id = "step3", Order = 3, IsEnabled = true }
            },
            FailurePolicy = ApiFlowFailurePolicy.StopOnFailure
        };

        // Setup mock to return failure for first step
        _requestExecutorMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequestEntry>(), It.IsAny<ApiCollection>(), It.IsAny<ApiEnvironment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpRequestResult { StatusCode = "500", ErrorMessage = "Server error" });

        var result = await _runner.RunFlowAsync(flow);

        Assert.Equal(ApiFlowRunState.Failed, result.State);
        Assert.Equal(3, result.StepResults.Count); // All 3 steps (2 skipped)
        Assert.Equal(ApiFlowStepState.Failed, result.StepResults[0].State);
        Assert.Equal(ApiFlowStepState.Skipped, result.StepResults[1].State);
        Assert.Equal(ApiFlowStepState.Skipped, result.StepResults[2].State);
    }

    [Fact]
    public async Task RunFlowAsync_ContinueOnFailure_ContinuesAfterFailure()
    {
        var flow = new ApiFlowDefinition
        {
            Id = "test-flow",
            Name = "Test Flow",
            Steps = new List<ApiFlowStep>
            {
                new ApiFlowStep { Id = "step1", Order = 1, IsEnabled = true },
                new ApiFlowStep { Id = "step2", Order = 2, IsEnabled = true },
                new ApiFlowStep { Id = "step3", Order = 3, IsEnabled = true }
            },
            FailurePolicy = ApiFlowFailurePolicy.ContinueOnFailure
        };

        // Setup mock to return failure for first step, success for others
        _requestExecutorMock.SetupSequence(x => x.ExecuteAsync(It.IsAny<HttpRequestEntry>(), It.IsAny<ApiCollection>(), It.IsAny<ApiEnvironment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpRequestResult { StatusCode = "500", ErrorMessage = "Server error" })
            .ReturnsAsync(new HttpRequestResult { StatusCode = "200", StatusText = "OK" })
            .ReturnsAsync(new HttpRequestResult { StatusCode = "200", StatusText = "OK" });

        var result = await _runner.RunFlowAsync(flow);

        Assert.Equal(ApiFlowRunState.CompletedWithFailures, result.State);
        Assert.Equal(3, result.StepResults.Count);
        Assert.Equal(ApiFlowStepState.Failed, result.StepResults[0].State);
        Assert.Equal(ApiFlowStepState.Completed, result.StepResults[1].State);
        Assert.Equal(ApiFlowStepState.Completed, result.StepResults[2].State);
    }

    [Fact]
    public async Task RunFlowAsync_CancellationRequested_StopsExecution()
    {
        var flow = new ApiFlowDefinition
        {
            Id = "test-flow",
            Name = "Test Flow",
            Steps = new List<ApiFlowStep>
            {
                new ApiFlowStep { Id = "step1", Order = 1, IsEnabled = true },
                new ApiFlowStep { Id = "step2", Order = 2, IsEnabled = true }
            },
            FailurePolicy = ApiFlowFailurePolicy.StopOnFailure
        };

        var cts = new CancellationTokenSource();
        cts.CancelAfter(10); // Cancel quickly

        var result = await _runner.RunFlowAsync(flow, cts.Token);

        Assert.Equal(ApiFlowRunState.Cancelled, result.State);
    }

    // ─── RunStepAsync Tests ────────────────────────────────────────────────────

    [Fact]
    public async Task RunStepAsync_ExecutesRequestAndReturnsResult()
    {
        var flow = new ApiFlowDefinition { Id = "test-flow", Name = "Test Flow" };
        var step = new ApiFlowStep
        {
            Id = "step1",
            Order = 1,
            IsEnabled = true,
            RequestReference = new ApiRequestReference
            {
                SourceKind = ApiRequestReferenceKind.LocalCollection,
                RequestId = "req1",
                RequestName = "Test Request"
            }
        };

        // Setup mock
        _requestExecutorMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequestEntry>(), It.IsAny<ApiCollection>(), It.IsAny<ApiEnvironment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpRequestResult { StatusCode = "200", StatusText = "OK" });

        var result = await _runner.RunStepAsync(flow, step, new Dictionary<string, string>());

        Assert.Equal("step1", result.StepId);
        Assert.Equal(1, result.StepOrder);
        Assert.Equal(ApiFlowStepState.Completed, result.State);
        Assert.Equal("200", result.StatusCode);
    }

    // ─── FlowSecretsMasker Tests ──────────────────────────────────────────────

    [Fact]
    public void SecretsMasker_IsSecretLookingKey_ReturnsTrueForSecretPatterns()
    {
        var masker = new FlowSecretsMasker();
        
        Assert.True(masker.IsSecretLookingKey("token"));
        Assert.True(masker.IsSecretLookingKey("apiKey"));
        Assert.True(masker.IsSecretLookingKey("password"));
        Assert.True(masker.IsSecretLookingKey("AUTH_TOKEN"));
    }

    [Fact]
    public void SecretsMasker_IsSecretLookingKey_ReturnsFalseForNonSecretPatterns()
    {
        var masker = new FlowSecretsMasker();
        
        Assert.False(masker.IsSecretLookingKey("name"));
        Assert.False(masker.IsSecretLookingKey("userId"));
    }

    [Fact]
    public void SecretsMasker_MaskSecrets_MasksAllSecretKeys()
    {
        var masker = new FlowSecretsMasker();
        var values = new Dictionary<string, string>
        {
            ["token"] = "abc123",
            ["password"] = "secret",
            ["name"] = "John"
        };

        var masked = masker.MaskSecrets(values);

        Assert.Equal("***MASKED***", masked["token"]);
        Assert.Equal("***MASKED***", masked["password"]);
        Assert.Equal("John", masked["name"]);
    }

    // ─── FlowCaptureExtractor Tests ────────────────────────────────────────────

    [Fact]
    public async Task CaptureExtractor_ExtractsJsonPathValue()
    {
        var extractor = new FlowCaptureExtractor();
        var step = new ApiFlowStep
        {
            CaptureMappings = new List<ApiFlowCaptureMapping>
            {
                new ApiFlowCaptureMapping
                {
                    IsEnabled = true,
                    Source = ApiFlowCaptureSource.BodyJsonPath,
                    JsonPath = "$.token",
                    TargetVariable = "authToken"
                }
            }
        };

        var requestResult = new HttpRequestResult
        {
            ResponseBody = "{\"token\":\"abc123\",\"name\":\"test\"}"
        };

        var captures = await extractor.ExtractAsync(step, requestResult);

        Assert.Single(captures);
        Assert.Equal("authToken", captures.Keys.First());
        Assert.Equal("abc123", captures["authToken"]);
    }

    [Fact]
    public async Task CaptureExtractor_ExtractsHeaderValue()
    {
        var extractor = new FlowCaptureExtractor();
        var step = new ApiFlowStep
        {
            CaptureMappings = new List<ApiFlowCaptureMapping>
            {
                new ApiFlowCaptureMapping
                {
                    IsEnabled = true,
                    Source = ApiFlowCaptureSource.ResponseHeader,
                    HeaderName = "X-Custom-Header",
                    TargetVariable = "customHeader"
                }
            }
        };

        var requestResult = new HttpRequestResult
        {
            Headers = new List<KeyValuePair<string, string>>
            {
                new("X-Custom-Header", "header-value")
            }
        };

        var captures = await extractor.ExtractAsync(step, requestResult);

        Assert.Single(captures);
        Assert.Equal("customHeader", captures.Keys.First());
        Assert.Equal("header-value", captures["customHeader"]);
    }

    [Fact]
    public async Task CaptureExtractor_ExtractsStatusCode()
    {
        var extractor = new FlowCaptureExtractor();
        var step = new ApiFlowStep
        {
            CaptureMappings = new List<ApiFlowCaptureMapping>
            {
                new ApiFlowCaptureMapping
                {
                    IsEnabled = true,
                    Source = ApiFlowCaptureSource.StatusCode,
                    TargetVariable = "status"
                }
            }
        };

        var requestResult = new HttpRequestResult
        {
            StatusCode = "200"
        };

        var captures = await extractor.ExtractAsync(step, requestResult);

        Assert.Single(captures);
        Assert.Equal("status", captures.Keys.First());
        Assert.Equal("200", captures["status"]);
    }

    [Fact]
    public async Task CaptureExtractor_UsesDefaultValueWhenCaptureFails()
    {
        var extractor = new FlowCaptureExtractor();
        var step = new ApiFlowStep
        {
            CaptureMappings = new List<ApiFlowCaptureMapping>
            {
                new ApiFlowCaptureMapping
                {
                    IsEnabled = true,
                    Source = ApiFlowCaptureSource.BodyJsonPath,
                    JsonPath = "$.nonexistent",
                    TargetVariable = "missingValue",
                    DefaultValue = "default-value"
                }
            }
        };

        var requestResult = new HttpRequestResult
        {
            ResponseBody = "{\"other\":\"value\"}"
        };

        var captures = await extractor.ExtractAsync(step, requestResult);

        Assert.Single(captures);
        Assert.Equal("missingValue", captures.Keys.First());
        Assert.Equal("default-value", captures["missingValue"]);
    }

    [Fact]
    public async Task CaptureExtractor_ExtractsResponseBody()
    {
        var extractor = new FlowCaptureExtractor();
        var step = new ApiFlowStep
        {
            CaptureMappings = new List<ApiFlowCaptureMapping>
            {
                new ApiFlowCaptureMapping
                {
                    IsEnabled = true,
                    Source = ApiFlowCaptureSource.ResponseBody,
                    TargetVariable = "responseBody"
                }
            }
        };

        var requestResult = new HttpRequestResult
        {
            ResponseBody = "{\"data\":\"test\"}"
        };

        var captures = await extractor.ExtractAsync(step, requestResult);

        Assert.Single(captures);
        Assert.Equal("responseBody", captures.Keys.First());
        Assert.Equal("{\"data\":\"test\"}", captures["responseBody"]);
    }

    // ─── FlowVariableScopeBuilder Tests ────────────────────────────────────────

    [Fact]
    public async Task ScopeBuilder_BuildsScopeWithFlowOverrides()
    {
        var builder = new FlowVariableScopeBuilder(
            _collectionRepo, _substitutionMock.Object);
        
        var flow = new ApiFlowDefinition
        {
            VariableOverrides = new List<ApiFlowVariableOverride>
            {
                new() { Key = "flowVar", Value = "flowValue", IsEnabled = true }
            }
        };
        var step = new ApiFlowStep
        {
            VariableOverrides = new List<ApiFlowVariableOverride>
            {
                new() { Key = "stepVar", Value = "stepValue", IsEnabled = true }
            }
        };

        var scope = await builder.BuildAsync(flow, step, null, null, new Dictionary<string, string>());

        Assert.Contains("flowVar", scope.Keys);
        Assert.Equal("flowValue", scope["flowVar"]);
        Assert.Contains("stepVar", scope.Keys);
        Assert.Equal("stepValue", scope["stepVar"]);
    }

    [Fact]
    public async Task ScopeBuilder_BuildsScopeWithRunScopedVariables()
    {
        var builder = new FlowVariableScopeBuilder(
            _collectionRepo, _substitutionMock.Object);
        
        var flow = new ApiFlowDefinition();
        var step = new ApiFlowStep();
        var runScopedVars = new Dictionary<string, string> { ["capturedVar"] = "capturedValue" };

        var scope = await builder.BuildAsync(flow, step, null, null, runScopedVars);

        Assert.Contains("capturedVar", scope.Keys);
        Assert.Equal("capturedValue", scope["capturedVar"]);
    }

    // ─── FlowReferenceResolver Tests ────────────────────────────────────────────

    [Fact]
    public async Task ReferenceResolver_ResolveRequestAsync_LocalCollection()
    {
        var resolver = new FlowReferenceResolver(
            _collectionRepo, _envRepo, _linkedRootRepo);
        
        // Add a test request to the collection repo
        var request = new HttpRequestEntry { Id = "req1", Name = "Test Request" };
        _collectionRepo.Collections.Add(new ApiCollection
        {
            Id = "col1",
            Name = "Test Collection",
            Nodes = new List<ApiCollectionNode>
            {
                new() { Id = "node1", Type = ApiCollectionNodeType.Request, Request = request }
            }
        });

        var reference = new ApiRequestReference
        {
            SourceKind = ApiRequestReferenceKind.LocalCollection,
            RequestId = "req1",
            RequestName = "Test Request"
        };

        var resolved = await resolver.ResolveRequestAsync(reference);

        Assert.NotNull(resolved);
        Assert.Equal("req1", resolved.Id);
    }

    [Fact]
    public async Task ReferenceResolver_ResolveRequestAsync_NotFound()
    {
        var resolver = new FlowReferenceResolver(
            _collectionRepo, _envRepo, _linkedRootRepo);
        
        var reference = new ApiRequestReference
        {
            SourceKind = ApiRequestReferenceKind.LocalCollection,
            RequestId = "nonexistent",
            RequestName = "Non Existent"
        };

        var resolved = await resolver.ResolveRequestAsync(reference);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ReferenceResolver_ResolveEnvironmentAsync_Local()
    {
        var resolver = new FlowReferenceResolver(
            _collectionRepo, _envRepo, _linkedRootRepo);
        
        // Add a test environment
        _envRepo.Environments.Add(new ApiEnvironment { Id = "env1", Name = "Test Env" });

        var reference = new ApiEnvironmentReference
        {
            SourceKind = ApiEnvironmentReferenceKind.Local,
            EnvironmentId = "env1"
        };

        var resolved = await resolver.ResolveEnvironmentAsync(reference);

        Assert.NotNull(resolved);
        Assert.Equal("env1", resolved.Id);
    }

    [Fact]
    public async Task ReferenceResolver_ResolveEnvironmentAsync_NotFound()
    {
        var resolver = new FlowReferenceResolver(
            _collectionRepo, _envRepo, _linkedRootRepo);
        
        var reference = new ApiEnvironmentReference
        {
            SourceKind = ApiEnvironmentReferenceKind.Local,
            EnvironmentId = "nonexistent"
        };

        var resolved = await resolver.ResolveEnvironmentAsync(reference);

        Assert.Null(resolved);
    }
}
