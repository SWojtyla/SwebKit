using System.Diagnostics;
using System.Text.RegularExpressions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

public sealed class LinkedGitService
{
    private static readonly Regex BranchNameRegex = new(@"^[A-Za-z0-9][A-Za-z0-9._/-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<LinkedGitStatus> GetStatusAsync(string configuredPath, string apiRootPath, CancellationToken cancellationToken = default)
    {
        var workingDirectory = Directory.Exists(configuredPath) ? configuredPath : apiRootPath;
        var repoRootResult = await RunGitAsync(workingDirectory, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (repoRootResult.ExitCode != 0)
        {
            return new LinkedGitStatus { IsGitRepository = false, ErrorMessage = repoRootResult.ErrorText };
        }

        var repoRoot = repoRootResult.OutputText.Trim();
        var branchResult = await RunGitAsync(repoRoot, ["branch", "--show-current"], cancellationToken);
        var statusResult = await RunGitAsync(repoRoot, ["status", "--porcelain", "--untracked-files=all"], cancellationToken);
        if (statusResult.ExitCode != 0)
        {
            return new LinkedGitStatus
            {
                IsGitRepository = true,
                RepositoryRoot = repoRoot,
                Branch = branchResult.OutputText.Trim(),
                ErrorMessage = statusResult.ErrorText,
            };
        }

        var relativeApiRoot = NormalizePath(Path.GetRelativePath(repoRoot, apiRootPath));
        var modified = 0;
        var untracked = 0;
        var changedFiles = new List<string>();
        var changedFileDetails = new List<LinkedGitChangedFile>();

        foreach (var line in statusResult.OutputText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var changedFile = ParseStatusLine(line);
            if (changedFile is null)
            {
                continue;
            }

            if (!IsUnderApiRoot(changedFile.Path, relativeApiRoot))
            {
                continue;
            }

            changedFiles.Add(changedFile.Path);
            changedFileDetails.Add(changedFile);

            if (changedFile.IsUntracked)
            {
                untracked++;
            }
            else
            {
                modified++;
            }
        }

        return new LinkedGitStatus
        {
            IsGitRepository = true,
            RepositoryRoot = repoRoot,
            Branch = branchResult.OutputText.Trim(),
            ModifiedCount = modified,
            UntrackedCount = untracked,
            ChangedFiles = changedFiles,
            ChangedFileDetails = changedFileDetails,
        };
    }

    public async Task<IReadOnlyList<LinkedGitBranch>> GetBranchesAsync(string configuredPath, CancellationToken cancellationToken = default)
    {
        var repoRootResult = await RunGitAsync(configuredPath, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (repoRootResult.ExitCode != 0)
        {
            return [];
        }

        var repoRoot = repoRootResult.OutputText.Trim();
        var currentResult = await RunGitAsync(repoRoot, ["branch", "--show-current"], cancellationToken);
        var branchResult = await RunGitAsync(repoRoot, ["branch", "--format=%(refname:short)"], cancellationToken);
        if (branchResult.ExitCode != 0)
        {
            return [];
        }

        var currentBranch = currentResult.OutputText.Trim();
        return branchResult.OutputText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static branch => branch, StringComparer.OrdinalIgnoreCase)
            .Select(branch => new LinkedGitBranch
            {
                Name = branch,
                IsCurrent = string.Equals(branch, currentBranch, StringComparison.OrdinalIgnoreCase),
            })
            .ToList();
    }

    public async Task<string?> GetOriginRemoteUrlAsync(string configuredPath, CancellationToken cancellationToken = default)
    {
        var repoRootResult = await RunGitAsync(configuredPath, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (repoRootResult.ExitCode != 0)
        {
            return null;
        }

        var remoteResult = await RunGitAsync(repoRootResult.OutputText.Trim(), ["remote", "get-url", "origin"], cancellationToken);
        return remoteResult.ExitCode == 0 ? remoteResult.OutputText.Trim() : null;
    }

    public async Task<LinkedGitFileDiff> GetFileDiffAsync(string configuredPath, string apiRootPath, string relativePath, CancellationToken cancellationToken = default)
    {
        var (status, changedFile, failure) = await GetScopedChangedFileAsync(configuredPath, apiRootPath, relativePath, cancellationToken);
        if (failure is not null)
        {
            return new LinkedGitFileDiff
            {
                Path = NormalizePath(relativePath),
                ErrorMessage = failure.ErrorMessage ?? "File is not a changed linked API file.",
            };
        }

        var repositoryRoot = status!.RepositoryRoot!;
        var path = changedFile!.Path;
        var original = changedFile.IsUntracked || changedFile.IndexStatus == 'A'
            ? string.Empty
            : await ReadGitBlobAsync(repositoryRoot, $"HEAD:{path}", cancellationToken);
        var current = await ReadCurrentFileContentAsync(repositoryRoot, path, changedFile, cancellationToken);

        return new LinkedGitFileDiff
        {
            Path = path,
            OriginalContent = original.ReplaceLineEndings("\n"),
            CurrentContent = current.ReplaceLineEndings("\n"),
        };
    }

    public async Task<LinkedGitCommandResult> CreateBranchAsync(string configuredPath, string branchName, CancellationToken cancellationToken = default)
    {
        if (!IsSafeBranchName(branchName))
        {
            return LinkedGitCommandResult.Failure("Branch name contains unsupported characters.");
        }

        var result = await RunGitAsync(configuredPath, ["switch", "-c", branchName], cancellationToken);
        return result.ExitCode == 0
            ? LinkedGitCommandResult.Success($"Created and switched to branch '{branchName}'.")
            : LinkedGitCommandResult.Failure(result.ErrorText.Trim());
    }

    public async Task<LinkedGitCommandResult> SwitchBranchAsync(string configuredPath, string apiRootPath, string branchName, bool allowDirty = false, CancellationToken cancellationToken = default)
    {
        if (!IsSafeBranchName(branchName))
        {
            return LinkedGitCommandResult.Failure("Branch name contains unsupported characters.");
        }

        var status = await GetStatusAsync(configuredPath, apiRootPath, cancellationToken);
        if (!status.IsGitRepository)
        {
            return LinkedGitCommandResult.Failure("Linked root is not inside a Git repository.");
        }

        if (status.HasChanges && !allowDirty)
        {
            return LinkedGitCommandResult.Failure("Commit or discard linked API root changes before switching branches.");
        }

        var result = await RunGitAsync(status.RepositoryRoot!, ["switch", branchName], cancellationToken);
        return result.ExitCode == 0
            ? LinkedGitCommandResult.Success($"Switched to branch '{branchName}'.")
            : LinkedGitCommandResult.Failure(result.ErrorText.Trim());
    }

    public async Task<LinkedGitCommandResult> CommitApiFilesAsync(string configuredPath, string apiRootPath, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return LinkedGitCommandResult.Failure("Commit message is required.");
        }

        var status = await GetStatusAsync(configuredPath, apiRootPath, cancellationToken);
        if (!status.IsGitRepository || string.IsNullOrWhiteSpace(status.RepositoryRoot))
        {
            return LinkedGitCommandResult.Failure("Linked root is not inside a Git repository.");
        }

        if (status.ChangedFiles.Count == 0)
        {
            return LinkedGitCommandResult.Failure("No linked API root changes to commit.");
        }

        var addArgs = new List<string> { "add", "--" };
        addArgs.AddRange(status.ChangedFiles);
        var addResult = await RunGitAsync(status.RepositoryRoot, addArgs, cancellationToken);
        if (addResult.ExitCode != 0)
        {
            return LinkedGitCommandResult.Failure(addResult.ErrorText.Trim());
        }

        var commitResult = await RunGitAsync(status.RepositoryRoot, ["commit", "-m", message.Trim()], cancellationToken);
        return commitResult.ExitCode == 0
            ? LinkedGitCommandResult.Success("Committed linked API root changes.")
            : LinkedGitCommandResult.Failure(commitResult.ErrorText.Trim());
    }

    public async Task<LinkedGitCommandResult> CommitStagedApiFilesAsync(string configuredPath, string apiRootPath, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return LinkedGitCommandResult.Failure("Commit message is required.");
        }

        var status = await GetStatusAsync(configuredPath, apiRootPath, cancellationToken);
        if (!status.IsGitRepository || string.IsNullOrWhiteSpace(status.RepositoryRoot))
        {
            return LinkedGitCommandResult.Failure("Linked root is not inside a Git repository.");
        }

        var stagedFiles = status.ChangedFileDetails
            .Where(static file => file.IsStaged)
            .Select(static file => file.Path)
            .ToList();
        if (stagedFiles.Count == 0)
        {
            return LinkedGitCommandResult.Failure("Stage one or more API files before committing.");
        }

        var allStagedResult = await RunGitAsync(status.RepositoryRoot, ["diff", "--cached", "--name-only"], cancellationToken);
        if (allStagedResult.ExitCode != 0)
        {
            return LinkedGitCommandResult.Failure(GetGitError(allStagedResult));
        }

        var relativeApiRoot = NormalizePath(Path.GetRelativePath(status.RepositoryRoot, apiRootPath));
        var allStagedFiles = allStagedResult.OutputText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizePath)
            .ToList();
        if (allStagedFiles.Any(path => !IsUnderApiRoot(path, relativeApiRoot)))
        {
            return LinkedGitCommandResult.Failure("Unstage non-API files before committing from SwebKit.");
        }

        var commitResult = await RunGitAsync(status.RepositoryRoot, ["commit", "-m", message.Trim()], cancellationToken);
        return commitResult.ExitCode == 0
            ? LinkedGitCommandResult.Success("Committed staged API root changes.")
            : LinkedGitCommandResult.Failure(GetGitError(commitResult));
    }

