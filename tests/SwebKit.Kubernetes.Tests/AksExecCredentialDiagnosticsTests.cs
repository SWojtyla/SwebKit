using k8s.KubeConfigModels;
using SwebKit.Kubernetes.AksClient;

namespace SwebKit.Kubernetes.Tests;

public class AksExecCredentialDiagnosticsTests
{
    [Fact]
    public void SummarizeProbeOutput_WhenPluginReturnedToken_ReturnsNull()
    {
        // The plugin works, so the 401 came from somewhere else (wrong audience, cluster-side AAD
        // config). Blaming the credential plugin here would send the user down the wrong path.
        const string stdout = """
            {"kind":"ExecCredential","status":{"token":"abc","expirationTimestamp":"2026-01-01T00:00:00Z"}}
            """;

        var summary = AksExecCredentialDiagnostics.SummarizeProbeOutput(0, stdout, standardError: "");

        Assert.Null(summary);
    }

    [Fact]
    public void SummarizeProbeOutput_PrefersStandardError()
    {
        // The real-world shape: kubelogin exits non-zero and explains itself on stderr.
        var summary = AksExecCredentialDiagnostics.SummarizeProbeOutput(
            1,
            standardOutput: "",
            standardError: "Error: failed to get token: AzureCLICredential: Azure CLI not found on path");

        Assert.Equal(
            "Error: failed to get token: AzureCLICredential: Azure CLI not found on path",
            summary);
    }

    [Fact]
    public void SummarizeProbeOutput_FallsBackToStandardOutput_WhenStandardErrorIsEmpty()
    {
        var summary = AksExecCredentialDiagnostics.SummarizeProbeOutput(
            2,
            standardOutput: "no subscription found",
            standardError: "   ");

        Assert.Equal("no subscription found", summary);
    }

    [Fact]
    public void SummarizeProbeOutput_WhenTokenTextAppearsButExitCodeIsNonZero_StillReportsFailure()
    {
        // A non-zero exit means the client library got nothing usable, even if the word "token"
        // shows up in a usage/error dump.
        var summary = AksExecCredentialDiagnostics.SummarizeProbeOutput(
            1,
            standardOutput: """{"token":"partial"}""",
            standardError: "Error: interactive login required");

        Assert.Equal("Error: interactive login required", summary);
    }

    [Fact]
    public void SummarizeProbeOutput_WhenThereIsNoOutput_DescribesTheExitCode()
    {
        var summary = AksExecCredentialDiagnostics.SummarizeProbeOutput(9, standardOutput: null, standardError: null);

        Assert.Equal("exited with code 9 and produced no output", summary);
    }

    [Fact]
    public void SummarizeProbeOutput_CollapsesWhitespaceIntoOneLine()
    {
        var summary = AksExecCredentialDiagnostics.SummarizeProbeOutput(
            1,
            standardOutput: "",
            standardError: "  Error: failed\r\n   to get   token\n\n");

        Assert.Equal("Error: failed to get token", summary);
    }

