using SwebKit.App.Components.Aks;

namespace SwebKit.App.Tests;

public sealed class PortForwardSessionsPanelTests
{
    [Theory]
    [InlineData(80, true)]
    [InlineData(443, true)]
    [InlineData(8080, true)]
    [InlineData(8443, true)]
    [InlineData(5432, false)]
    [InlineData(3306, false)]
    [InlineData(27017, false)]
    [InlineData(6379, false)]
    public void IsHttpPort_ReturnsExpected(int port, bool expected)
    {
        Assert.Equal(expected, PortForwardSessionsPanel.IsHttpPort(port));
    }
}
