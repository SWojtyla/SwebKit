using System.Text.Json.Serialization;

namespace SwebKit.DevOps;

/// <summary>
/// Internal DTOs for Azure DevOps REST API JSON responses.
/// These are deserialized from the API and mapped to domain models in SwebKit.Core.
/// </summary>

// ── Generic wrapper ──

internal sealed record AdoListResponse<T>(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("value")] List<T> Value);

// ── Pipelines ──

internal sealed record AdoPipelineDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("folder")] string? Folder,
    [property: JsonPropertyName("url")] string? Url);

internal sealed record AdoPipelineRunDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("pipeline")] AdoPipelineRefDto? Pipeline,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("result")] string? Result,
    [property: JsonPropertyName("createdDate")] DateTimeOffset CreatedDate,
    [property: JsonPropertyName("finishedDate")] DateTimeOffset? FinishedDate,
    [property: JsonPropertyName("resources")] AdoRunResourcesDto? Resources,
    [property: JsonPropertyName("_links")] AdoLinksDto? Links);

internal sealed record AdoPipelineRefDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name);

internal sealed record AdoRunResourcesDto(
    [property: JsonPropertyName("repositories")] Dictionary<string, AdoRunRepoDto>? Repositories);

internal sealed record AdoRunRepoDto(
    [property: JsonPropertyName("refName")] string? RefName);

internal sealed record AdoLinksDto(
    [property: JsonPropertyName("web")] AdoLinkRefDto? Web);

internal sealed record AdoLinkRefDto(
    [property: JsonPropertyName("href")] string? Href);

// ── Build timeline (for stage info) ──

internal sealed record AdoTimelineDto(
    [property: JsonPropertyName("records")] List<AdoTimelineRecordDto>? Records);

internal sealed record AdoTimelineRecordDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("parentId")] string? ParentId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("result")] string? Result,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("identifier")] string? Identifier);

// ── Approvals ──

internal sealed record AdoApprovalDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("createdOn")] DateTimeOffset CreatedOn,
    [property: JsonPropertyName("pipeline")] AdoApprovalPipelineDto? Pipeline,
    [property: JsonPropertyName("steps")] List<AdoApprovalStepDto>? Steps,
    [property: JsonPropertyName("_links")] AdoLinksDto? Links);

internal sealed record AdoApprovalPipelineDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name);

internal sealed record AdoApprovalStepDto(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("assignedApprover")] AdoIdentityDto? AssignedApprover);

internal sealed record AdoIdentityDto(
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("uniqueName")] string? UniqueName);

// ── Git ──

internal sealed record AdoRepositoryDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("defaultBranch")] string? DefaultBranch,
    [property: JsonPropertyName("webUrl")] string? WebUrl);

internal sealed record AdoAnnotatedTagDto(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("objectId")] string? ObjectId,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("taggedBy")] AdoTagPersonDto? TaggedBy,
    [property: JsonPropertyName("taggedObject")] AdoTaggedObjectDto? TaggedObject);

internal sealed record AdoTagPersonDto(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("date")] DateTimeOffset? Date);

internal sealed record AdoTaggedObjectDto(
    [property: JsonPropertyName("objectId")] string? ObjectId);

internal sealed record AdoCommitDto(
    [property: JsonPropertyName("commitId")] string? CommitId,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("author")] AdoCommitAuthorDto? Author);

internal sealed record AdoCommitAuthorDto(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("date")] DateTimeOffset Date);

internal sealed record AdoAnnotatedTagCreateDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("taggedObject")] AdoTaggedObjectDto TaggedObject,
    [property: JsonPropertyName("message")] string Message);

// ── Git refs (for listing tags) ──

internal sealed record AdoRefDto(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("objectId")] string? ObjectId,
    [property: JsonPropertyName("creator")] AdoIdentityDto? Creator);

// ── Environments ──

internal sealed record AdoEnvironmentDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name);

// ── Pipeline run trigger ──

internal sealed record AdoPipelineRunTriggerDto(
    [property: JsonPropertyName("resources")] AdoTriggerResourcesDto Resources,
    [property: JsonPropertyName("templateParameters")] Dictionary<string, string>? TemplateParameters);

internal sealed record AdoTriggerResourcesDto(
    [property: JsonPropertyName("repositories")] Dictionary<string, AdoTriggerRepoDto> Repositories);

internal sealed record AdoTriggerRepoDto(
    [property: JsonPropertyName("refName")] string RefName);

// ── Approval patch ──

internal sealed record AdoApprovalPatchDto(
    [property: JsonPropertyName("approvalId")] string ApprovalId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("comment")] string? Comment);

// ── Projects (for connection test) ──

internal sealed record AdoProjectDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name);
