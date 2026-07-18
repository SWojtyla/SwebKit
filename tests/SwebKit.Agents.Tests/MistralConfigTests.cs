using Xunit;

namespace SwebKit.Agents.Tests;

public class MistralConfigTests
{
    [Fact]
    public void Defaults_MatchExpectedValues()
    {
        var config = new MistralConfig();

        Assert.Equal(string.Empty, config.ApiKey);
        Assert.Equal("https://api.mistral.ai/v1", config.ApiEndpoint);
        Assert.Equal("mistral-medium-latest", config.Model);
        Assert.Equal(2048, config.MaxTokens);
        Assert.Equal(0.7, config.Temperature);
    }

    [Fact]
    public void Properties_AreMutable()
    {
        var config = new MistralConfig
        {
            ApiKey = "secret",
            ApiEndpoint = "https://example.test/v1",
            Model = "mistral-large-latest",
            MaxTokens = 512,
            Temperature = 0.2,
        };

        Assert.Equal("secret", config.ApiKey);
        Assert.Equal("https://example.test/v1", config.ApiEndpoint);
        Assert.Equal("mistral-large-latest", config.Model);
        Assert.Equal(512, config.MaxTokens);
        Assert.Equal(0.2, config.Temperature);
    }
}
