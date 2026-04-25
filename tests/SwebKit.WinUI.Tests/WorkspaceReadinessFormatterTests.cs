using SwebKit.WinUI.Services;

namespace SwebKit.WinUI.Tests;

public sealed class WorkspaceReadinessFormatterTests
{
    [Fact]
    public void TryFormatPipelines_ReturnsGuidance_ForConnectionValidationFailure()
    {
        var exception = new InvalidOperationException("The Azure DevOps connection test did not succeed.");

        var result = WorkspaceReadinessFormatter.TryFormatPipelines(exception, "contoso", out var state);

        Assert.True(result);
        Assert.Equal("Azure DevOps access needs attention", state.Title);
        Assert.Contains("'contoso'", state.Message, StringComparison.Ordinal);
        Assert.Contains("PAT scope", state.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryFormatObservability_ReturnsGuidance_ForAzureCredentialChainFailure()
    {
        var exception = new InvalidOperationException("DefaultAzureCredential failed to retrieve a token from the included credentials.");

        var result = WorkspaceReadinessFormatter.TryFormatObservability(exception, "prod-ai", out var state);

        Assert.True(result);
        Assert.Equal("Azure sign-in is required", state.Title);
        Assert.Contains("'prod-ai'", state.Message, StringComparison.Ordinal);
        Assert.Contains("az login", state.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryFormatObservability_ReturnsFalse_ForUnknownFailure()
    {
        var exception = new InvalidOperationException("Some other failure.");

        var result = WorkspaceReadinessFormatter.TryFormatObservability(exception, resourceName: null, out _);

        Assert.False(result);
    }
}