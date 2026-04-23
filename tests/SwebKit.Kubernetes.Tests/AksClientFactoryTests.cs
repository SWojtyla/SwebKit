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
        // The factory should construct the client; connection is lazy so no network call happens here.
        var client = _factory.Create(context: null, kubeconfigPath: null);

        Assert.NotNull(client);
        Assert.IsAssignableFrom<IAksClient>(client);
    }

    [Fact]
    public void Create_ReturnsDifferentInstancePerCall()
    {
        var a = _factory.Create(null, null);
        var b = _factory.Create(null, null);

        Assert.NotSame(a, b);
    }
}
