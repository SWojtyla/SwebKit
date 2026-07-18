using Bunit;
using SwebKit.App.Components.ApiClient;
using SwebKit.Core.Domain;

namespace SwebKit.App.Tests;

/// <summary>
/// Tests for <see cref="KeyValueGrid"/> — the shared editable key/value grid used for headers,
/// query params, and form-data across the API Client. Pure presentational component (DEC-UX-3):
/// no injected services, all state lives in the caller-owned <see cref="KeyValuePair{T}"/> list.
/// </summary>
public sealed class KeyValueGridTests : TestContext
{
    [Fact]
    public void RendersExistingRows_WithKeyValueAndEnabledState()
    {
        var pairs = new List<KeyValuePair<string>>
        {
            new() { Key = "Authorization", Value = "Bearer abc", IsEnabled = true },
            new() { Key = "X-Disabled", Value = "ignored", IsEnabled = false },
        };

        var cut = RenderComponent<KeyValueGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs));

        var rows = cut.FindAll(".kv-grid__row");
        Assert.Equal(2, rows.Count);

        var keyInputs = cut.FindAll("input.kv-grid__input--key");
        var valueInputs = cut.FindAll("input.kv-grid__input--value");
        Assert.Equal("Authorization", keyInputs[0].GetAttribute("value"));
        Assert.Equal("Bearer abc", valueInputs[0].GetAttribute("value"));
        Assert.Equal("X-Disabled", keyInputs[1].GetAttribute("value"));

        Assert.DoesNotContain("kv-grid__row--disabled", rows[0].ClassName);
        Assert.Contains("kv-grid__row--disabled", rows[1].ClassName);
    }

    [Fact]
    public void AddRow_AppendsEnabledEmptyPair_AndRaisesOnChanged()
    {
        var pairs = new List<KeyValuePair<string>>();
        var changedCount = 0;

        var cut = RenderComponent<KeyValueGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs)
            .Add(p => p.OnChanged, () => changedCount++));

        cut.Find("button.kv-grid__add-btn").Click();

        Assert.Single(pairs);
        Assert.True(pairs[0].IsEnabled);
        Assert.Equal(string.Empty, pairs[0].Key);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void RemoveRow_DeletesPairAtIndex_AndRaisesOnChanged()
    {
        var pairs = new List<KeyValuePair<string>>
        {
            new() { Key = "keep", Value = "1" },
            new() { Key = "remove-me", Value = "2" },
        };
        var changedCount = 0;

        var cut = RenderComponent<KeyValueGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs)
            .Add(p => p.OnChanged, () => changedCount++));

        cut.FindAll("button.kv-grid__del-btn")[1].Click();

        Assert.Single(pairs);
        Assert.Equal("keep", pairs[0].Key);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void ToggleRow_FlipsIsEnabled_AndRaisesOnChanged()
    {
        var pairs = new List<KeyValuePair<string>> { new() { Key = "a", Value = "1", IsEnabled = true } };
        var changedCount = 0;

        var cut = RenderComponent<KeyValueGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs)
            .Add(p => p.OnChanged, () => changedCount++));

        cut.Find("input.kv-grid__check").Change(false);

        Assert.False(pairs[0].IsEnabled);
        Assert.Equal(1, changedCount);
        Assert.Contains("kv-grid__row--disabled", cut.Find(".kv-grid__row").ClassName);
    }

    [Fact]
    public void EditingKeyAndValueInputs_UpdatesUnderlyingPair_AndRaisesOnChanged()
    {
        var pairs = new List<KeyValuePair<string>> { new() { Key = "old-key", Value = "old-value" } };
        var changedCount = 0;

        var cut = RenderComponent<KeyValueGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs)
            .Add(p => p.OnChanged, () => changedCount++));

        cut.Find("input.kv-grid__input--key").Input("new-key");
        cut.Find("input.kv-grid__input--value").Input("new-value");

        Assert.Equal("new-key", pairs[0].Key);
        Assert.Equal("new-value", pairs[0].Value);
        Assert.Equal(2, changedCount);
    }
}