    [Fact]
    public void SummarizeProbeOutput_RedactsJwtLikeTokens()
    {
        // The message travels to the browser over the sidecar API, so a plugin that echoes a bearer
        // token back on stderr must not leak it.
        const string jwt = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signature";

        var summary = AksExecCredentialDiagnostics.SummarizeProbeOutput(
            1,
            standardOutput: "",
            standardError: $"Error: rejected token {jwt} for audience");

        Assert.NotNull(summary);
        Assert.DoesNotContain("eyJ", summary, StringComparison.Ordinal);
        Assert.Contains("[redacted]", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void SummarizeProbeOutput_TruncatesVeryLongOutput()
    {
        var summary = AksExecCredentialDiagnostics.SummarizeProbeOutput(
            1,
            standardOutput: "",
            standardError: new string('x', 5_000));

        Assert.NotNull(summary);
        Assert.True(summary.Length < 500, $"expected a truncated summary, got {summary.Length} chars");
        Assert.EndsWith("…", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAuthenticationMessage_WithDiagnostic_IncludesCauseAndRemediation()
    {
        var message = AksExecCredentialDiagnostics.BuildAuthenticationMessage(
            "Error: failed to get token: AzureCLICredential: Azure CLI not found on path");

        Assert.Contains("401", message, StringComparison.Ordinal);
        Assert.Contains("Azure CLI not found on path", message, StringComparison.Ordinal);
        Assert.Contains("az login", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAuthenticationMessage_WithoutDiagnostic_StillGivesRemediation()
    {
        var message = AksExecCredentialDiagnostics.BuildAuthenticationMessage(null);

        Assert.Contains("401", message, StringComparison.Ordinal);
        Assert.Contains("az login", message, StringComparison.Ordinal);
        Assert.DoesNotContain("credential plugin failed", message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindExecCredential_ResolvesThePluginForTheNamedContext()
    {
        var kubeConfig = BuildKubeConfig();

        var exec = AksExecCredentialDiagnostics.FindExecCredential(kubeConfig, "prd");

        Assert.NotNull(exec);
        Assert.Equal("kubelogin", exec.Command);
        Assert.Contains("get-token", exec.Arguments, StringComparer.Ordinal);
    }

    [Fact]
    public void FindExecCredential_WithoutAnExplicitContext_UsesTheCurrentContext()
    {
        var kubeConfig = BuildKubeConfig();

        var exec = AksExecCredentialDiagnostics.FindExecCredential(kubeConfig, kubeconfigContext: null);

        Assert.NotNull(exec);
        Assert.Equal("kubelogin", exec.Command);
    }

    [Fact]
    public void FindExecCredential_WhenTheContextUsesNoPlugin_ReturnsNull()
    {
        var kubeConfig = BuildKubeConfig();

        var exec = AksExecCredentialDiagnostics.FindExecCredential(kubeConfig, "local");

        Assert.Null(exec);
    }

    [Fact]
    public void FindExecCredential_WhenTheContextIsUnknown_ReturnsNull()
    {
        var kubeConfig = BuildKubeConfig();

        var exec = AksExecCredentialDiagnostics.FindExecCredential(kubeConfig, "__missing__");

        Assert.Null(exec);
    }

    [Fact]
    public async Task TryDescribeFailureAsync_WhenThePluginIsNotInstalled_SaysSo()
    {
        // Exactly the case that produced a blank namespace picker: the kubeconfig names a
        // credential plugin that cannot be launched at all.
        var kubeconfigPath = Path.Combine(Path.GetTempPath(), $"swebkit-kubeconfig-{Guid.NewGuid():N}.yaml");
        await File.WriteAllTextAsync(kubeconfigPath, $"""
            apiVersion: v1
            clusters:
            - name: prd
              cluster:
                server: https://cluster.westeurope.azmk8s.io:443
            contexts:
            - name: prd
              context:
                cluster: prd
                user: prd-user
            current-context: prd
            users:
            - name: prd-user
              user:
                exec:
                  apiVersion: client.authentication.k8s.io/v1beta1
                  command: swebkit-not-a-real-plugin-{Guid.NewGuid():N}
                  args:
                  - get-token
            """);

        try
        {
            var diagnostic = await AksExecCredentialDiagnostics.TryDescribeFailureAsync(kubeconfigPath, "prd");

            Assert.NotNull(diagnostic);
            Assert.Contains("not installed or not on PATH", diagnostic, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(kubeconfigPath);
        }
    }

    [Fact]
    public async Task TryDescribeFailureAsync_WhenTheKubeconfigIsMissing_ReturnsNullInsteadOfThrowing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"swebkit-absent-{Guid.NewGuid():N}.yaml");

        var diagnostic = await AksExecCredentialDiagnostics.TryDescribeFailureAsync(missingPath, "prd");

        Assert.Null(diagnostic);
    }

    private static K8SConfiguration BuildKubeConfig() => new()
    {
        CurrentContext = "prd",
        Contexts =
        [
            new Context { Name = "prd", ContextDetails = new ContextDetails { Cluster = "prd", User = "prd-user" } },
            new Context { Name = "local", ContextDetails = new ContextDetails { Cluster = "local", User = "local-user" } }
        ],
        Users =
        [
            new User
            {
                Name = "prd-user",
                UserCredentials = new UserCredentials
                {
                    ExternalExecution = new ExternalExecution
                    {
                        Command = "kubelogin",
                        Arguments = ["get-token", "--login", "azurecli"]
                    }
                }
            },
            new User
            {
                Name = "local-user",
                UserCredentials = new UserCredentials { Token = "static-token" }
            }
        ]
    };
}
