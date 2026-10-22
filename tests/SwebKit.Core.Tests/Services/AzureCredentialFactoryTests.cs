using Azure.Identity;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests.Services;

/// <summary>
/// The parameterless credential is a shared instance (one chain resolution for the whole app);
/// <see cref="AzureCredentialFactory.Reset"/> must drop it so an identity change takes effect.
/// Constructing a <see cref="DefaultAzureCredential"/> does no I/O — the chain only runs on the
/// first token request — so these tests never touch Azure.
/// </summary>
public class AzureCredentialFactoryTests : IDisposable
{
    public AzureCredentialFactoryTests() => AzureCredentialFactory.Reset();

    public void Dispose()
    {
        AzureCredentialFactory.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CreateDefault_ReturnsTheSameInstance_OnRepeatedCalls()
    {
        var first = AzureCredentialFactory.CreateDefault();
        var second = AzureCredentialFactory.CreateDefault();

        Assert.IsType<DefaultAzureCredential>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task CreateDefault_ConcurrentFirstCalls_ShareOneInstance()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(AzureCredentialFactory.CreateDefault)));

        Assert.Single(results.Distinct());
    }

    [Fact]
    public void Reset_MakesTheNextCallBuildAFreshCredential()
    {
        var before = AzureCredentialFactory.CreateDefault();

        AzureCredentialFactory.Reset();
        var after = AzureCredentialFactory.CreateDefault();

        Assert.NotSame(before, after);
        Assert.Same(after, AzureCredentialFactory.CreateDefault());
    }

    [Fact]
    public void CreateDefault_WithOptions_IsNeverCached()
    {
        var options = new DefaultAzureCredentialOptions { ExcludeWorkloadIdentityCredential = true };

        var a = AzureCredentialFactory.CreateDefault(options);
        var b = AzureCredentialFactory.CreateDefault(options);

        Assert.NotSame(a, b);
        Assert.NotSame(AzureCredentialFactory.CreateDefault(), a);
    }
}
