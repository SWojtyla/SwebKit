using Microsoft.AspNetCore.Http;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

public class SystemEndpointsTests
{
    [Fact]
    public void GetHealth_ReturnsOkStatusAndAVersion()
    {
        var result = Assert.IsAssignableFrom<IValueHttpResult>(SystemEndpoints.GetHealth());
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);

        Assert.Contains("\"status\":\"ok\"", json);
        Assert.Contains("version", json);
    }

    [Fact]
    public void GetDemoMode_ReflectsTheServicesCurrentState()
    {
        var demo = new DemoModeService { IsDemoMode = true };

        var result = Assert.IsAssignableFrom<IValueHttpResult>(SystemEndpoints.GetDemoMode(demo));
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);

        Assert.Contains("\"isDemoMode\":true", json);
    }

    [Fact]
    public void SetDemoMode_TogglesTheServiceAndReturnsTheNewState()
    {
        var demo = new DemoModeService { IsDemoMode = false };

        var result = Assert.IsAssignableFrom<IValueHttpResult>(SystemEndpoints.SetDemoMode(demo, true));

        Assert.True(demo.IsDemoMode);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"isDemoMode\":true", json);
    }
}
