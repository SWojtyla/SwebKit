using SwebKit.App.Components.ApiClient;

namespace SwebKit.App.Tests;

public sealed class ApiClientOpenTabTests
{
    [Fact]
    public void NewTab_HasEmptyRequestIdAndNoRequestReference()
    {
        var tab = new ApiClientOpenTab();

        Assert.Equal(string.Empty, tab.RequestId);
        Assert.Null(tab.Request);
    }
}
