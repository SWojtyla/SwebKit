using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;
using SwebKit.Kubernetes.AksClient;

namespace SwebKit.Kubernetes.Tests;

public sealed class AksClientFactoryTests
{
    private readonly AksClientFactory _factory = new();

    [Fact]
    public void Create_WithNullContextAndPath_ReturnsNonNullClient()
    {
        using var kubeconfig = CreateTempKubeconfig();

        // The factory should construct the client; connection is lazy so no network call happens here.
        var client = _factory.Create(context: null, kubeconfigPath: kubeconfig.Path);

        Assert.NotNull(client);
        Assert.IsAssignableFrom<IAksClient>(client);
    }

    [Fact]
    public void Create_ReturnsDifferentInstancePerCall()
    {
        using var kubeconfig = CreateTempKubeconfig();

        var a = _factory.Create(null, kubeconfig.Path);
        var b = _factory.Create(null, kubeconfig.Path);

        Assert.NotSame(a, b);
    }

    private static TempKubeconfig CreateTempKubeconfig()
    {
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "config");
        var kubeconfig = string.Join(
                "\n",
                [
                        "apiVersion: v1",
                                "kind: Config",
                                "clusters:",
                                "- cluster:",
                                "    server: https://cluster.region.azmk8s.io:443",
                                "  name: test-cluster",
                                "contexts:",
                                "- context:",
                                "    cluster: test-cluster",
                                "    user: test-user",
                                "  name: test-context",
                                "current-context: test-context",
                                "preferences: {}",
                                "users:",
                                "- name: test-user",
                                "  user:",
                                "    exec:",
                                "      apiVersion: client.authentication.k8s.io/v1beta1",
                                "      command: __definitely_missing_exec_command__"
                ]) + "\n";

        File.WriteAllText(path, kubeconfig);
        return new TempKubeconfig(directory, path);
    }

    private sealed class TempKubeconfig(DirectoryInfo directory, string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            if (directory.Exists)
                directory.Delete(recursive: true);
        }
    }
}
