using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Serialization;

namespace SwebKit.DevOps;

public class DevOpsClient : IDevOpsClient
{
    private readonly HttpClient _http;
    private readonly ILogger<DevOpsClient> _logger;
    private readonly string _orgUrl;
    private readonly string _patCredentialKey;

    private static readonly JsonSerializerOptions JsonOptions = SwebKitJsonOptions.Default;

    private const string DevAzureHost = "dev.azure.com";
    private const string VisualStudioHostSuffix = ".visualstudio.com";

    public DevOpsClient(IHttpClientFactory httpClientFactory, DevOpsConfig config, ILogger<DevOpsClient> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        _http = httpClientFactory.CreateClient("AzureDevOps");
        _logger = logger;
        _orgUrl = NormalizeOrganizationUrl(config.Organization);
        _patCredentialKey = config.PatCredentialKey;
    }

    private string OrgApi => $"{_orgUrl}/_apis";

    private string ProjectApi(string project) => $"{_orgUrl}/{project}/_apis";

    private static string NormalizeOrganizationUrl(string organizationInput)
    {
        if (string.IsNullOrWhiteSpace(organizationInput))
            throw new InvalidOperationException($"{nameof(DevOpsConfig)}.{nameof(DevOpsConfig.Organization)} is required.");

        var input = organizationInput.Trim();

        var absoluteUri = ParseAbsoluteUri(input);
        if (absoluteUri is not null)
            return NormalizeFromAbsoluteUri(absoluteUri);

        if (input.StartsWith($"{DevAzureHost}/", StringComparison.OrdinalIgnoreCase))
        {
            absoluteUri = ParseAbsoluteUri($"https://{input}");
            if (absoluteUri is not null)
                return NormalizeFromAbsoluteUri(absoluteUri);
        }

        if (input.EndsWith(VisualStudioHostSuffix, StringComparison.OrdinalIgnoreCase)
            || input.Contains($"{VisualStudioHostSuffix}/", StringComparison.OrdinalIgnoreCase))
        {
            absoluteUri = ParseAbsoluteUri($"https://{input.TrimStart('/')}");
            if (absoluteUri is not null)
                return NormalizeFromAbsoluteUri(absoluteUri);
        }

        var organizationSlug = ExtractOrganizationSlug(input);
        return $"https://{DevAzureHost}/{organizationSlug}";
    }

