using SwebKit.Core.Domain;
using Xunit;

namespace SwebKit.Agents.Tests;

public class AgentProfilePresetsTests
{
    [Fact]
    public void LmStudio_Preset_HasExpectedDefaults()
    {
        var profile = AgentProfilePresets.LmStudio();

        Assert.Equal(ProviderKind.LmStudio, profile.Provider);
        Assert.Equal("http://localhost:1234/v1", profile.BaseUrl);
        Assert.Null(profile.CredentialKey);
        Assert.False(profile.RequiresApiKey);
        Assert.Equal(120, profile.TimeoutSeconds);
    }

    [Fact]
    public void Mistral_Preset_HasExpectedDefaults()
    {
        var profile = AgentProfilePresets.Mistral();

        Assert.Equal(ProviderKind.Mistral, profile.Provider);
        Assert.Equal("https://api.mistral.ai/v1", profile.BaseUrl);
        Assert.Equal("mistral-medium-latest", profile.Model);
        Assert.Equal("SwebKit-Agent:Mistral-ApiKey", profile.CredentialKey);
        Assert.True(profile.RequiresApiKey);
        Assert.Equal(60, profile.TimeoutSeconds);
    }

    [Fact]
    public void OpenAiCompatible_Preset_HasUserValues()
    {
        var profile = AgentProfilePresets.OpenAiCompatible("https://api.example.com/v1", "gpt-4o", "my-key");

        Assert.Equal(ProviderKind.OpenAiCompatible, profile.Provider);
        Assert.Equal("https://api.example.com/v1", profile.BaseUrl);
        Assert.Equal("gpt-4o", profile.Model);
        Assert.Equal("my-key", profile.CredentialKey);
        Assert.True(profile.RequiresApiKey);
    }

    [Fact]
    public void LmStudio_Preset_WithCustomModel_SetsModel()
    {
        var profile = AgentProfilePresets.LmStudio("llama-3.1-8b-instruct");

        Assert.Equal("llama-3.1-8b-instruct", profile.Model);
    }

    [Fact]
    public void Mistral_Preset_WithCustomModel_SetsModel()
    {
        var profile = AgentProfilePresets.Mistral("mistral-large-latest");

        Assert.Equal("mistral-large-latest", profile.Model);
    }
}
