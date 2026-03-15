using System.Net.Http.Json;
using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.DevOps;

public class DevOpsClient : IDevOpsClient
{
    private readonly HttpClient _http;
    private readonly DevOpsAuthHandler _authHandler;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private string? _orgUrl;

    public DevOpsClient(IHttpClientFactory httpClientFactory, DevOpsAuthHandler authHandler)
    {
        _http = httpClientFactory.CreateClient("AzureDevOps");
        _authHandler = authHandler;
    }

    /// <summary>
    /// Configures the client with org-level connection details.
    /// All project-scoped calls accept the project name as a parameter.
    /// </summary>
    public void Configure(DevOpsConfig config)
    {
        _orgUrl = $"https://dev.azure.com/{config.Organization}";
        _authHandler.SetCredentialKey(config.PatCredentialKey);
    }

    private string OrgUrl => _orgUrl
        ?? throw new InvalidOperationException("DevOpsClient is not configured. Call Configure() first.");

    private string OrgApi => $"{OrgUrl}/_apis";

    private string ProjectApi(string project) => $"{OrgUrl}/{project}/_apis";

    // ── Connection ──

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"{OrgApi}/projects?api-version=7.1&$top=1", ct);
        return response.IsSuccessStatusCode;
    }

    // ── Projects ──

    public async Task<List<AdoProject>> GetProjectsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetFromJsonAsync<AdoListResponse<AdoProjectDto>>(
            $"{OrgApi}/projects?api-version=7.1&$top=100", JsonOptions, ct);

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
        var response = await _http.GetFromJsonAsync<AdoListResponse<AdoPipelineDto>>(
            $"{ProjectApi(project)}/pipelines?api-version=7.1", JsonOptions, ct);

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

        var response = await _http.GetFromJsonAsync<AdoListResponse<AdoPipelineRunDto>>(
            url, JsonOptions, ct);

        return response?.Value.Select(MapPipelineRun).ToList() ?? [];
    }

    public async Task<AdoPipelineRun> GetPipelineRunAsync(
        string project, int pipelineId, int runId, CancellationToken ct = default)
    {
        var runDto = await _http.GetFromJsonAsync<AdoPipelineRunDto>(
            $"{ProjectApi(project)}/pipelines/{pipelineId}/runs/{runId}?api-version=7.1", JsonOptions, ct)
            ?? throw new InvalidOperationException($"Pipeline run {runId} not found.");

        var stages = await GetRunStagesAsync(project, runId, ct);

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

        var response = await _http.PostAsJsonAsync(
            $"{ProjectApi(project)}/pipelines/{pipelineId}/runs?api-version=7.1", body, JsonOptions, ct);

        response.EnsureSuccessStatusCode();

        var runDto = await response.Content.ReadFromJsonAsync<AdoPipelineRunDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to parse pipeline run response.");

        return MapPipelineRun(runDto);
    }

    // ── Approvals ──

    public async Task<List<AdoApproval>> GetPendingApprovalsAsync(string project, CancellationToken ct = default)
    {
        var response = await _http.GetFromJsonAsync<AdoListResponse<AdoApprovalDto>>(
            $"{ProjectApi(project)}/pipelines/approvals?status=pending&api-version=7.1-preview.1", JsonOptions, ct);

        return response?.Value.Select(a => new AdoApproval(
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

    public async Task<List<string>> GetWaitingStagesAsync(string project, int runId, CancellationToken ct = default)
    {
        try
        {
            var timeline = await _http.GetFromJsonAsync<AdoTimelineDto>(
                $"{ProjectApi(project)}/build/builds/{runId}/timeline?api-version=7.1", JsonOptions, ct);

            if (timeline?.Records is null) return [];

            // Find stages that have a Checkpoint child record in "inProgress" state
            // This indicates the stage is waiting for an approval/check
            var stageRecords = timeline.Records
                .Where(r => r.Type == "Stage")
                .ToDictionary(r => r.Id ?? "", r => r);

            var checkpointParents = timeline.Records
                .Where(r => r.Type == "Checkpoint" && r.State == "inProgress" && r.ParentId is not null)
                .Select(r => r.ParentId!)
                .ToHashSet();

            return stageRecords
                .Where(kv => checkpointParents.Contains(kv.Key))
                .Select(kv => kv.Value.Name ?? "Unknown stage")
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task ApproveAsync(string project, string approvalId, string? comment = null, CancellationToken ct = default)
    {
        var body = new[] { new AdoApprovalPatchDto(approvalId, "approved", comment) };
        var response = await _http.PatchAsJsonAsync(
            $"{ProjectApi(project)}/pipelines/approvals?api-version=7.1-preview.1", body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RejectAsync(string project, string approvalId, string? comment = null, CancellationToken ct = default)
    {
        var body = new[] { new AdoApprovalPatchDto(approvalId, "rejected", comment) };
        var response = await _http.PatchAsJsonAsync(
            $"{ProjectApi(project)}/pipelines/approvals?api-version=7.1-preview.1", body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    // ── Git ──

    public async Task<List<AdoRepository>> GetRepositoriesAsync(string project, CancellationToken ct = default)
    {
        var response = await _http.GetFromJsonAsync<AdoListResponse<AdoRepositoryDto>>(
            $"{ProjectApi(project)}/git/repositories?api-version=7.1", JsonOptions, ct);

        return response?.Value.Select(r => new AdoRepository(
            r.Id ?? string.Empty,
            r.Name ?? string.Empty,
            r.DefaultBranch ?? "refs/heads/main",
            r.WebUrl ?? string.Empty
        )).ToList() ?? [];
    }

    public async Task<List<AdoTag>> GetTagsAsync(string project, string repositoryId, CancellationToken ct = default)
    {
        // ADO lists tags via the refs endpoint with a tags/ filter
        var response = await _http.GetFromJsonAsync<AdoListResponse<AdoRefDto>>(
            $"{ProjectApi(project)}/git/repositories/{repositoryId}/refs?filter=tags/&api-version=7.1", JsonOptions, ct);

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

        var response = await _http.PostAsJsonAsync(
            $"{ProjectApi(project)}/git/repositories/{repositoryId}/annotatedtags?api-version=7.1",
            body, JsonOptions, ct);

        response.EnsureSuccessStatusCode();

        var tagDto = await response.Content.ReadFromJsonAsync<AdoAnnotatedTagDto>(JsonOptions, ct)
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

        var response = await _http.GetFromJsonAsync<AdoListResponse<AdoCommitDto>>(
            url, JsonOptions, ct);

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

    // ── Environments ──

    public async Task<List<AdoEnvironment>> GetEnvironmentsAsync(string project, CancellationToken ct = default)
    {
        var response = await _http.GetFromJsonAsync<AdoListResponse<AdoEnvironmentDto>>(
            $"{ProjectApi(project)}/distributedtask/environments?api-version=7.1", JsonOptions, ct);

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
            var timeline = await _http.GetFromJsonAsync<AdoTimelineDto>(
                $"{ProjectApi(project)}/build/builds/{runId}/timeline?api-version=7.1", JsonOptions, ct);

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
        catch
        {
            return [];
        }
    }

    private static AdoPipelineRun MapPipelineRun(AdoPipelineRunDto dto)
    {
        var sourceBranch = dto.Resources?.Repositories?.GetValueOrDefault("self")?.RefName
            ?? string.Empty;

        if (sourceBranch.StartsWith("refs/heads/"))
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
