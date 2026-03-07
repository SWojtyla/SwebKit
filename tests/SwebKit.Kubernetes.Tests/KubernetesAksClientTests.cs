using SwebKit.Kubernetes.AksClient;

namespace SwebKit.Kubernetes.Tests;

public class KubernetesAksClientTests
{
    [Fact]
    public void Ctor_InvalidContext_ThrowsHelpfulException()
    {
        // The scaffold currently binds directly to kubeconfig; invalid context should fail fast.
        var ex = Record.Exception(() => new KubernetesAksClient("__this-context-should-not-exist__"));

        Assert.NotNull(ex);
    }
}
