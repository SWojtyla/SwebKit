using System.Diagnostics;
using System.Text.Json;
using Json.Path;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Executes API Client flows: ordered request steps with variable overrides, capture mappings,
/// and user-selected failure policy (stop on failure or continue).
/// </summary>
public sealed class ApiClientFlowRunnerService : IApiFlowRunnerService
{
    private readonly IHttpRequestExecutor _requestExecutor;
    private readonly IVariableSubstitutionService _substitutionService;
    private readonly IApiFlowRepository _flowRepository;
    private readonly CollectionRepository _collectionRepository;
    private readonly EnvironmentRepository _environmentRepository;
    private readonly LinkedCollectionRootRepository _linkedRootRepository;

    private static readonly string[] SecretKeyPatterns = new[]
    {
        "secret", "token", "password", "passwd", "pwd", "key", "auth",
        "credential", "api", "bearer", "access", "refresh", "private"
    };

    public ApiClientFlowRunnerService(
        IHttpRequestExecutor requestExecutor,
        IVariableSubstitutionService substitutionService,
        IApiFlowRepository flowRepository,
        CollectionRepository collectionRepository,
        EnvironmentRepository environmentRepository,
        LinkedCollectionRootRepository linkedRootRepository)
    {
        _requestExecutor = requestExecutor;
        _substitutionService = substitutionService;
        _flowRepository = flowRepository;
        _collectionRepository = collectionRepository;
        _environmentRepository = environmentRepository;
        _linkedRootRepository = linkedRootRepository;
    }

    public async Task<ApiFlowRunResult> RunFlowAsync(
        ApiFlowDefinition flow,
        CancellationToken cancellationToken = default)
    {
        var runResult = new ApiFlowRunResult
        {
            FlowId = flow.Id,
            FlowName = flow.Name,
            StartedAt = DateTimeOffset.UtcNow,
            State = ApiFlowRunState.Running,
            StepResults = new List<ApiFlowStepResult>(),
            AllCapturedValues = new Dictionary<string, string>(),
            Warnings = new List<string>()
        };

        var runScopedVariables = new Dictionary<string, string>();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var orderedSteps = flow.Steps
                .Where(s => s.IsEnabled)
                .OrderBy(s => s.Order)
                .ToList();

            foreach (var step in orderedSteps)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    runResult.State = ApiFlowRunState.Cancelled;
                    break;
                }

                var stepResult = await RunStepAsync(flow, step, runScopedVariables, cancellationToken);
                runResult.StepResults.Add(stepResult);

                foreach (var kvp in stepResult.CapturedValues)
                {
                    runScopedVariables[kvp.Key] = kvp.Value;
                }

