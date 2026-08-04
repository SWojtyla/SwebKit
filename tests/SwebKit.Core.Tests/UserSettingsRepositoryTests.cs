using SwebKit.Core.Configuration;

namespace SwebKit.Core.Tests;

public sealed class UserSettingsRepositoryTests
{
    [Fact]
    public async Task SaveAsync_CreatesBackupFile()
    {
        using var _ = new AppDataSandbox();
        var repository = new UserSettingsRepository();

        repository.Settings.Theme = "light-coral-studio";
        await repository.SaveAsync();

        Assert.True(File.Exists(AppDataPaths.UserSettingsJson));
        Assert.True(File.Exists($"{AppDataPaths.UserSettingsJson}.bak"));
    }

    [Fact]
    public async Task LoadAsync_WithCorruptedPrimaryAndBackup_RecoversTheme()
    {
        using var _ = new AppDataSandbox();
        var writer = new UserSettingsRepository();
        writer.Settings.Theme = "light-coral-studio";
        await writer.SaveAsync();

        var backupPath = $"{AppDataPaths.UserSettingsJson}.bak";
        Assert.True(File.Exists(backupPath));

        await File.WriteAllTextAsync(AppDataPaths.UserSettingsJson, "{ invalid json");

        var reader = new UserSettingsRepository();
        await reader.LoadAsync();

        Assert.Equal("light-coral-studio", reader.Settings.Theme);
    }

    [Fact]
    public void DefaultSettings_HasApiClientRequestTabsDisabled()
    {
        var settings = new UserSettings();

        Assert.False(settings.ApiClientRequestTabs);
    }

    [Fact]
    public void DefaultSettings_HasFontSizeAndDensityDefaults()
    {
        var settings = new UserSettings();

        Assert.Equal("medium", settings.FontSize);
        Assert.Equal("comfortable", settings.Density);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsFontSizeDensityAndLogging()
    {
        using var _ = new AppDataSandbox();
        var writer = new UserSettingsRepository();
        writer.Settings.FontSize = "large";
        writer.Settings.Density = "compact";
        writer.Settings.Logging.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Debug;
        writer.Settings.PinnedPortForwards["test"] =
        [
            new("local", "default", "app=test", 8080, 18080, DateTimeOffset.UtcNow),
        ];

        await writer.SaveAsync();

        var reader = new UserSettingsRepository();
        await reader.LoadAsync();

        Assert.Equal("large", reader.Settings.FontSize);
        Assert.Equal("compact", reader.Settings.Density);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Debug, reader.Settings.Logging.MinimumLevel);
        Assert.Single(reader.Settings.PinnedPortForwards);
        Assert.Single(reader.Settings.PinnedPortForwards["test"]);
    }

    [Fact]
    public async Task LoadAsync_WithMinimalJson_DefaultsNewFieldsAndPinnedPortForwards()
    {
        using var _ = new AppDataSandbox();
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.UserSettingsJson, "{\"theme\":\"dark\"}");

        var reader = new UserSettingsRepository();
        await reader.LoadAsync();

        Assert.Equal("dark", reader.Settings.Theme);
        Assert.Equal("medium", reader.Settings.FontSize);
        Assert.Equal("comfortable", reader.Settings.Density);
        Assert.NotNull(reader.Settings.PinnedPortForwards);
        Assert.Empty(reader.Settings.PinnedPortForwards);
    }

    [Fact]
    public async Task SaveAsync_WritesLoggingMinimumLevelAsString()
    {
        using var _ = new AppDataSandbox();
        var repository = new UserSettingsRepository();
        repository.Settings.Logging.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Information;

        await repository.SaveAsync();

        var json = await File.ReadAllTextAsync(AppDataPaths.UserSettingsJson);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var level = doc.RootElement.GetProperty("logging").GetProperty("minimumLevel").GetString();
        Assert.Equal("Information", level);
    }
}