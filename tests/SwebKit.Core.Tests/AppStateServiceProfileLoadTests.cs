using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;
using System.Text.Json;

namespace SwebKit.Core.Tests;

public sealed class AppStateServiceProfileLoadTests
{
    [Fact]
    public async Task InitializeAsync_WithCorruptedProfilesJson_SurfacesFailureWithoutBreakingStartup()
    {
        using var appDataRoot = new TemporaryAppDataRoot();
        await File.WriteAllTextAsync(Path.Combine(appDataRoot.Root, "profiles.json"), "{ this is not valid json");

        var appState = CreateAppStateService();

        await appState.InitializeAsync();

        Assert.True(appState.IsInitialized);
        Assert.True(appState.HasProfileLoadFailure);
        Assert.True(appState.IsProfilePersistenceBlocked);
        Assert.Equal(ProfileLoadStatus.Failed, appState.ProfileLoadResult.Status);
        Assert.Contains("Saving is blocked", appState.ProfilePersistenceBlockedMessage);
    }

    [Fact]
    public async Task SaveConfigAsync_WithBlockedProfilePersistence_DoesNotOverwriteCorruptedFile()
    {
        using var appDataRoot = new TemporaryAppDataRoot();
        var profilePath = Path.Combine(appDataRoot.Root, "profiles.json");
        const string corruptedContent = "{ this is still not valid json";
        await File.WriteAllTextAsync(profilePath, corruptedContent);

        var appState = CreateAppStateService();
        await appState.InitializeAsync();

        appState.Config.Name = "ChangedInMemory";
        var persisted = await appState.SaveConfigAsync();

        Assert.False(persisted);
        Assert.Equal(corruptedContent, await File.ReadAllTextAsync(profilePath));
    }

    [Fact]
    public async Task InitializeAsync_WithLegacyProfilesJson_MigratesToSingleConfigOnSave()
    {
        using var appDataRoot = new TemporaryAppDataRoot();
        var profilePath = Path.Combine(appDataRoot.Root, "profiles.json");
        var legacyProfile = new
        {
            Config = new { Name = "Default", IsProduction = false },
            Environments = new object[]
            {
                new { Name = "Default", IsProduction = false },
                new { Name = "Production", IsProduction = true },
            },
            ActiveEnvironmentName = "Production",
            ServiceBusNamespaces = Array.Empty<object>(),
            MessageTemplates = Array.Empty<object>(),
            SchemaVersion = 1,
        };

        await File.WriteAllTextAsync(profilePath, JsonSerializer.Serialize(legacyProfile));

        var appState = CreateAppStateService();
        await appState.InitializeAsync();

        Assert.False(appState.HasProfileLoadFailure);
        Assert.True(appState.Config.IsProduction);
        Assert.Equal("Production", appState.Config.Name);

        var persisted = await appState.SaveConfigAsync();
        var savedJson = await File.ReadAllTextAsync(profilePath);

        Assert.True(persisted);
        Assert.DoesNotContain("Environments", savedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("ActiveEnvironmentName", savedJson, StringComparison.Ordinal);
    }

    private static AppStateService CreateAppStateService()
    {
        var eventBus = new AppEventBus(NullLogger<AppEventBus>.Instance);
        return new AppStateService(new ProfileRepository(), new UiStateRepository(), eventBus);
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