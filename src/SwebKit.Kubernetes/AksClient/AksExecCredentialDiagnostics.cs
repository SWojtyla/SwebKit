using k8s;
using k8s.KubeConfigModels;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace SwebKit.Kubernetes.AksClient;

/// <summary>
/// Explains *why* an AAD-enabled AKS cluster answered HTTP 401 by re-running the kubeconfig's
/// exec credential plugin (<c>kubelogin get-token …</c>) and capturing its output.
/// <para>
/// The Kubernetes client library runs that plugin internally and writes its stderr to the host
/// process's console, so the actual cause — e.g. <c>failed to get token: AzureCLICredential:
/// Azure CLI not found on path</c> — never reaches the application, let alone the UI. Without
/// this probe a broken <c>az</c>/<c>kubelogin</c> install is indistinguishable from an empty
/// cluster: every request just fails and the namespace picker renders "No namespaces found".
/// </para>
/// <para>
/// Only invoked on the 401 failure path, never in the happy path, so the extra process launch
/// costs nothing during normal operation.
/// </para>
/// </summary>
internal static class AksExecCredentialDiagnostics
{
    /// <summary>Upper bound on the plugin output echoed into an exception message.</summary>
    private const int MaxDiagnosticLength = 400;

    /// <summary>
    /// Budget for the probe. Generous enough for an interactive device-code or browser prompt to
    /// be declined, short enough that a wedged plugin doesn't hang the caller indefinitely.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Matches JWT-shaped runs so a plugin that echoes a token back on stderr cannot leak it into
    /// an exception message that the sidecar returns over HTTP.
    /// </summary>
    private static readonly Regex JwtLikeRegex = new(
        @"eyJ[A-Za-z0-9_\-\.]{20,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Builds the user-facing message for <see cref="SwebKit.Core.Abstractions.AksAuthenticationException"/>.
    /// Safe to display in the UI and to return over the sidecar HTTP API: it contains no kubeconfig
    /// paths, no resource ids and no token material.
    /// </summary>
    /// <param name="execFailure">
    /// Sanitized credential-plugin output from <see cref="TryDescribeFailureAsync"/>, or
    /// <see langword="null"/> when the plugin itself is healthy (or could not be probed) and the
    /// 401 therefore has another cause.
    /// </param>
    public static string BuildAuthenticationMessage(string? execFailure)
    {
        var builder = new StringBuilder(
            "The cluster rejected the request as unauthorized (HTTP 401): no valid Azure AD token could be obtained for this kubeconfig context.");

        if (!string.IsNullOrWhiteSpace(execFailure))
        {
            builder.Append(" The kubeconfig credential plugin failed: ").Append(execFailure).Append('.');
        }

        builder.Append(
            " Sign in again with `az login` and reconnect — and if `az` is missing from PATH, reinstall the Azure CLI, "
            + "because `kubelogin --login azurecli` shells out to it.");

        return builder.ToString();
    }

    /// <summary>
    /// Runs the exec credential plugin configured for <paramref name="kubeconfigContext"/> and
    /// returns a sanitized one-line summary of its failure, or <see langword="null"/> when the
    /// plugin succeeded, the context uses no exec credential, or the probe could not run at all.
    /// Never throws for probe-local problems — a diagnostic that fails must not replace the 401
    /// it was meant to explain.
    /// </summary>
    public static async Task<string?> TryDescribeFailureAsync(
        string? kubeconfigPath,
        string? kubeconfigContext,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        try
        {
            var path = string.IsNullOrWhiteSpace(kubeconfigPath)
                ? KubernetesClientConfiguration.KubeConfigDefaultLocation
                : kubeconfigPath;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            var kubeConfig = KubernetesClientConfiguration.LoadKubeConfig(path);
            var exec = FindExecCredential(kubeConfig, kubeconfigContext);
            if (exec is null || string.IsNullOrWhiteSpace(exec.Command))
                return null;

            return await RunProbeAsync(exec, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Could not probe the kubeconfig exec credential plugin while diagnosing an AKS 401.");
            return null;
        }
    }

    /// <summary>
    /// Resolves the <c>exec</c> credential block for the named context (or the kubeconfig's
    /// current context when <paramref name="kubeconfigContext"/> is blank).
    /// </summary>
    internal static ExternalExecution? FindExecCredential(K8SConfiguration kubeConfig, string? kubeconfigContext)
    {
        ArgumentNullException.ThrowIfNull(kubeConfig);

        var contextName = string.IsNullOrWhiteSpace(kubeconfigContext)
            ? kubeConfig.CurrentContext
            : kubeconfigContext;

        if (string.IsNullOrWhiteSpace(contextName))
            return null;

        var context = (kubeConfig.Contexts ?? Enumerable.Empty<Context>())
            .FirstOrDefault(ctx => string.Equals(ctx.Name, contextName, StringComparison.Ordinal));

        var userName = context?.ContextDetails?.User;
        if (string.IsNullOrWhiteSpace(userName))
            return null;

        return (kubeConfig.Users ?? Enumerable.Empty<User>())
            .FirstOrDefault(user => string.Equals(user.Name, userName, StringComparison.Ordinal))
            ?.UserCredentials
            ?.ExternalExecution;
    }

    /// <summary>
    /// Turns a finished probe into a diagnostic string. Returns <see langword="null"/> when the
    /// plugin actually produced a token, since the 401 then comes from somewhere else (wrong
    /// audience, cluster-side AAD integration) and blaming the plugin would mislead.
    /// </summary>
    internal static string? SummarizeProbeOutput(int exitCode, string? standardOutput, string? standardError)
    {
        var stdout = standardOutput ?? string.Empty;
        if (exitCode == 0 && stdout.Contains("\"token\"", StringComparison.OrdinalIgnoreCase))
            return null;

        var detail = CollapseWhitespace(standardError);
        if (detail.Length == 0)
            detail = CollapseWhitespace(stdout);
        if (detail.Length == 0)
            detail = $"exited with code {exitCode} and produced no output";

        return Truncate(JwtLikeRegex.Replace(detail, "[redacted]"));
    }

    private static async Task<string?> RunProbeAsync(ExternalExecution exec, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(exec.Command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in exec.Arguments ?? Enumerable.Empty<string>())
        {
            startInfo.ArgumentList.Add(argument);
        }

        // kubeconfig `env:` entries are name/value pairs the plugin may depend on (e.g. AZURE_*
        // overrides); the client library passes them through, so the probe must too or it would
        // report a failure the real request never had.
        foreach (var entry in exec.EnvironmentVariables ?? Enumerable.Empty<Dictionary<string, string>>())
        {
            if (entry.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name))
            {
                startInfo.Environment[name] = entry.TryGetValue("value", out var value) ? value : string.Empty;
            }
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            // The classic "installHint" case: the plugin named in the kubeconfig isn't installed.
            return $"`{exec.Command}` could not be started — it is not installed or not on PATH";
        }

        if (process is null)
            return $"`{exec.Command}` could not be started";

        using (process)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TryKill(process);
                return $"`{exec.Command}` did not respond within {ProbeTimeout.TotalSeconds:N0}s "
                    + "(it may be waiting for an interactive sign-in)";
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return SummarizeProbeOutput(process.ExitCode, stdout, stderr);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited between the timeout firing and the kill — nothing to clean up.
        }
        catch (Win32Exception)
        {
            // Access denied terminating the tree; the probe result matters more than the cleanup.
        }
    }

    private static string CollapseWhitespace(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : WhitespaceRegex.Replace(value.Trim(), " ");

    private static string Truncate(string value)
        => value.Length <= MaxDiagnosticLength ? value : value[..MaxDiagnosticLength] + "…";
}
