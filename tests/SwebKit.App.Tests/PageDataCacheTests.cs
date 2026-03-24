using SwebKit.App.Services;

namespace SwebKit.App.Tests;

public class PageDataCacheTests
{
    private readonly PageDataCache _cache = new();

    [Fact]
    public void Get_ReturnsNull_WhenKeyNotFound()
    {
        var result = _cache.Get<string>("missing");
        Assert.Null(result);
    }

    [Fact]
    public void Set_And_Get_ReturnsValue()
    {
        _cache.Set("key1", "hello");
        var result = _cache.Get<string>("key1");
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task Get_ReturnsNull_WhenExpired()
    {
        _cache.Set("ephemeral", 42, TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);
        var result = _cache.Get<int>("ephemeral");
        Assert.Equal(0, result); // default(int)
    }

    [Fact]
    public void Invalidate_RemovesEntry()
    {
        _cache.Set("removeMe", "value");
        _cache.Invalidate("removeMe");
        Assert.Null(_cache.Get<string>("removeMe"));
    }

    [Fact]
    public void InvalidateAll_ClearsAllEntries()
    {
        _cache.Set("a", 1);
        _cache.Set("b", 2);
        _cache.InvalidateAll();
        Assert.Equal(0, _cache.Get<int>("a"));
        Assert.Equal(0, _cache.Get<int>("b"));
    }

    [Fact]
    public void InvalidateByPrefix_RemovesMatchingEntries()
    {
        _cache.Set("aks:clusters", "c1");
        _cache.Set("aks:nodes", "n1");
        _cache.Set("sb:queues", "q1");
        _cache.InvalidateByPrefix("aks");
        Assert.Null(_cache.Get<string>("aks:clusters"));
        Assert.Null(_cache.Get<string>("aks:nodes"));
        Assert.Equal("q1", _cache.Get<string>("sb:queues"));
    }

    [Fact]
    public void Set_OverwritesExistingEntry()
    {
        _cache.Set("key", "first");
        _cache.Set("key", "second");
        Assert.Equal("second", _cache.Get<string>("key"));
    }
}
