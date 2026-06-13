namespace SwebKit.Core.Domain;

public sealed class LinkedCollectionRootStore
{
    public int SchemaVersion { get; set; } = 1;
    public List<LinkedCollectionRootConfig> Roots { get; set; } = [];
}

public sealed class LinkedCollectionRootConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset AddedAt { get; set; }
}

public sealed class LinkedCollectionRootLoadResult
{
    public LinkedCollectionRootConfig Config { get; init; } = new();
    public string ApiRootPath { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public IReadOnlyList<ApiCollection> Collections { get; init; } = [];
    public IReadOnlyList<ApiEnvironment> Environments { get; init; } = [];
    public IReadOnlyList<LinkedRequestFileState> RequestFiles { get; init; } = [];
    public IReadOnlyList<LinkedEnvironmentFileState> EnvironmentFiles { get; init; } = [];
    public LinkedGitStatus GitStatus { get; init; } = new();
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
    public bool IsValid => Diagnostics.Count == 0;
}

public sealed class LinkedEnvironmentFileState
{
    public string EnvironmentId { get; init; } = string.Empty;
    public string EnvironmentFilePath { get; init; } = string.Empty;
}

public sealed class LinkedRequestFileState
{
    public string RequestId { get; init; } = string.Empty;
    public string RequestFilePath { get; init; } = string.Empty;
    public string ContentStamp { get; init; } = string.Empty;
}

public sealed class LinkedRequestSaveResult
{
    public bool IsSuccess { get; init; }
    public bool HasConflict { get; init; }
    public string? RequestFilePath { get; init; }
    public string? CurrentContentStamp { get; init; }
    public string? ErrorMessage { get; init; }

    public static LinkedRequestSaveResult Success(string requestFilePath, string currentContentStamp) => new()
    {
        IsSuccess = true,
        RequestFilePath = requestFilePath,
        CurrentContentStamp = currentContentStamp,
    };

    public static LinkedRequestSaveResult Conflict(string requestFilePath, string currentContentStamp) => new()
    {
        IsSuccess = false,
        HasConflict = true,
        RequestFilePath = requestFilePath,
        CurrentContentStamp = currentContentStamp,
        ErrorMessage = "The linked request changed on disk. Reload it before saving to avoid overwriting external changes.",
    };
}

public sealed class LinkedCollectionTreeInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string? Branch { get; init; }
    public int ChangedFileCount { get; init; }
    public bool IsGitRepository { get; init; }
    public bool IsValid { get; init; } = true;
    public IReadOnlyList<string> CollectionIds { get; init; } = [];
}

public sealed class LinkedGitStatus
{
    public bool IsGitRepository { get; init; }
    public string? RepositoryRoot { get; init; }
    public string? Branch { get; init; }
    public int ModifiedCount { get; init; }
    public int UntrackedCount { get; init; }
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
    public string? ErrorMessage { get; init; }
    public bool HasChanges => ModifiedCount > 0 || UntrackedCount > 0;
    public int ChangedFileCount => ModifiedCount + UntrackedCount;
}

public sealed class LinkedGitCommandResult
{
    public bool IsSuccess { get; init; }
    public string? Message { get; init; }
    public string? ErrorMessage { get; init; }

    public static LinkedGitCommandResult Success(string message) => new() { IsSuccess = true, Message = message };

    public static LinkedGitCommandResult Failure(string errorMessage) => new() { ErrorMessage = errorMessage };
}

public sealed class SwebKitApiRootManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Format { get; set; } = "swebkit-api-root";
    public string Name { get; set; } = string.Empty;
}

public sealed class SwebKitCollectionManifest
{
    public string? Name { get; set; }
    public List<CollectionVariable> Variables { get; set; } = [];
    public AuthConfig? DefaultAuth { get; set; }
}
