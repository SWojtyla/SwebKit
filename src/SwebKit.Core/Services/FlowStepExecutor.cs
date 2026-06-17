using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Executes individual flow steps with variable substitution and capture extraction.
/// </summary>
public sealed class FlowStepExecutor
{
    private readonly IHttpRequestExecutor _requestExecutor;
    private readonly IVariableSubstitutionService _substitutionService;
    private readonly FlowReferenceResolver _referenceResolver;
    private readonly FlowVariableScopeBuilder _scopeBuilder;
    private readonly FlowCaptureExtractor _captureExtractor;
    private readonly CollectionRepository _collectionRepository;

    public FlowStepExecutor(
        IHttpRequestExecutor requestExecutor,
        IVariableSubstitutionService substitutionService,
        FlowReferenceResolver referenceResolver,
        FlowVariableScopeBuilder scopeBuilder,
        FlowCaptureExtractor captureExtractor,
        CollectionRepository collectionRepository)
    {
        _requestExecutor = requestExecutor;
        _substitutionService = substitutionService;
        _referenceResolver = referenceResolver;
        _scopeBuilder = scopeBuilder;
        _captureExtractor = captureExtractor;
        _collectionRepository = collectionRepository;
    }

    /// <summary>
    /// Executes a single step in a flow.
    /// </summary>
    public async Task<ApiFlowStepResult> ExecuteAsync(
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
            var resolvedRequest = await _referenceResolver.ResolveRequestAsync(step.RequestReference);
            if (resolvedRequest is null)
            {
                stepResult.State = ApiFlowStepState.Failed;
                stepResult.ErrorMessage = "Request not found: " + step.RequestReference.RequestName;
                stepResult.CompletedAt = DateTimeOffset.UtcNow;
                return stepResult;
            }

            // Resolve environment reference
            var resolvedEnvironment = await _referenceResolver.ResolveEnvironmentAsync(
                step.EnvironmentReference ?? flow.DefaultEnvironmentReference);

            // Find the collection containing the request
            var collection = _collectionRepository.Collections
                .FirstOrDefault(c => c.Nodes.Any(n => n.Request?.Id == resolvedRequest.Id));

            // Build variable scope
            var variableScope = await _scopeBuilder.BuildAsync(
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
                var capturedValues = await _captureExtractor.ExtractAsync(step, requestResult);
                foreach (var kvp in capturedValues)
                {
                    stepResult.CapturedValues[kvp.Key] = kvp.Value;
                    runScopedVariables[kvp.Key] = kvp.Value;
                }
                stepResult.HasSecretCaptures = capturedValues.Any(kvp => 
                    kvp.Key.ToLowerInvariant().Contains("secret") ||
                    kvp.Key.ToLowerInvariant().Contains("token") ||
                    kvp.Key.ToLowerInvariant().Contains("password"));
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
}
