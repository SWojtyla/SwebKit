namespace SwebKit.Core.Domain;

/// <summary>How a release PR may be completed. Used for validation, not to drive completion.</summary>
public enum MergeStrategy
{
    FastForward,
    MergeCommit,
    Squash,
    Rebase
}

/// <summary>Global DevOps authentication mode. PAT is v1; Entra is a later milestone.</summary>
public enum DevOpsAuthenticationMode
{
    Pat,
    Entra
}

/// <summary>
/// A reusable, named group of components that ship together as one release train.
/// Stored inside <see cref="DevOpsConfig.ReleaseGroups"/>.
/// </summary>
public class ReleaseGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public MergeStrategy DefaultMergeStrategy { get; set; } = MergeStrategy.MergeCommit;

    /// <summary>
    /// Default stage aliases for this group. Keys are semantic slots (TST, STG, PRD);
    /// values are the ADO stage/environment names they map to.
    /// </summary>
    public Dictionary<string, string> StageAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TST"] = "TST",
        ["STG"] = "STG",
        ["PRD"] = "PRD"
    };

    public List<ReleaseGroupComponent> Components { get; set; } = [];
}

/// <summary>One repository/pipeline entry inside a release group.</summary>
public class ReleaseGroupComponent
{
    public string ProjectName { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string SourceBranch { get; set; } = "development";
    public string TargetBranch { get; set; } = "main";
    public int PipelineId { get; set; }
    public string? PipelineName { get; set; }
    public MergeStrategy MergeStrategy { get; set; } = MergeStrategy.MergeCommit;

    /// <summary>
    /// Per-component stage aliases. Merges over <see cref="ReleaseGroup.StageAliases"/>.
    /// </summary>
    public Dictionary<string, string> StageAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TST"] = "TST",
        ["STG"] = "STG",
        ["PRD"] = "PRD"
    };

    /// <summary>Optional prefix used when constructing pre-merge tag names (e.g. "v2.4.0").</summary>
    public string? VersionPrefix { get; set; }
}