    public async Task<LinkedGitCommandResult> StageFileAsync(string configuredPath, string apiRootPath, string relativePath, CancellationToken cancellationToken = default)
    {
        var (status, changedFile, failure) = await GetScopedChangedFileAsync(configuredPath, apiRootPath, relativePath, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        if (!changedFile!.HasUnstagedChanges)
        {
            return LinkedGitCommandResult.Failure("File has no unstaged changes to stage.");
        }

        var result = await RunGitAsync(status!.RepositoryRoot!, ["add", "--", changedFile.Path], cancellationToken);
        return result.ExitCode == 0
            ? LinkedGitCommandResult.Success($"Staged {changedFile.Path}.")
            : LinkedGitCommandResult.Failure(GetGitError(result));
    }

    public async Task<LinkedGitCommandResult> StageAllApiFilesAsync(string configuredPath, string apiRootPath, CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(configuredPath, apiRootPath, cancellationToken);
        if (!status.IsGitRepository || string.IsNullOrWhiteSpace(status.RepositoryRoot))
        {
            return LinkedGitCommandResult.Failure("Linked root is not inside a Git repository.");
        }

        var files = status.ChangedFileDetails.Where(static file => file.HasUnstagedChanges).Select(static file => file.Path).ToList();
        if (files.Count == 0)
        {
            return LinkedGitCommandResult.Failure("No unstaged API files to stage.");
        }

        var args = new List<string> { "add", "--" };
        args.AddRange(files);
        var result = await RunGitAsync(status.RepositoryRoot, args, cancellationToken);
        return result.ExitCode == 0
            ? LinkedGitCommandResult.Success("Staged all changed API files.")
            : LinkedGitCommandResult.Failure(GetGitError(result));
    }

    public async Task<LinkedGitCommandResult> UnstageFileAsync(string configuredPath, string apiRootPath, string relativePath, CancellationToken cancellationToken = default)
    {
        var (status, changedFile, failure) = await GetScopedChangedFileAsync(configuredPath, apiRootPath, relativePath, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        if (!changedFile!.IsStaged)
        {
            return LinkedGitCommandResult.Failure("File has no staged changes to unstage.");
        }

        var result = await RunGitAsync(status!.RepositoryRoot!, ["restore", "--staged", "--", changedFile.Path], cancellationToken);
        return result.ExitCode == 0
            ? LinkedGitCommandResult.Success($"Unstaged {changedFile.Path}.")
            : LinkedGitCommandResult.Failure(GetGitError(result));
    }

    public async Task<LinkedGitCommandResult> UnstageAllApiFilesAsync(string configuredPath, string apiRootPath, CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(configuredPath, apiRootPath, cancellationToken);
        if (!status.IsGitRepository || string.IsNullOrWhiteSpace(status.RepositoryRoot))
        {
            return LinkedGitCommandResult.Failure("Linked root is not inside a Git repository.");
        }

        var files = status.ChangedFileDetails.Where(static file => file.IsStaged).Select(static file => file.Path).ToList();
        if (files.Count == 0)
        {
            return LinkedGitCommandResult.Failure("No staged API files to unstage.");
        }

        var args = new List<string> { "restore", "--staged", "--" };
        args.AddRange(files);
        var result = await RunGitAsync(status.RepositoryRoot, args, cancellationToken);
        return result.ExitCode == 0
            ? LinkedGitCommandResult.Success("Unstaged all API files.")
            : LinkedGitCommandResult.Failure(GetGitError(result));
    }

    public async Task<LinkedGitCommandResult> RevertFileAsync(string configuredPath, string apiRootPath, string relativePath, CancellationToken cancellationToken = default)
    {
        var (status, changedFile, failure) = await GetScopedChangedFileAsync(configuredPath, apiRootPath, relativePath, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        var result = changedFile!.IsUntracked
            ? await RunGitAsync(status!.RepositoryRoot!, ["clean", "-f", "--", changedFile.Path], cancellationToken)
            : await RevertTrackedFileAsync(status!.RepositoryRoot!, changedFile, cancellationToken);

        return result.ExitCode == 0
            ? LinkedGitCommandResult.Success($"Reverted {changedFile.Path}.")
            : LinkedGitCommandResult.Failure(GetGitError(result));
    }

    public async Task<LinkedGitCommandResult> PushCurrentBranchAsync(string configuredPath, CancellationToken cancellationToken = default)
    {
        var repoRootResult = await RunGitAsync(configuredPath, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (repoRootResult.ExitCode != 0)
        {
            return LinkedGitCommandResult.Failure("Linked root is not inside a Git repository.");
        }

        var pushResult = await RunGitAsync(repoRootResult.OutputText.Trim(), ["push"], cancellationToken);
        return pushResult.ExitCode == 0
            ? LinkedGitCommandResult.Success("Pushed current branch.")
            : LinkedGitCommandResult.Failure(pushResult.ErrorText.Trim());
    }

    public async Task<string?> GetRemoteCompareUrlAsync(string configuredPath, CancellationToken cancellationToken = default)
    {
        var repoRootResult = await RunGitAsync(configuredPath, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (repoRootResult.ExitCode != 0)
            return null;

        var repoRoot = repoRootResult.OutputText.Trim();
        var branchResult = await RunGitAsync(repoRoot, ["branch", "--show-current"], cancellationToken);
        var remoteResult = await RunGitAsync(repoRoot, ["remote", "get-url", "origin"], cancellationToken);
        if (branchResult.ExitCode != 0 || remoteResult.ExitCode != 0)
            return null;

        var branch = branchResult.OutputText.Trim();
        var remote = remoteResult.OutputText.Trim();
        if (string.IsNullOrWhiteSpace(branch) || string.IsNullOrWhiteSpace(remote))
            return null;

        if (TryBuildGitHubCompareUrl(remote, branch, out var githubUrl))
            return githubUrl;

        if (TryBuildAzureDevOpsCompareUrl(remote, branch, out var azureDevOpsUrl))
            return azureDevOpsUrl;

        return null;
    }

    private static bool TryBuildGitHubCompareUrl(string remote, string branch, out string url)
    {
        url = string.Empty;
        var normalized = remote.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? remote[..^4] : remote;
        if (normalized.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            var path = normalized["git@github.com:".Length..];
            url = $"https://github.com/{path}/compare/{Uri.EscapeDataString(branch)}?expand=1";
            return true;
        }

        const string httpsPrefix = "https://github.com/";
        if (normalized.StartsWith(httpsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var path = normalized[httpsPrefix.Length..];
            url = $"https://github.com/{path}/compare/{Uri.EscapeDataString(branch)}?expand=1";
            return true;
        }

        return false;
    }

    private static bool TryBuildAzureDevOpsCompareUrl(string remote, string branch, out string url)
    {
        url = string.Empty;
        var normalized = remote.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? remote[..^4] : remote;
        var marker = "/_git/";
        if (!normalized.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase) || !normalized.Contains(marker, StringComparison.OrdinalIgnoreCase))
            return false;

        url = $"{normalized}/pullrequestcreate?sourceRef=GB{Uri.EscapeDataString(branch)}";
        return true;
    }

    private static bool IsSafeBranchName(string branchName) =>
        !string.IsNullOrWhiteSpace(branchName) &&
        !branchName.Contains("..", StringComparison.Ordinal) &&
        !branchName.EndsWith(".", StringComparison.Ordinal) &&
        !branchName.EndsWith("/", StringComparison.Ordinal) &&
        BranchNameRegex.IsMatch(branchName);

    private static bool IsUnderApiRoot(string path, string relativeApiRoot) =>
        relativeApiRoot is "." or "" ||
        path.Equals(relativeApiRoot, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(relativeApiRoot.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) => path.Replace('\\', '/').Trim('/');

    private static LinkedGitChangedFile? ParseStatusLine(string line)
    {
        if (line.Length < 4)
        {
            return null;
        }

        var pathText = line[3..].Trim().Trim('"');
        var renameSeparator = pathText.LastIndexOf(" -> ", StringComparison.Ordinal);
        var path = NormalizePath(renameSeparator >= 0 ? pathText[(renameSeparator + 4)..].Trim().Trim('"') : pathText);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new LinkedGitChangedFile
        {
            IndexStatus = line[0],
            WorkTreeStatus = line[1],
            Path = path,
        };
    }

    private async Task<(LinkedGitStatus? Status, LinkedGitChangedFile? File, LinkedGitCommandResult? Failure)> GetScopedChangedFileAsync(
        string configuredPath,
        string apiRootPath,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizePath(relativePath);
        var status = await GetStatusAsync(configuredPath, apiRootPath, cancellationToken);
        if (!status.IsGitRepository || string.IsNullOrWhiteSpace(status.RepositoryRoot))
        {
            return (null, null, LinkedGitCommandResult.Failure("Linked root is not inside a Git repository."));
        }

        var changedFile = status.ChangedFileDetails.FirstOrDefault(file => string.Equals(file.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (changedFile is null)
        {
            return (status, null, LinkedGitCommandResult.Failure("File is not a changed linked API file."));
        }

        return (status, changedFile, null);
    }

    private async Task<GitCommandResult> RevertTrackedFileAsync(string repoRoot, LinkedGitChangedFile changedFile, CancellationToken cancellationToken)
    {
        if (changedFile.IndexStatus == 'A')
        {
            var unstageResult = await RunGitAsync(repoRoot, ["restore", "--staged", "--", changedFile.Path], cancellationToken);
            if (unstageResult.ExitCode != 0)
            {
                return unstageResult;
            }

            return await RunGitAsync(repoRoot, ["clean", "-f", "--", changedFile.Path], cancellationToken);
        }

        return await RunGitAsync(repoRoot, ["restore", "--staged", "--worktree", "--", changedFile.Path], cancellationToken);
    }

    private static async Task<string> ReadCurrentFileContentAsync(string repositoryRoot, string normalizedPath, LinkedGitChangedFile changedFile, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, normalizedPath));
        var repositoryFullPath = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(repositoryFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (File.Exists(fullPath))
        {
            return await File.ReadAllTextAsync(fullPath, cancellationToken);
        }

        return changedFile.IsStaged
            ? await ReadGitBlobAsync(repositoryRoot, $":{normalizedPath}", cancellationToken)
            : string.Empty;
    }

    private static async Task<string> ReadGitBlobAsync(string repositoryRoot, string objectName, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(repositoryRoot, ["show", objectName], cancellationToken);
        return result.ExitCode == 0 ? result.OutputText : string.Empty;
    }

    private static string GetGitError(GitCommandResult result)
    {
        var error = string.IsNullOrWhiteSpace(result.ErrorText) ? result.OutputText : result.ErrorText;
        return string.IsNullOrWhiteSpace(error) ? "Git command failed." : error.Trim();
    }

    private static async Task<GitCommandResult> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new GitCommandResult(-1, string.Empty, "Could not start git.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new GitCommandResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return new GitCommandResult(-1, string.Empty, ex.Message);
        }
    }

    private sealed record GitCommandResult(int ExitCode, string OutputText, string ErrorText);
}
