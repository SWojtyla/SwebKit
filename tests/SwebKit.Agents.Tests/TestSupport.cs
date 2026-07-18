using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tests;

/// <summary>
/// Shared helpers for constructing an <see cref="AppStateService"/> around a
/// synthetic <see cref="AppConfig"/> so agent tools can be exercised in isolation.
/// </summary>
internal static class TestSupport
{
    public static AppStateService CreateAppState(
        Action<AppConfig>? configure = null,
        IReadOnlyList<ServiceBusNamespace>? serviceBusNamespaces = null)
    {
        var config = new AppConfig { Name = "Test" };
        configure?.Invoke(config);

        var repository = new ProfileRepository();
        repository.ReplaceProfileData(new ProfileData
        {
            Config = config,
            ServiceBusNamespaces = serviceBusNamespaces is null
                ? []
                : [.. serviceBusNamespaces],
        });

        return new AppStateService(repository, new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance));
    }
}
