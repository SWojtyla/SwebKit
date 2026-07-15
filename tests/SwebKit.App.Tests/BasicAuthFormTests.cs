using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.ApiClient;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.App.Tests;

/// <summary>
/// Tests for <see cref="BasicAuthForm"/> — Basic auth (username + credential-store-backed
/// password) editor used by <c>AuthPanel</c>. Pure presentational component (DEC-UX-3): the only
/// injected service is <see cref="ICredentialStore"/>, faked here in-memory.
/// </summary>
public sealed class BasicAuthFormTests : TestContext
{
    private readonly InMemoryCredentialStore _credentialStore = new();

    public BasicAuthFormTests()
    {
        Services.AddSingleton<ICredentialStore>(_credentialStore);
    }

    [Fact]
    public void RendersExistingUsername()
    {
        var auth = new AuthConfig { BasicUsername = "alice" };

        var cut = RenderComponent<BasicAuthForm>(parameters => parameters
            .Add(p => p.Auth, auth));

        Assert.Equal("alice", cut.Find("input.basic-form__input[type=text]").GetAttribute("value"));
    }

    [Fact]
    public void RendersExistingPassword_FromCredentialStore_WhenCredentialKeySet()
    {
        _credentialStore.Save("api-client:basic:existing", "s3cret");
        var auth = new AuthConfig { BasicUsername = "alice", CredentialKey = "api-client:basic:existing" };

        var cut = RenderComponent<BasicAuthForm>(parameters => parameters
            .Add(p => p.Auth, auth));

        var passwordInput = cut.Find("input[autocomplete='new-password']");
        Assert.Equal("s3cret", passwordInput.GetAttribute("value"));
    }

    [Fact]
    public void TypingUsername_UpdatesAuth_AndRaisesOnChanged()
    {
        var auth = new AuthConfig();
        var changedCount = 0;

        var cut = RenderComponent<BasicAuthForm>(parameters => parameters
            .Add(p => p.Auth, auth)
            .Add(p => p.OnChanged, () => changedCount++));

        cut.Find("input.basic-form__input[type=text]").Input("bob");

        Assert.Equal("bob", auth.BasicUsername);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void TypingPassword_GeneratesCredentialKey_AndSavesToStore()
    {
        var auth = new AuthConfig { BasicUsername = "alice" };

        var cut = RenderComponent<BasicAuthForm>(parameters => parameters
            .Add(p => p.Auth, auth));

        cut.Find("input[autocomplete='new-password']").Input("hunter2");

        Assert.False(string.IsNullOrEmpty(auth.CredentialKey));
        Assert.StartsWith("api-client:basic:", auth.CredentialKey, StringComparison.Ordinal);
        Assert.Equal("hunter2", _credentialStore.Get(auth.CredentialKey!));
    }

    [Fact]
    public void ClearingPassword_DeletesCredentialFromStore()
    {
        _credentialStore.Save("api-client:basic:existing", "s3cret");
        var auth = new AuthConfig { BasicUsername = "alice", CredentialKey = "api-client:basic:existing" };

        var cut = RenderComponent<BasicAuthForm>(parameters => parameters
            .Add(p => p.Auth, auth));

        cut.Find("input[autocomplete='new-password']").Input(string.Empty);

        Assert.Null(_credentialStore.Get("api-client:basic:existing"));
    }

    [Fact]
    public void ShowHideToggle_SwitchesPasswordInputType()
    {
        var auth = new AuthConfig();

        var cut = RenderComponent<BasicAuthForm>(parameters => parameters
            .Add(p => p.Auth, auth));

        Assert.Equal("password", cut.Find("input[autocomplete='new-password']").GetAttribute("type"));

        cut.Find("button.basic-form__toggle").Click();

        Assert.Equal("text", cut.Find("input[autocomplete='new-password']").GetAttribute("type"));
    }

    private sealed class InMemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

        public void Save(string key, string secret) => _secrets[key] = secret;
        public string? Get(string key) => _secrets.TryGetValue(key, out var value) ? value : null;
        public void Delete(string key) => _secrets.Remove(key);
        public IReadOnlyList<string> ListKeys(string prefix = "") =>
            _secrets.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
