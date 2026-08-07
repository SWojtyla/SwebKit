namespace SwebKit.Core.Domain;

public record PipelineGroupEntry(string ProjectName, int PipelineId, string PipelineName);

public record PipelineGroup(string Id, string Name, List<PipelineGroupEntry> Pipelines);

public class DevOpsConfig
{
    public string Organization { get; set; } = string.Empty;

    public DevOpsAuthenticationMode AuthenticationMode { get; set; } = DevOpsAuthenticationMode.Pat;

    /// <summary>Key in ICredentialStore for the PAT. Never logged or exposed in UI.</summary>
    public string PatCredentialKey { get; set; } = string.Empty;

    /// <summary>
    /// ADO project names the user has opted into. Only these projects load pipeline data.
    /// Empty list means not yet configured (project picker will be shown).
    /// </summary>
    public List<string> PinnedProjects { get; set; } = [];

    /// <summary>Named groups of pipelines that can be triggered together.</summary>
    public List<PipelineGroup> PipelineGroups { get; set; } = [];

    /// <summary>Reusable release-train component groups.</summary>
    public List<ReleaseGroup> ReleaseGroups { get; set; } = [];

    /// <summary>
    /// Global default stage aliases. Per-group and per-component aliases override these.
    /// Keys are semantic slots (TST, STG, PRD); values are ADO stage/environment names.
    /// </summary>
    public Dictionary<string, string> DefaultStageAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TST"] = "TST",
        ["STG"] = "STG",
        ["PRD"] = "PRD"
    };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Organization))
            throw new InvalidOperationException($"{nameof(DevOpsConfig)}.{nameof(Organization)} is required.");
        if (AuthenticationMode == DevOpsAuthenticationMode.Pat && string.IsNullOrWhiteSpace(PatCredentialKey))
            throw new InvalidOperationException($"{nameof(DevOpsConfig)}.{nameof(PatCredentialKey)} is required when {nameof(AuthenticationMode)} is {nameof(DevOpsAuthenticationMode.Pat)}.");
    }
}
