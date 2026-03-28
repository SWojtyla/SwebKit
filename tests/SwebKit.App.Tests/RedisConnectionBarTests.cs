using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Redis;
using SwebKit.Core.Domain;

namespace SwebKit.App.Tests;

public class RedisConnectionBarTests : TestContext
{
    public RedisConnectionBarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void RedisConnectionBar_NotConnected_HidesConnectionLabel()
    {
        var cut = RenderComponent<RedisConnectionBar>(ps => ps
            .Add(p => p.IsConnected, false));

        Assert.Empty(cut.FindAll(".connection"));
    }

    [Fact]
    public void RedisConnectionBar_Connected_ShowsConnectionLabel()
    {
        var cut = RenderComponent<RedisConnectionBar>(ps => ps
            .Add(p => p.IsConnected, true)
            .Add(p => p.ConnectionLabel, "db:0 — 42 keys"));

        Assert.Contains("db:0 — 42 keys", cut.Find(".connection").TextContent);
    }

    [Fact]
    public void RedisConnectionBar_MultipleCacheEntries_ShowsCacheSelector()
    {
        var entries = new List<RedisCacheEntry>
        {
            new() { Id = "a", DisplayName = "Redis Dev" },
            new() { Id = "b", DisplayName = "Redis Prod" },
        };

        var cut = RenderComponent<RedisConnectionBar>(ps => ps
            .Add(p => p.CacheEntries, entries)
            .Add(p => p.ActiveCacheEntry, entries[0])
            .Add(p => p.SelectedCacheId, "a"));

        Assert.NotEmpty(cut.FindAll("select.cache-selector"));
    }

    [Fact]
    public void RedisConnectionBar_SingleEntry_HidesCacheSelector()
    {
        var entries = new List<RedisCacheEntry>
        {
            new() { Id = "a", DisplayName = "Redis Dev" },
        };

        var cut = RenderComponent<RedisConnectionBar>(ps => ps
            .Add(p => p.CacheEntries, entries)
            .Add(p => p.ActiveCacheEntry, entries[0]));

        Assert.Empty(cut.FindAll("select.cache-selector"));
    }
}
