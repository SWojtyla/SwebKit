using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Redis;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public class RedisKeyDetailTests : TestContext
{
    public RedisKeyDetailTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);

        Services.AddSingleton<INotificationService>(new NotificationService(new UiStateRepository()));
    }

    [Fact]
    public void StringValue_RendersFullValueWithoutTruncation()
    {
        var longValue = new string('a', 600) + "tail-token";

        var cut = RenderComponent<RedisKeyDetail>(parameters => parameters
            .Add(component => component.SelectedKey, "app:large")
            .Add(component => component.KeyInfo, new RedisKeyInfo
            {
                Key = "app:large",
                Type = "string",
            })
            .Add(component => component.StringValue, longValue));

        var value = cut.Find("pre.blob").TextContent;

        Assert.EndsWith("tail-token", value, StringComparison.Ordinal);
        Assert.Equal(longValue, value);
        Assert.Contains("Copy Value", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void CopyValue_CopiesRawStringValue()
    {
        const string rawJson = "{\"b\":1,\"a\":2}";

        var cut = RenderComponent<RedisKeyDetail>(parameters => parameters
            .Add(component => component.SelectedKey, "app:json")
            .Add(component => component.KeyInfo, new RedisKeyInfo
            {
                Key = "app:json",
                Type = "string",
            })
            .Add(component => component.StringValue, rawJson));

        cut.FindAll("fluent-button")
            .First(button => button.TextContent.Contains("Copy Value", StringComparison.Ordinal))
            .Click();

        var invocation = JSInterop.VerifyInvoke("navigator.clipboard.writeText");
        Assert.Equal(rawJson, invocation.Arguments[0]);
    }
}