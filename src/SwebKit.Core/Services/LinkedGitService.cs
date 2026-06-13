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

        foreach (var line in statusResult.OutputText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4)
            {
                continue;
            }

            var path = NormalizePath(line[3..].Trim().Trim('"'));
            if (!IsUnderApiRoot(path, relativeApiRoot))
            {
                continue;
            }

            changedFiles.Add(path);

            if (line.StartsWith("??", StringComparison.Ordinal))
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