    private static Uri? ParseAbsoluteUri(string input)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out var absoluteUri))
            return null;

        return absoluteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || absoluteUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            ? absoluteUri
            : null;
    }

    private static string NormalizeFromAbsoluteUri(Uri absoluteUri)
    {
        var host = absoluteUri.Host.TrimEnd('.');

        if (host.Equals(DevAzureHost, StringComparison.OrdinalIgnoreCase))
        {
            var organizationSlug = ExtractOrganizationSlug(absoluteUri.AbsolutePath);
            return $"https://{DevAzureHost}/{organizationSlug}";
        }

        if (host.EndsWith(VisualStudioHostSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var organizationSlug = host[..^VisualStudioHostSuffix.Length];
            organizationSlug = ExtractOrganizationSlug(organizationSlug);
            return $"https://{organizationSlug}{VisualStudioHostSuffix}";
        }

        return absoluteUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string ExtractOrganizationSlug(string input)
    {
        var slug = input.Trim().Trim('/');

        if (slug.StartsWith($"{DevAzureHost}/", StringComparison.OrdinalIgnoreCase))
            slug = slug[(DevAzureHost.Length + 1)..];

        if (slug.Contains('/'))
            slug = slug.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];

        if (string.IsNullOrWhiteSpace(slug) || slug.Any(char.IsWhiteSpace))
            throw new InvalidOperationException(
                "Azure DevOps organization is invalid. Use an organization slug or supported URL.");

        return slug;
    }

    // ── Connection ──

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, $"{OrgApi}/projects?api-version=7.1&$top=1");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    // ── Projects ──

    public async Task<List<AdoProject>> GetProjectsAsync(CancellationToken ct = default)
    {
        var response = await GetFromJsonAsync<AdoListResponse<AdoProjectDto>>(
            $"{OrgApi}/projects?api-version=7.1&$top=100", JsonOptions, ct).ConfigureAwait(false);

        return response?.Value.Select(p => new AdoProject(
            p.Id ?? string.Empty,
            p.Name ?? string.Empty,
            null,
            null
        )).ToList() ?? [];
    }

    // ── Pipelines ──

    public async Task<List<AdoPipeline>> GetPipelinesAsync(string project, CancellationToken ct = default)
    {
        var response = await GetFromJsonAsync<AdoListResponse<AdoPipelineDto>>(
            $"{ProjectApi(project)}/pipelines?api-version=7.1", JsonOptions, ct).ConfigureAwait(false);

        return response?.Value.Select(p => new AdoPipeline(
            p.Id,
            p.Name,
            p.Folder ?? string.Empty,
            p.Url ?? string.Empty
        )).ToList() ?? [];
    }

    public async Task<List<AdoPipelineRun>> GetPipelineRunsAsync(
        string project, int pipelineId, int? top = null, CancellationToken ct = default)
    {
        var url = $"{ProjectApi(project)}/pipelines/{pipelineId}/runs?api-version=7.1";
        if (top.HasValue) url += $"&$top={top.Value}";

        var response = await GetFromJsonAsync<AdoListResponse<AdoPipelineRunDto>>(
            url, JsonOptions, ct).ConfigureAwait(false);

        return response?.Value.Select(MapPipelineRun).ToList() ?? [];
    }

    public async Task<AdoPipelineRun> GetPipelineRunAsync(
        string project, int pipelineId, int runId, CancellationToken ct = default)
    {
        var runDto = await GetFromJsonAsync<AdoPipelineRunDto>(
            $"{ProjectApi(project)}/pipelines/{pipelineId}/runs/{runId}?api-version=7.1", JsonOptions, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Pipeline run {runId} not found.");

        var stages = await GetRunStagesAsync(project, runId, ct).ConfigureAwait(false);

        var run = MapPipelineRun(runDto);
        return run with { Stages = stages };
    }

    public async Task<AdoPipelineRun> TriggerPipelineRunAsync(
        string project, int pipelineId, string branch,
        Dictionary<string, string>? templateParameters = null,
        CancellationToken ct = default)
    {
        var body = new AdoPipelineRunTriggerDto(
            Resources: new AdoTriggerResourcesDto(
                Repositories: new Dictionary<string, AdoTriggerRepoDto>
                {
                    ["self"] = new AdoTriggerRepoDto(RefName: $"refs/heads/{branch}")
                }),
            TemplateParameters: templateParameters);

        using var request = CreateRequest(
            HttpMethod.Post,
            $"{ProjectApi(project)}/pipelines/{pipelineId}/runs?api-version=7.1",
            body);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var runDto = await response.Content.ReadFromJsonAsync<AdoPipelineRunDto>(JsonOptions, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Failed to parse pipeline run response.");

        return MapPipelineRun(runDto);
    }

    // ── Approvals ──

    public async Task<List<AdoApproval>> GetPendingApprovalsAsync(string project, CancellationToken ct = default)
    {
        // Try without status filter first — environment check approvals may use different status values
        using var request = CreateRequest(
            HttpMethod.Get,
            $"{ProjectApi(project)}/pipelines/approvals?api-version=7.1-preview.1");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return [];

        var dto = await response.Content.ReadFromJsonAsync<AdoListResponse<AdoApprovalDto>>(JsonOptions, ct).ConfigureAwait(false);

        return dto?.Value?
            .Where(a => a.Status is "pending" or "waiting" or "assigned" or "undefined")
            .Select(a => new AdoApproval(
                Id: a.Id ?? string.Empty,
                Status: a.Status ?? "unknown",
                PipelineId: a.Pipeline?.Id ?? 0,
                PipelineName: a.Pipeline?.Name ?? "Unknown",
                RunId: 0,
                StageName: string.Empty,
                EnvironmentName: null,
                TriggeredBy: a.Steps?.FirstOrDefault()?.AssignedApprover?.DisplayName,
                WebUrl: a.Links?.Web?.Href,
                CreatedOn: a.CreatedOn
            )).ToList() ?? [];
    }

    public async Task<List<WaitingStage>> GetWaitingStagesAsync(string project, int runId, CancellationToken ct = default)
    {
        try
        {
            var timeline = await GetFromJsonAsync<AdoTimelineDto>(
                $"{ProjectApi(project)}/build/builds/{runId}/timeline?api-version=7.1", JsonOptions, ct).ConfigureAwait(false);

            if (timeline?.Records is null) return [];

            var waitingStages = ExtractWaitingStagesFromTimeline(timeline.Records);
            if (waitingStages.Count == 0) return [];

            if (waitingStages.Any(w => w.ApprovalId is null))
                waitingStages = await EnrichWithApprovalsFallbackAsync(project, runId, waitingStages, ct).ConfigureAwait(false);

            return waitingStages;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch waiting stages for run {RunId} in project {Project}", runId, project);
            return [];
        }
    }

    /// <summary>
    /// Identifies stages that are blocked at a checkpoint (waiting for approval) by inspecting
    /// the timeline record tree: Stage → Checkpoint (inProgress) → Checkpoint.Approval.
    /// </summary>
    private static List<WaitingStage> ExtractWaitingStagesFromTimeline(IReadOnlyList<AdoTimelineRecordDto> records)
    {
        var stageRecords = records
            .Where(r => r.Type == "Stage")
            .ToDictionary(r => r.Id ?? "", r => r);

        var inProgressCheckpoints = records
            .Where(r => r.Type == "Checkpoint" && r.State == "inProgress" && r.ParentId is not null)
            .ToList();

        var checkpointIds = inProgressCheckpoints.Select(r => r.Id).ToHashSet();
        var checkpointParentStageIds = inProgressCheckpoints.Select(r => r.ParentId!).ToHashSet();

        var approvalIdByStageId = BuildApprovalIdMap(records, inProgressCheckpoints, checkpointIds);

        return stageRecords
            .Where(kv => checkpointParentStageIds.Contains(kv.Key))
            .Select(kv => new WaitingStage(
                kv.Value.Name ?? "Unknown stage",
                approvalIdByStageId.GetValueOrDefault(kv.Key)))
            .ToList();
    }

    /// <summary>
    /// Builds a map from stage ID to approval record ID by walking
    /// Checkpoint.Approval → Checkpoint → Stage parent relationships.
    /// </summary>
    private static Dictionary<string, string> BuildApprovalIdMap(
        IReadOnlyList<AdoTimelineRecordDto> records,
        List<AdoTimelineRecordDto> inProgressCheckpoints,
        HashSet<string?> checkpointIds)
    {
        var approvalRecords = records
            .Where(r => r.Type == "Checkpoint.Approval"
                && r.ParentId is not null
                && checkpointIds.Contains(r.ParentId))
            .ToList();

        var approvalIdByStageId = new Dictionary<string, string>();
        foreach (var approvalRec in approvalRecords)
        {
            var checkpoint = inProgressCheckpoints.FirstOrDefault(c => c.Id == approvalRec.ParentId);
            if (checkpoint?.ParentId is not null && approvalRec.Id is not null)
                approvalIdByStageId[checkpoint.ParentId] = approvalRec.Id;
        }

        return approvalIdByStageId;
    }

    /// <summary>
    /// Falls back to the approvals API to fill in missing approval IDs for waiting stages.
    /// </summary>
    private async Task<List<WaitingStage>> EnrichWithApprovalsFallbackAsync(
        string project, int runId, List<WaitingStage> waitingStages, CancellationToken ct)
    {
        try
        {
            var approvals = await GetPendingApprovalsAsync(project, ct).ConfigureAwait(false);
            if (approvals.Count == 0) return waitingStages;

            var approvalQueue = new Queue<AdoApproval>(approvals);
            return waitingStages.Select(w =>
            {
                if (w.ApprovalId is not null) return w;
                return approvalQueue.Count > 0
                    ? w with { ApprovalId = approvalQueue.Dequeue().Id }
                    : w;
            }).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch approvals as fallback for run {RunId}", runId);
            return waitingStages;
        }
    }

    public async Task ApproveAsync(string project, string approvalId, string? comment = null, CancellationToken ct = default)
    {
        var body = new[] { new AdoApprovalPatchDto(approvalId, "approved", comment) };
        using var request = CreateRequest(
            HttpMethod.Patch,
            $"{ProjectApi(project)}/pipelines/approvals?api-version=7.1-preview.1",
            body);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task RejectAsync(string project, string approvalId, string? comment = null, CancellationToken ct = default)
    {
        var body = new[] { new AdoApprovalPatchDto(approvalId, "rejected", comment) };
        using var request = CreateRequest(
            HttpMethod.Patch,
            $"{ProjectApi(project)}/pipelines/approvals?api-version=7.1-preview.1",
            body);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    // ── Git ──

    public async Task<List<AdoRepository>> GetRepositoriesAsync(string project, CancellationToken ct = default)
    {
        var response = await GetFromJsonAsync<AdoListResponse<AdoRepositoryDto>>(
            $"{ProjectApi(project)}/git/repositories?api-version=7.1", JsonOptions, ct).ConfigureAwait(false);

        return response?.Value.Select(r => new AdoRepository(
            r.Id ?? string.Empty,
            r.Name ?? string.Empty,
            r.DefaultBranch ?? "refs/heads/main",
            r.WebUrl ?? string.Empty
        )).ToList() ?? [];
    }

    public async Task<List<string>> GetBranchesAsync(string project, string repositoryId, CancellationToken ct = default)
    {
        var response = await GetFromJsonAsync<AdoListResponse<AdoRefDto>>(
            $"{ProjectApi(project)}/git/repositories/{repositoryId}/refs?filter=heads/&api-version=7.1", JsonOptions, ct).ConfigureAwait(false);

        return response?.Value
            .Select(r => (r.Name ?? string.Empty).Replace("refs/heads/", ""))
            .Where(name => !string.IsNullOrEmpty(name))
            .OrderBy(name => name)
            .ToList() ?? [];
    }

    public async Task<List<AdoTag>> GetTagsAsync(string project, string repositoryId, CancellationToken ct = default)
    {
        // ADO lists tags via the refs endpoint with a tags/ filter
        var response = await GetFromJsonAsync<AdoListResponse<AdoRefDto>>(
            $"{ProjectApi(project)}/git/repositories/{repositoryId}/refs?filter=tags/&api-version=7.1", JsonOptions, ct).ConfigureAwait(false);

        return response?.Value.Select(r => new AdoTag(
            (r.Name ?? string.Empty).Replace("refs/tags/", ""),
            r.ObjectId ?? string.Empty,
            null,
            r.Creator?.DisplayName,
            null
        )).OrderByDescending(t => t.Name).ToList() ?? [];
    }

    public async Task<AdoTag> CreateAnnotatedTagAsync(
        string project, string repositoryId, string name, string commitSha, string message,
        CancellationToken ct = default)
    {
        var body = new AdoAnnotatedTagCreateDto(
            Name: name,
            TaggedObject: new AdoTaggedObjectDto(ObjectId: commitSha),
            Message: message);

        using var request = CreateRequest(
            HttpMethod.Post,
            $"{ProjectApi(project)}/git/repositories/{repositoryId}/annotatedtags?api-version=7.1",
            body);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var tagDto = await response.Content.ReadFromJsonAsync<AdoAnnotatedTagDto>(JsonOptions, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Failed to parse tag response.");

        return new AdoTag(
            tagDto.Name ?? name,
            tagDto.TaggedObject?.ObjectId ?? commitSha,
            tagDto.Message,
            tagDto.TaggedBy?.Name,
            tagDto.TaggedBy?.Date);
    }

    public async Task<List<AdoCommit>> GetCommitsAsync(
        string project, string repositoryId, string branch, int top = 20, CancellationToken ct = default)
    {
        var url = $"{ProjectApi(project)}/git/repositories/{repositoryId}/commits"
            + $"?searchCriteria.itemVersion.version={Uri.EscapeDataString(branch)}"
            + $"&searchCriteria.$top={top}"
            + "&api-version=7.1";

        var response = await GetFromJsonAsync<AdoListResponse<AdoCommitDto>>(
            url, JsonOptions, ct).ConfigureAwait(false);

        return response?.Value.Select(c => new AdoCommit(
            c.CommitId ?? string.Empty,
            (c.CommitId ?? string.Empty).Length >= 7
                ? (c.CommitId ?? string.Empty)[..7]
                : c.CommitId ?? string.Empty,
            c.Comment ?? string.Empty,
            c.Author?.Name ?? string.Empty,
            c.Author?.Date ?? DateTimeOffset.MinValue
        )).ToList() ?? [];
    }

    // ── Environment status ──

    public async Task<List<PipelineEnvironmentStatus>> GetEnvironmentStatusAsync(
        string project, int pipelineId, int scanDepth = 5, CancellationToken ct = default)
    {
        var runs = await GetPipelineRunsAsync(project, pipelineId, top: scanDepth, ct: ct).ConfigureAwait(false);
        var envMap = new Dictionary<string, PipelineEnvironmentStatus>(StringComparer.OrdinalIgnoreCase);

        foreach (var runHeader in runs)
        {
            AdoPipelineRun run;
            try
            {
                run = runHeader.Stages.Count > 0
                    ? runHeader
                    : await GetPipelineRunAsync(project, pipelineId, runHeader.Id, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to process a pipeline run entry; skipping"); continue; }

            var waitingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (run.State != "completed")
            {
                try
                {
                    var waiting = await GetWaitingStagesAsync(project, run.Id, ct).ConfigureAwait(false);
                    foreach (var w in waiting) waitingNames.Add(w.StageName);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Fallback exception while processing pipeline run");
                }
            }

            foreach (var stage in run.Stages)
            {
                var envName = stage.EnvironmentName ?? stage.Name;
                if (envMap.ContainsKey(envName)) continue;

                envMap[envName] = new PipelineEnvironmentStatus(
                    EnvironmentName: envName,
                    StageName: stage.Name,
                    LatestRunId: run.Id,
                    RunName: run.Name,
                    State: stage.State,
                    Result: stage.Result,
                    FinishedAt: run.FinishedDate,
                    TriggeredBy: run.TriggeredBy,
                    WaitingForApproval: waitingNames.Contains(stage.Name));
            }

            if (envMap.Count > 0 && runs.IndexOf(runHeader) >= 2)
                break; // have data from at least 3 runs, stop scanning
        }

        return [.. envMap.Values];
    }

    // ── Environments ──

    public async Task<List<AdoEnvironment>> GetEnvironmentsAsync(string project, CancellationToken ct = default)
    {
        var response = await GetFromJsonAsync<AdoListResponse<AdoEnvironmentDto>>(
            $"{ProjectApi(project)}/distributedtask/environments?api-version=7.1", JsonOptions, ct).ConfigureAwait(false);

        return response?.Value.Select(e => new AdoEnvironment(
            e.Id,
            e.Name ?? string.Empty
        )).ToList() ?? [];
    }

    // ── Private helpers ──

    private async Task<List<AdoPipelineStage>> GetRunStagesAsync(string project, int runId, CancellationToken ct)
    {
        try
        {
            var timeline = await GetFromJsonAsync<AdoTimelineDto>(
                $"{ProjectApi(project)}/build/builds/{runId}/timeline?api-version=7.1", JsonOptions, ct).ConfigureAwait(false);

            return timeline?.Records?
                .Where(r => r.Type == "Stage")
                .OrderBy(r => r.Order)
                .Select(r => new AdoPipelineStage(
                    r.Name ?? string.Empty,
                    r.State ?? string.Empty,
                    r.Result ?? string.Empty,
                    r.Order,
                    r.Identifier))
                .ToList() ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch pipeline stages for run {RunId} in project {Project}", runId, project);
            return [];
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Options.Set(DevOpsAuthHandler.PatCredentialKeyOption, _patCredentialKey);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return request;
    }

    private async Task<T?> GetFromJsonAsync<T>(string url, JsonSerializerOptions options, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(options, ct).ConfigureAwait(false);
    }

    private static AdoPipelineRun MapPipelineRun(AdoPipelineRunDto dto)
    {
        var sourceBranch = dto.Resources?.Repositories?.GetValueOrDefault("self")?.RefName
            ?? string.Empty;

        if (sourceBranch.StartsWith("refs/heads/", StringComparison.Ordinal))
            sourceBranch = sourceBranch["refs/heads/".Length..];

        return new AdoPipelineRun(
            Id: dto.Id,
            PipelineId: dto.Pipeline?.Id ?? 0,
            Name: dto.Name ?? dto.Pipeline?.Name ?? "Unknown",
            State: dto.State ?? "unknown",
            Result: dto.Result ?? string.Empty,
            CreatedDate: dto.CreatedDate,
            FinishedDate: dto.FinishedDate,
            SourceBranch: sourceBranch,
            TriggeredBy: null,
            WebUrl: dto.Links?.Web?.Href,
            Stages: []);
    }
}
