using SwebKit.App.Services;

namespace SwebKit.App.Tests;

public class SingleInstanceNamingTests
{
    [Fact]
    public void ComposeMutexName_UsesSessionLocalNamespace_AndIsScopedPerUser()
    {
        var name = SingleInstanceNaming.ComposeMutexName("S-1-5-21-1");

        Assert.StartsWith(@"Local\", name);
        Assert.Contains(SingleInstanceNaming.BaseName, name);
        Assert.EndsWith("S-1-5-21-1", name);
    }

    [Fact]
    public void ComposePipeName_HasNoNamespacePrefix_AndIsScopedPerUser()
    {
        var name = SingleInstanceNaming.ComposePipeName("S-1-5-21-1");

        Assert.DoesNotContain(@"\", name);
        Assert.Contains(SingleInstanceNaming.BaseName, name);
        Assert.EndsWith("S-1-5-21-1", name);
    }

    [Fact]
    public void Compose_IsDeterministic_ForSameScope()
    {
        Assert.Equal(
            SingleInstanceNaming.ComposeMutexName("scope-a"),
            SingleInstanceNaming.ComposeMutexName("scope-a"));
        Assert.Equal(
            SingleInstanceNaming.ComposePipeName("scope-a"),
            SingleInstanceNaming.ComposePipeName("scope-a"));
    }

    [Fact]
    public void Compose_DiffersByScope()
    {
        Assert.NotEqual(
            SingleInstanceNaming.ComposeMutexName("scope-a"),
            SingleInstanceNaming.ComposeMutexName("scope-b"));
        Assert.NotEqual(
            SingleInstanceNaming.ComposePipeName("scope-a"),
            SingleInstanceNaming.ComposePipeName("scope-b"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Compose_FallsBackToDefault_ForBlankScope(string? scope)
    {
        Assert.EndsWith("default", SingleInstanceNaming.ComposeMutexName(scope!));
        Assert.EndsWith("default", SingleInstanceNaming.ComposePipeName(scope!));
    }
}