                if (stepResult.State == ApiFlowStepState.Failed && 
                    flow.FailurePolicy == ApiFlowFailurePolicy.StopOnFailure)
                {
                    var remainingSteps = orderedSteps.SkipWhile(s => s.Id != step.Id).Skip(1);
                    foreach (var remainingStep in remainingSteps)
                    {
                        runResult.StepResults.Add(new ApiFlowStepResult
                        {
                            StepId = remainingStep.Id,
                            StepOrder = remainingStep.Order,
                            State = ApiFlowStepState.Skipped,
                            ErrorMessage = "Skipped due to StopOnFailure policy",
                        });
                    }
                    runResult.State = ApiFlowRunState.Failed;
                    break;
                }
            }

            stopwatch.Stop();
            runResult.TotalElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            runResult.CompletedAt = DateTimeOffset.UtcNow;
            runResult.AllCapturedValues = MaskSecrets(runScopedVariables);

            if (runResult.State == ApiFlowRunState.Running)
            {
                if (runResult.StepResults.Any(s => s.State == ApiFlowStepState.Failed))
                {
                    runResult.State = ApiFlowRunState.CompletedWithFailures;
                }
                else if (runResult.StepResults.All(s => 
                    s.State == ApiFlowStepState.Completed || s.State == ApiFlowStepState.Skipped))
                {
                    runResult.State = ApiFlowRunState.Completed;
                }
            }
        }
        catch (OperationCanceledException)
        {
            runResult.State = ApiFlowRunState.Cancelled;
            runResult.CompletedAt = DateTimeOffset.UtcNow;
            stopwatch.Stop();
            runResult.TotalElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            runResult.State = ApiFlowRunState.Failed;
            runResult.CompletedAt = DateTimeOffset.UtcNow;
            stopwatch.Stop();
            runResult.TotalElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            runResult.Warnings.Add(ex.Message);
        }

        return runResult;
    }

    public async Task<ApiFlowStepResult> RunStepAsync(
        ApiFlowDefinition flow,
        ApiFlowStep step,
        Dictionary<string, string> runScopedVariables,
        CancellationToken cancellationToken = default)
    {
        var stepResult = new ApiFlowStepResult
        {
            StepId = step.Id,
            StepOrder = step.Order,
            State = ApiFlowStepState.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CapturedValues = new Dictionary<string, string>(),
            Warnings = new List<string>()
        };

        try
        {
            // Resolve request reference
            var resolvedRequest = await ResolveRequestAsync(step.RequestReference);
            if (resolvedRequest is null)
            {
                stepResult.State = ApiFlowStepState.Failed;
                stepResult.ErrorMessage = "Request not found: " + step.RequestReference.RequestName;
                stepResult.CompletedAt = DateTimeOffset.UtcNow;
                return stepResult;
            }

            // Resolve environment reference
            var resolvedEnvironment = await ResolveEnvironmentAsync(
                step.EnvironmentReference ?? flow.DefaultEnvironmentReference);

            // Find the collection containing the request
            var collection = _collectionRepository.Collections
                .FirstOrDefault(c => c.Nodes.Any(n => n.Request?.Id == resolvedRequest.Id));

            // Build variable scope
            var variableScope = await BuildVariableScopeAsync(
                flow, step, resolvedRequest, resolvedEnvironment, runScopedVariables);

            // Apply variable substitution to the request
            var substitutedRequest = SubstituteRequest(resolvedRequest, variableScope);

            // Execute the request
            var requestResult = await _requestExecutor.ExecuteAsync(
                substitutedRequest, collection, resolvedEnvironment, cancellationToken);

            stepResult.StatusCode = requestResult.StatusCode;
            stepResult.StatusText = requestResult.StatusText;
            stepResult.ElapsedMilliseconds = (long)requestResult.Elapsed.TotalMilliseconds;

            if (!string.IsNullOrEmpty(requestResult.ErrorMessage))
            {
                stepResult.State = ApiFlowStepState.Failed;
                stepResult.ErrorMessage = requestResult.ErrorMessage;
            }
            else
            {
                stepResult.State = ApiFlowStepState.Completed;

                // Extract captures
                var capturedValues = await ExtractCapturesAsync(step, requestResult);
                foreach (var kvp in capturedValues)
                {
                    stepResult.CapturedValues[kvp.Key] = kvp.Value;
                    runScopedVariables[kvp.Key] = kvp.Value;
                }
                stepResult.HasSecretCaptures = capturedValues.Any(kvp => IsSecretLookingKey(kvp.Key));
            }
        }
        catch (OperationCanceledException)
        {
            stepResult.State = ApiFlowStepState.Cancelled;
        }
        catch (Exception ex)
        {
            stepResult.State = ApiFlowStepState.Failed;
            stepResult.ErrorMessage = ex.Message;
        }

        stepResult.CompletedAt = DateTimeOffset.UtcNow;
        return stepResult;
    }

    public async Task<HttpRequestEntry?> ResolveRequestAsync(ApiRequestReference reference)
    {
        if (reference.SourceKind == ApiRequestReferenceKind.LocalCollection)
        {
            var (collection, request) = _collectionRepository.FindRequest(reference.RequestId);
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

            var envDir = Path.Combine(root.LocalPath, ".swebkit-api", "environments");
            if (!Directory.Exists(envDir)) return null;

            foreach (var file in Directory.GetFiles(envDir, "*.swebenv.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var env = JsonSerializer.Deserialize<ApiEnvironment>(json);
                    if (env?.Id == reference.EnvironmentId) return env;
                }
                catch { }
            }
        }

        return null;
    }

    public async Task<Dictionary<string, string>> BuildVariableScopeAsync(
        ApiFlowDefinition flow,
        ApiFlowStep step,
        HttpRequestEntry? resolvedRequest,
        ApiEnvironment? resolvedEnvironment,
        Dictionary<string, string> runScopedVariables)
    {
        var scope = new Dictionary<string, string>();

        // Start with collection variables (from the collection containing the request)
        if (resolvedRequest is not null)
        {
            var collection = _collectionRepository.Collections
                .FirstOrDefault(c => c.Nodes.Any(n => n.Request?.Id == resolvedRequest.Id));
            if (collection is not null)
            {
                foreach (var var in collection.Variables.Where(v => v.IsEnabled))
                {
                    if (var.Generator is not null)
                    {
                        // Generated variables are resolved by the substitution service
                        scope[var.Key] = var.Value ?? string.Empty;
                    }
                    else
                    {
                        scope[var.Key] = var.Value ?? string.Empty;
                    }
                }
            }
        }

        // Add environment variables
        if (resolvedEnvironment is not null)
        {
            foreach (var var in resolvedEnvironment.Variables.Where(v => v.IsEnabled))
            {
                // Skip secret sources that require async resolution
                if (var.SecretSource == EnvironmentVariableSecretSource.Plain)
                {
                    scope[var.Key] = var.Value ?? string.Empty;
                }
            }
        }

        // Add flow-level variable overrides
        foreach (var override in flow.VariableOverrides.Where(o => o.IsEnabled))
        {
            scope[override.Key] = override.Value;
        }

        // Add step-level variable overrides
        foreach (var override in step.VariableOverrides.Where(o => o.IsEnabled))
        {
            scope[override.Key] = override.Value;
        }

        // Add run-scoped variables (from previous captures)
        foreach (var kvp in runScopedVariables)
        {
            scope[kvp.Key] = kvp.Value;
        }

        // Apply substitution to all values (so {{variable}} references are resolved)
        var finalScope = new Dictionary<string, string>();
        foreach (var kvp in scope)
        {
            finalScope[kvp.Key] = _substitutionService.Substitute(kvp.Value, scope);
        }

        return finalScope;
    }

    public async Task<Dictionary<string, string>> ExtractCapturesAsync(
        ApiFlowStep step,
        HttpRequestResult requestResult)
    {
        var captures = new Dictionary<string, string>();

        foreach (var mapping in step.CaptureMappings.Where(m => m.IsEnabled))
        {
            try
            {
                string? value = null;

                switch (mapping.Source)
                {
                    case ApiFlowCaptureSource.BodyJsonPath:
                        if (!string.IsNullOrEmpty(requestResult.ResponseBody) && 
                            !string.IsNullOrEmpty(mapping.JsonPath))
                        {
                            var jsonDoc = JsonDocument.Parse(requestResult.ResponseBody);
                            var result = jsonDoc.RootElement.Select(mapping.JsonPath);
                            value = result.FirstOrDefault()?.GetRawText();
                        }
                        break;

                    case ApiFlowCaptureSource.BodyJsonPathArray:
                        if (!string.IsNullOrEmpty(requestResult.ResponseBody) && 
                            !string.IsNullOrEmpty(mapping.JsonPath))
                        {
                            var jsonDoc = JsonDocument.Parse(requestResult.ResponseBody);
                            var result = jsonDoc.RootElement.Select(mapping.JsonPath);
                            value = string.Join(",", result.Select(r => r.GetRawText()));
                        }
                        break;

                    case ApiFlowCaptureSource.ResponseHeader:
                        if (!string.IsNullOrEmpty(mapping.HeaderName))
                        {
                            var header = requestResult.Headers.FirstOrDefault(h =>
                                string.Equals(h.Key, mapping.HeaderName, StringComparison.OrdinalIgnoreCase));
                            value = header.Value;
                        }
                        break;

                    case ApiFlowCaptureSource.StatusCode:
                        value = requestResult.StatusCode?.ToString();
                        break;

                    case ApiFlowCaptureSource.ResponseBody:
                        value = requestResult.ResponseBody;
                        break;
                }

                if (value is not null)
                {
                    captures[mapping.TargetVariable] = value;
                }
                else if (mapping.DefaultValue is not null)
                {
                    captures[mapping.TargetVariable] = mapping.DefaultValue;
                }
            }
            catch
            {
                // Capture failed - skip
            }
        }

        return captures;
    }

    public bool IsSecretLookingKey(string key)
    {
        var lowerKey = key.ToLowerInvariant();
        return SecretKeyPatterns.Any(pattern => lowerKey.Contains(pattern));
    }

    public Dictionary<string, string> MaskSecrets(Dictionary<string, string> values)
    {
        var masked = new Dictionary<string, string>();
        foreach (var kvp in values)
        {
            if (IsSecretLookingKey(kvp.Key))
            {
                masked[kvp.Key] = "***MASKED***";
            }
            else
            {
                masked[kvp.Key] = kvp.Value;
            }
        }
        return masked;
    }

    // ─── Helper Methods ───────────────────────────────────────────────────────

    private HttpRequestEntry SubstituteRequest(
        HttpRequestEntry request,
        Dictionary<string, string> variableScope)
    {
        var substituted = new HttpRequestEntry
        {
            Id = request.Id,
            Name = request.Name,
            Method = request.Method,
            Url = _substitutionService.Substitute(request.Url, variableScope),
            Headers = new List<KeyValuePair<string>>(),
            QueryParams = new List<KeyValuePair<string>>(),
            Body = request.Body,
            Auth = request.Auth,
            CaptureRules = request.CaptureRules,
            GraphQlQuery = request.GraphQlQuery,
            GraphQlVariables = request.GraphQlVariables,
            GraphQlSelectedOperation = request.GraphQlSelectedOperation,
            SavedMessages = request.SavedMessages,
            WsSubProtocol = request.WsSubProtocol,
            ResponseExamples = request.ResponseExamples,
            CreatedAt = request.CreatedAt,
            UpdatedAt = request.UpdatedAt
        };

        // Substitute headers
        foreach (var header in request.Headers.Where(h => h.IsEnabled))
        {
            substituted.Headers.Add(new KeyValuePair<string>
            {
                Key = _substitutionService.Substitute(header.Key, variableScope),
                Value = _substitutionService.Substitute(header.Value ?? string.Empty, variableScope),
                IsEnabled = header.IsEnabled
            });
        }

        // Substitute query params
        foreach (var param in request.QueryParams.Where(p => p.IsEnabled))
        {
            substituted.QueryParams.Add(new KeyValuePair<string>
            {
                Key = _substitutionService.Substitute(param.Key, variableScope),
                Value = _substitutionService.Substitute(param.Value ?? string.Empty, variableScope),
                IsEnabled = param.IsEnabled
            });
        }

        // Substitute body
        if (request.Body is not null)
        {
            substituted.Body = new RequestBody
            {
                Mode = request.Body.Mode,
                RawContent = request.Body.RawContent is not null 
                    ? _substitutionService.Substitute(request.Body.RawContent, variableScope)
                    : null,
                ContentType = request.Body.ContentType,
                FormData = request.Body.FormData,
                FilePath = request.Body.FilePath
            };
        }

        return substituted;
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
