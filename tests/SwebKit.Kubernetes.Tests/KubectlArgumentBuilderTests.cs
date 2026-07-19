using SwebKit.Kubernetes.AksClient;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwebKit.Kubernetes.Tests;

public class KubectlArgumentBuilderTests
{
    [Fact]
    public void ValidateKubernetesName_ValidDnsName_ReturnsValue()
    {
        var result = KubectlArgumentBuilder.ValidateKubernetesName("my-pod-123", "paramName");
        Assert.Equal("my-pod-123", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UPPERCASE")]
    [InlineData("pod_with_underscore")]
    [InlineData("pod;rm -rf /")]
    [InlineData("pod&whoami")]
    [InlineData("pod|cat /etc/passwd")]
    [InlineData("pod`whoami`")]
    [InlineData("pod$(whoami)")]
    [InlineData("pod\nmalicious")]
    [InlineData("-leading-dash")]
    [InlineData("trailing-dash-")]
    public void ValidateKubernetesName_InvalidNames_Throws(string invalid)
    {
        Assert.Throws<ArgumentException>(
            () => KubectlArgumentBuilder.ValidateKubernetesName(invalid, "test"));
    }

    [Fact]
    public void ValidateKubernetesName_TooLong_Throws()
    {
        var longName = new string('a', 254);
        Assert.Throws<ArgumentException>(
            () => KubectlArgumentBuilder.ValidateKubernetesName(longName, "test"));
    }

    [Fact]
    public void WithGlobalFlags_AddsKubeconfigAndContext()
    {
        var args = new KubectlArgumentBuilder()
            .WithGlobalFlags("/path/to/kubeconfig", "my-context")
            .Build();

        Assert.Equal(new[] { "--kubeconfig", "/path/to/kubeconfig", "--context", "my-context" }, args);
    }

    [Fact]
    public void WithGlobalFlags_NullPathAndContext_ReturnsEmpty()
    {
        var args = new KubectlArgumentBuilder()
            .WithGlobalFlags(null, null)
            .Build();

        Assert.Empty(args);
    }

    [Fact]
    public void WithGlobalFlags_EmptyStrings_ReturnsEmpty()
    {
        var args = new KubectlArgumentBuilder()
            .WithGlobalFlags("", "  ")
            .Build();

        Assert.Empty(args);
    }

    [Fact]
    public void WithHelmGlobalFlags_UsesKubeContextFlag()
    {
        var args = new KubectlArgumentBuilder()
            .WithHelmGlobalFlags("/path/to/kubeconfig", "my-context")
            .Build();

        Assert.Equal(new[] { "--kubeconfig", "/path/to/kubeconfig", "--kube-context", "my-context" }, args);
    }

    [Fact]
    public void PortForward_BuildsCorrectArgs()
    {
        var args = new KubectlArgumentBuilder()
            .WithGlobalFlags("/path/to/kubeconfig", "ctx")
            .PortForward("default", "my-pod", 8080, 80)
            .Build();

        Assert.Equal(
            new[] { "--kubeconfig", "/path/to/kubeconfig", "--context", "ctx",
                    "port-forward", "my-pod", "8080:80", "-n", "default" },
            args);
    }

    [Fact]
    public void PortForward_InvalidNamespace_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new KubectlArgumentBuilder().PortForward("bad;ns", "pod", 80, 80));
    }

    [Fact]
    public void ExecInteractive_BuildsCorrectArgs()
    {
        var args = new KubectlArgumentBuilder()
            .ExecInteractive("default", "my-pod", "my-container")
            .Build();

        Assert.Equal(
            new[] { "exec", "-it", "my-pod", "-n", "default", "-c", "my-container" },
            args);
    }

    [Fact]
    public void ExecInteractive_InvalidPod_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new KubectlArgumentBuilder().ExecInteractive("default", "bad;pod", "container"));
    }

    [Fact]
    public void Apply_BuildsCorrectArgs()
    {
        var args = new KubectlArgumentBuilder()
            .Apply("/tmp/manifest.yaml", "my-namespace")
            .Build();

        Assert.Equal(
            new[] { "apply", "-f", "/tmp/manifest.yaml", "--namespace", "my-namespace" },
            args);
    }

    [Fact]
    public void DryRunServer_AddsFlag()
    {
        var args = new KubectlArgumentBuilder()
            .Apply("/tmp/file.yaml", "ns")
            .DryRunServer()
            .Build();

        Assert.Contains("--dry-run=server", args);
    }

    [Fact]
    public void WithToken_AddsTokenFlag()
    {
        var args = new KubectlArgumentBuilder()
            .WithToken("my-token-value")
            .Build();

        Assert.Equal(new[] { "--token", "my-token-value" }, args);
    }

    [Fact]
    public void Add_RawString_AddedAsIs()
    {
        var args = new KubectlArgumentBuilder()
            .Add("custom-arg")
            .Build();

        Assert.Equal(new[] { "custom-arg" }, args);
    }

    [Fact]
    public void AddRange_AddsMultipleArgs()
    {
        var args = new KubectlArgumentBuilder()
            .AddRange(new[] { "a", "b", "c" })
            .Build();

        Assert.Equal(new[] { "a", "b", "c" }, args);
    }

    [Fact]
    public void Build_ReturnsReadOnlyList()
    {
        var args = new KubectlArgumentBuilder()
            .Add("test")
            .Build();

        Assert.IsAssignableFrom<IReadOnlyList<string>>(args);
    }

    [Fact]
    public void FullApplyWithToken_BuildsCorrectOrder()
    {
        var args = new KubectlArgumentBuilder()
            .WithGlobalFlags("/kubeconfig", "ctx")
            .Apply("/tmp/manifest.yaml", "ns")
            .WithToken("tok123")
            .Build();

        Assert.Equal(
            new[] { "--kubeconfig", "/kubeconfig", "--context", "ctx",
                    "apply", "-f", "/tmp/manifest.yaml", "--namespace", "ns",
                    "--token", "tok123" },
            args);
    }

    [Fact]
    public void WindowsTerminalStartInfo_PreservesArgumentsWithoutShellConcatenation()
    {
        string[] args = ["--kubeconfig", @"C:\cluster configs\prod & whoami", "--context", "prod"];

        var startInfo = KubectlShellLauncher.CreateWindowsTerminalStartInfo(args);

        Assert.False(startInfo.UseShellExecute);
        Assert.Equal("wt.exe", startInfo.FileName);
        Assert.Equal(new[] { "kubectl.exe", "--kubeconfig", @"C:\cluster configs\prod & whoami", "--context", "prod" }, startInfo.ArgumentList);
        Assert.Empty(startInfo.Arguments);
    }

    [Fact]
    public void PowerShellStartInfo_EncodesAllDynamicArguments()
    {
        const string hostilePath = @"C:\cluster configs\prod & whoami";
        string[] args = ["--kubeconfig", hostilePath, "--context", "prod; Write-Host injected"];

        var startInfo = KubectlShellLauncher.CreatePowerShellStartInfo(args);

        Assert.True(startInfo.UseShellExecute);
        Assert.DoesNotContain(hostilePath, startInfo.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host injected", startInfo.Arguments, StringComparison.Ordinal);

        var encodedCommand = startInfo.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(encodedCommand));
        var payload = Regex.Match(script, "FromBase64String\\('(?<payload>[A-Za-z0-9+/=]+)'\\)").Groups["payload"].Value;
        var decodedArgs = JsonSerializer.Deserialize<string[]>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));

        Assert.Equal(args, decodedArgs);
    }
}
