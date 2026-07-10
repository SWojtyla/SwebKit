using SwebKit.Core.Domain;

namespace SwebKit.App.Components.ApiClient;

/// <summary>
/// Page-scoped state container for <see cref="ApiClientPage"/>. Holds the core mutable state that
/// was previously kept as private fields directly on the page component.
/// </summary>
/// <remarks>
/// Per DEC-UX-3 (docs/features/active/api-client-ux-refactor/decisions.md): this is a plain,
/// page-scoped POCO — instantiated once per <see cref="ApiClientPage"/> instance as a private
/// field — and is NOT registered with dependency injection. State stays parent/container-owned;
/// no stateful child component holds page-level truth.
/// Public (rather than internal) because presentational child components in this folder (e.g.
/// <c>ApiClientToolbar</c>) receive it as a component parameter, and Razor component classes are
/// generated as public.
/// </remarks>
public sealed class ApiClientState
{
    // Worksheet mode constants — single source of truth (Phase 2, Task 5). Referenced by
    // ApiClientPage and ApiClientManagementScreens so the two never drift out of sync.
    public const string WorksheetEnvs = "envs";
    public const string WorksheetVars = "vars";
    public const string WorksheetVariables = "variables";
    public const string WorksheetLinkedRoots = "linked-roots";
    public const string WorksheetGit = "git";

    public bool LoadingCollections { get; set; } = true;
    public List<ApiCollection> Collections { get; set; } = [];
    public List<LinkedCollectionRootLoadResult> LinkedRootResults { get; set; } = [];
    public IReadOnlyList<LinkedCollectionTreeInfo> LinkedRootInfos { get; set; } = [];
    public List<ApiEnvironment> Environments { get; set; } = [];
    public ApiCollection? ActiveCollection { get; set; }
    public string? SelectedRequestId { get; set; }
    public HttpRequestEntry? SelectedRequest { get; set; }
    public string? ActiveEnvironmentId { get; set; }
    public bool IsDirty { get; set; }
    public bool AutoSave { get; set; }
    public string? LinkedSaveError { get; set; }
    internal ApiClientPage.LinkedSaveConflict? LinkedSaveConflict { get; set; }
    public string? ActiveLinkedRootId { get; set; }
    public string? WorkflowMessage { get; set; }
    public bool WorkflowMessageIsError { get; set; }
    public Dictionary<string, bool> DirtyByRequestId { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, HttpRequestResult> LastResultByRequestId { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<GraphQlSubscriptionMessage>> SubscriptionMessagesByRequestId { get; set; } = new(StringComparer.Ordinal);
    public HttpRequestResult? LastResult { get; set; }
    public Dictionary<string, List<HttpRequestResult>> RequestHistory { get; set; } = new(StringComparer.Ordinal);
    public List<GraphQlSubscriptionMessage> SubscriptionMessages { get; set; } = [];
    public string? WorksheetMode { get; set; }
    public int ScopeVersion { get; set; }

    // Moved from ApiClientPage page fields (Phase 2, Task 5): ApiClientManagementScreens is
    // destroyed/recreated when the user toggles worksheet mode, so this must live on the
    // page-owned state container to survive leaving and re-entering the Variables worksheet —
    // matching today's behaviour where the page itself never unmounts.
    public IReadOnlyList<VariableInspectionItem> VariableInspectionItems { get; set; } = [];
    public bool VariableInspectorLoading { get; set; }

    // Moved from ApiClientPage page fields (Phase 2, Task 6): ApiClientGitPanel is
    // destroyed/recreated when the user toggles worksheet mode, so this must live on the
    // page-owned state container to survive leaving and re-entering the Git worksheet —
    // matching today's behaviour where the page itself never unmounts (DEC-UX-3).
    public string? ActiveGitRootId { get; set; }
    public IReadOnlyList<LinkedGitBranch> GitBranches { get; set; } = [];
    public string? GitRemoteUrl { get; set; }
    public string? GitCompareUrl { get; set; }
    public LinkedGitFileDiff? GitDiff { get; set; }
    public bool GitDiffLoading { get; set; }
    public string GitBranchToSwitch { get; set; } = string.Empty;
    public string GitBranchName { get; set; } = string.Empty;
    public string GitCommitMessage { get; set; } = string.Empty;
    public string? GitMessage { get; set; }
    public bool GitMessageIsError { get; set; }
    public string? PendingRevertGitFilePath { get; set; }
}
