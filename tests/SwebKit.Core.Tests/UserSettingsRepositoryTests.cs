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
}