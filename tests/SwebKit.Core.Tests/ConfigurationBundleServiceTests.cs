using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class ConfigurationBundleServiceTests
{
    [Fact]
    public async Task ExportAndImport_RoundTripsAllPersistedStores()
    {
        using var sandbox = new TemporaryAppDataRoot();

        var profiles = new ProfileRepository();
        var uiState = new UiStateRepository();
        var userSettings = new UserSettingsRepository();
        var releases = new ReleaseRepository();
        var scheduled = new ScheduledMessageRepository();
        var appState = new AppStateService(profiles, uiState, new AppEventBus(NullLogger<AppEventBus>.Instance));
        var bundleService = new ConfigurationBundleService(profiles, uiState, userSettings, releases, scheduled, appState);

        profiles.ReplaceProfileData(new ProfileData
        {
            Config = new AppConfig { Name = "Imported", IsProduction = true },
            ServiceBusNamespaces =
            [
                new ServiceBusNamespace
                {
                    Id = Guid.NewGuid(),
                    Alias = "orders",
                    FullyQualifiedNamespace = "orders.servicebus.windows.net",
                    CredentialKey = "sb-orders"
                }
            ],
            MessageTemplates = [],
            SchemaVersion = 3
        });
        await profiles.ImportAsync(profiles.GetProfileData());

        uiState.ReplaceState(new UiState
        {
            UseDemoData = true,
            RecentCommandIds = ["aks.refresh"],
            RecentResources =
            [
                new RecentResourceEntry
                {
                    Snapshot = new WorkspaceSnapshot
                    {
                        Resource = new OperatorResourceReference
                        {
                            Area = "redis",
                            Key = "redis/cache",
                            Kind = "cache",
                            DisplayName = "cache"
                        }
                    }
                }
            ]
        });
        await uiState.SaveAsync();

        userSettings.ReplaceSettings(new UserSettings
        {
            Theme = "Studio Ledger",
            WarmupConnectionsOnStartup = false
        });
        await userSettings.SaveAsync();

        await releases.ImportAsync(new ReleaseStoreData
        {
            Releases =
            [
                new ReleaseRecord
                {
                    Id = Guid.NewGuid(),
                    Name = "Release 42",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Status = ReleaseStatus.InProgress
                }
            ]
        });

        await scheduled.ImportAsync(
        [
            new ScheduledMessageEntry
            {
                Id = Guid.NewGuid(),
                NamespaceId = Guid.NewGuid(),
                EntityPath = "orders",
                SequenceNumber = 123,
                ScheduledEnqueueTime = DateTimeOffset.UtcNow.AddMinutes(5),
                Subject = "hello"
            }
        ]);

        var exported = bundleService.Export();
        var json = bundleService.Serialize(exported);

        var importedProfiles = new ProfileRepository();
        var importedUiState = new UiStateRepository();
        var importedUserSettings = new UserSettingsRepository();
        var importedReleases = new ReleaseRepository();
        var importedScheduled = new ScheduledMessageRepository();
        var importedAppState = new AppStateService(importedProfiles, importedUiState, new AppEventBus(NullLogger<AppEventBus>.Instance));
        var importedBundleService = new ConfigurationBundleService(
            importedProfiles,
            importedUiState,
            importedUserSettings,
            importedReleases,
            importedScheduled,
            importedAppState);

        await importedBundleService.ImportAsync(importedBundleService.Deserialize(json));

        Assert.Equal("Imported", importedProfiles.Config.Name);
        Assert.True(importedProfiles.Config.IsProduction);
        Assert.Single(importedProfiles.ServiceBusNamespaces);
        Assert.True(importedUiState.State.UseDemoData);
        Assert.Equal("Studio Ledger", importedUserSettings.Settings.Theme);
        Assert.False(importedUserSettings.Settings.WarmupConnectionsOnStartup);
        Assert.Single(importedReleases.AllReleases);
        Assert.Single(importedScheduled.All);
        Assert.True(importedAppState.UseDemoData);
        Assert.True(File.Exists(AppDataPaths.ProfilesJson));
        Assert.True(File.Exists(AppDataPaths.UiStateJson));
        Assert.True(File.Exists(AppDataPaths.UserSettingsJson));
        Assert.True(File.Exists(AppDataPaths.ReleasesJson));
        Assert.True(File.Exists(AppDataPaths.ScheduledMessagesJson));
    }

    [Fact]
    public void Deserialize_RejectsUnsupportedSchemaVersion()
    {
        using var sandbox = new TemporaryAppDataRoot();

        var profiles = new ProfileRepository();
        var uiState = new UiStateRepository();
        var userSettings = new UserSettingsRepository();
        var releases = new ReleaseRepository();
        var scheduled = new ScheduledMessageRepository();
        var appState = new AppStateService(profiles, uiState, new AppEventBus(NullLogger<AppEventBus>.Instance));
        var bundleService = new ConfigurationBundleService(profiles, uiState, userSettings, releases, scheduled, appState);

        var ex = Assert.Throws<InvalidOperationException>(() => bundleService.Deserialize("""
            {
              "schemaVersion": 99
            }
            """));

        Assert.Contains("Unsupported configuration bundle schema version", ex.Message, StringComparison.Ordinal);
    }

    private sealed class TemporaryAppDataRoot : IDisposable
    {
        private readonly string? _previousRoot;

        public TemporaryAppDataRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "SwebKit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            _previousRoot = Environment.GetEnvironmentVariable("SWEBKIT_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", _previousRoot);
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}