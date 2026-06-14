using Bunit;
using SwebKit.App.Components.ApiClient;
using SwebKit.Core.Domain;

namespace SwebKit.App.Tests;

public sealed class PostRequestCaptureBuilderTests : TestContext
{
    [Fact]
    public void RendersSharedPrimitivesWithExistingClasses()
    {
        var request = new HttpRequestEntry
        {
            CaptureRules =
            [
                new CaptureRule
                {
                    Id = "rule-1",
                    Source = CaptureSource.BodyJsonPath,
                    JsonPath = "$.token",
                    TargetVariable = "token",
                    TargetScope = "collection",
                    IsEnabled = true,
                }
            ]
        };

        var cut = RenderComponent<PostRequestCaptureBuilder>(parameters => parameters
            .Add(component => component.Request, request)
            .Add(component => component.AvailableEnvironments, TestEnvironments()));

        var addButton = cut.Find("button.capture-builder__add-btn");
        var deleteButton = cut.Find("button.capture-builder__rule-delete");
        var selects = cut.FindAll("select");

        Assert.Contains("app-button", addButton.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-button--secondary", addButton.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-button--small", addButton.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-icon-button", deleteButton.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-icon-button--ghost", deleteButton.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-icon-button--small", deleteButton.ClassName, StringComparison.Ordinal);
        Assert.Equal("Remove capture rule", deleteButton.GetAttribute("aria-label"));
        Assert.Equal(2, selects.Count);
        Assert.Contains("app-select", selects[0].ClassName, StringComparison.Ordinal);
        Assert.Contains("capture-builder__source-type", selects[0].ClassName, StringComparison.Ordinal);
        Assert.Contains("app-select", selects[1].ClassName, StringComparison.Ordinal);
        Assert.Contains("capture-builder__scope", selects[1].ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void PreservesCaptureRuleEditingBehavior()
    {
        var changedCount = 0;
        var request = new HttpRequestEntry();
        var cut = RenderComponent<PostRequestCaptureBuilder>(parameters => parameters
            .Add(component => component.Request, request)
            .Add(component => component.AvailableEnvironments, TestEnvironments())
            .Add(component => component.OnChanged, () => changedCount++));

        cut.Find("button.capture-builder__add-btn").Click();

        Assert.Single(request.CaptureRules);
        Assert.Equal(CaptureSource.BodyJsonPath, request.CaptureRules[0].Source);
        Assert.Equal("collection", request.CaptureRules[0].TargetScope);
        Assert.True(request.CaptureRules[0].IsEnabled);
        Assert.Equal(1, changedCount);

        cut.Find("input.capture-builder__rule-enabled").Change(false);
        Assert.False(request.CaptureRules[0].IsEnabled);
        Assert.Equal(2, changedCount);

        cut.Find("select.capture-builder__source-type").Change(nameof(CaptureSource.ResponseHeader));
        Assert.Equal(CaptureSource.ResponseHeader, request.CaptureRules[0].Source);
        Assert.Equal(3, changedCount);
        Assert.Equal("X-Auth-Token", cut.Find("input.capture-builder__expr").GetAttribute("placeholder"));

        cut.Find("select.capture-builder__scope").Change("env-prod");
        Assert.Equal("env-prod", request.CaptureRules[0].TargetScope);
        Assert.Equal(4, changedCount);

        cut.Find("button.capture-builder__rule-delete").Click();
        Assert.Empty(request.CaptureRules);
        Assert.Equal(5, changedCount);
    }

    private static ApiEnvironment[] TestEnvironments() =>
    [
        new ApiEnvironment { Id = "env-prod", Name = "Production" }
    ];
}