using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

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