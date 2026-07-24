using SwebKit.Core.Domain;
using Xunit;

namespace SwebKit.Agents.Tests;

public class AgentConfigMigrationTests
{
    [Fact]
    public void Migrate_NoProfiles_NoModelOverride_CreatesLmStudioDefault()
    {
        var config = new AgentConfig();

        config.Migrate();

        Assert.Single(config.Profiles);
        Assert.Equal(ProviderKind.LmStudio, config.Profiles[0].Provider);
        Assert.Equal(config.Profiles[0].Id, config.ActiveProfileId);
    }

    [Fact]
    public void Migrate_NoProfiles_WithModelOverride_CreatesMistralProfile()
    {
        var config = new AgentConfig
        {
            ModelOverride = "mistral-large-latest"
        };

        config.Migrate();

        Assert.Single(config.Profiles);
        var profile = config.Profiles[0];
        Assert.Equal(ProviderKind.Mistral, profile.Provider);
        Assert.Equal("mistral-large-latest", profile.Model);
        Assert.Equal("SwebKit-Agent:Mistral-ApiKey", profile.CredentialKey);
        Assert.Equal(config.Profiles[0].Id, config.ActiveProfileId);
    }

    [Fact]
    public void Migrate_WithExistingProfiles_PreservesProfiles()
    {
        var config = new AgentConfig();
        var profile = AgentProfilePresets.LmStudio("test-model");
        config.Profiles.Add(profile);
        config.ActiveProfileId = profile.Id;

        config.Migrate();

        Assert.Single(config.Profiles);
        Assert.Equal(profile.Id, config.ActiveProfileId);
    }

    [Fact]
    public void Migrate_WithExistingProfiles_InvalidActiveId_FixesActiveId()
    {
        var config = new AgentConfig();
        var profile = AgentProfilePresets.LmStudio();
        config.Profiles.Add(profile);
        config.ActiveProfileId = "nonexistent";

        config.Migrate();

        Assert.Equal(profile.Id, config.ActiveProfileId);
    }

    [Fact]
    public void Migrate_IsIdempotent()
    {
        var config = new AgentConfig();

        config.Migrate();
        var firstCount = config.Profiles.Count;
        var firstActiveId = config.ActiveProfileId;

        config.Migrate();

        Assert.Equal(firstCount, config.Profiles.Count);
        Assert.Equal(firstActiveId, config.ActiveProfileId);
    }

    [Fact]
    public void Migrate_EmptyActiveId_WithProfiles_SetsFirstProfile()
    {
        var config = new AgentConfig();
        var p1 = AgentProfilePresets.LmStudio();
        var p2 = AgentProfilePresets.Mistral();
        config.Profiles.Add(p1);
        config.Profiles.Add(p2);
        config.ActiveProfileId = "";

        config.Migrate();

        Assert.Equal(p1.Id, config.ActiveProfileId);
    }

    [Fact]
    public void GetActiveProfile_AfterMigration_ReturnsActiveProfile()
    {
        var config = new AgentConfig
        {
            ModelOverride = "mistral-small-latest"
        };

        var profile = config.GetActiveProfile();

        Assert.NotNull(profile);
        Assert.Equal(ProviderKind.Mistral, profile.Provider);
        Assert.Equal("mistral-small-latest", profile.Model);
    }

    [Fact]
    public void GetActiveProfile_NoProfiles_ReturnsLmStudioDefault()
    {
        var config = new AgentConfig();

        var profile = config.GetActiveProfile();

        Assert.NotNull(profile);
        Assert.Equal(ProviderKind.LmStudio, profile.Provider);
    }
}
