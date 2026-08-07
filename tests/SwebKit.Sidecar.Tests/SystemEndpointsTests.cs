using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
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
        using var sandbox = new AppDataSandbox();
        var appState = BuildAppState();

        var result = Assert.IsAssignableFrom<IValueHttpResult>(SystemEndpoints.GetDemoMode(appState));
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);

        Assert.Contains("\"isDemoMode\":false", json);
    }

    [Fact]
    public async Task SetDemoMode_TogglesTheServiceAndReturnsTheNewState()
    {
        using var sandbox = new AppDataSandbox();
        var appState = BuildAppState();

        var result = await SystemEndpoints.SetDemoMode(new DemoModeService(), appState, true);

        var json = System.Text.Json.JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.Contains("\"isDemoMode\":true", json);
    }

    private static AppStateService BuildAppState()
    {
        var profiles = new ProfileRepository();
        var uiState = new UiStateRepository();
        var events = new AppEventBus(NullLogger<AppEventBus>.Instance);
        return new AppStateService(profiles, uiState, events);
    }
}
