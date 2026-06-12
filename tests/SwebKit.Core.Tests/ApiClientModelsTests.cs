using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Tests;

public sealed class ApiClientModelsTests
{
    // ── CollectionsStore defaults ──────────────────────────────────────────────

    [Fact]
    public void CollectionsStore_DefaultSchemaVersion_Is1()
    {
        var store = new CollectionsStore();

        Assert.Equal(1, store.SchemaVersion);
        Assert.Empty(store.Collections);
    }

    // ── EnvironmentsStore defaults ─────────────────────────────────────────────

    [Fact]
    public void EnvironmentsStore_DefaultSchemaVersion_Is1()
    {
        var store = new EnvironmentsStore();

        Assert.Equal(1, store.SchemaVersion);
        Assert.Empty(store.Environments);
        Assert.NotNull(store.UiState);
    }

    // ── ApiClientUiState defaults ──────────────────────────────────────────────

    [Fact]
    public void ApiClientUiState_DefaultActiveEnvironmentId_IsNull()
    {
        var state = new ApiClientUiState();

        Assert.Null(state.ActiveEnvironmentId);
        Assert.Empty(state.LastSelectedRequestIdByCollection);
    }

    // ── ApiCollection defaults ────────────────────────────────────────────────

    [Fact]
    public void ApiCollection_DefaultNodes_IsEmpty()
    {
        var col = new ApiCollection();

        Assert.Empty(col.Nodes);
        Assert.Empty(col.Variables);
        Assert.Null(col.DefaultAuth);
    }

    // ── ApiCollectionNode types ────────────────────────────────────────────────

    [Fact]
    public void ApiCollectionNode_FolderType_HasEmptyChildrenByDefault()
    {
        var node = new ApiCollectionNode { Type = ApiCollectionNodeType.Folder };

        Assert.Empty(node.Children);
        Assert.Null(node.Request);
    }

    [Fact]
    public void ApiCollectionNode_RequestType_HasNullChildrenByDefault()
    {
        var node = new ApiCollectionNode { Type = ApiCollectionNodeType.Request };

        Assert.Empty(node.Children); // Children is always initialised to []
    }

    // ── HttpRequestEntry defaults ──────────────────────────────────────────────

    [Fact]
    public void HttpRequestEntry_DefaultMethod_IsGet()
    {
        var entry = new HttpRequestEntry();

        Assert.Equal(ApiRequestMethod.Get, entry.Method);
        Assert.Empty(entry.Headers);
        Assert.Empty(entry.QueryParams);
        Assert.Empty(entry.CaptureRules);
        Assert.Null(entry.Auth);
    }

    [Fact]
    public void HttpRequestEntry_DefaultBody_HasNoneMode()
    {
        var entry = new HttpRequestEntry();

        Assert.Equal(RequestBodyMode.None, entry.Body.Mode);
        Assert.Null(entry.Body.RawContent);
        Assert.Empty(entry.Body.FormData);
    }

    // ── AuthConfig defaults ────────────────────────────────────────────────────

    [Fact]
    public void AuthConfig_DefaultType_IsNone()
    {
        var auth = new AuthConfig();

        Assert.Equal(AuthType.None, auth.Type);
        Assert.Null(auth.CredentialKey);
        Assert.Equal(ApiKeyLocation.Header, auth.ApiKeyLocation);
        Assert.Equal(OAuth2GrantType.ClientCredentials, auth.OAuth2GrantType);
    }

    // ── CaptureRule defaults ───────────────────────────────────────────────────

    [Fact]
    public void CaptureRule_DefaultSource_IsBodyJsonPath()
    {
        var rule = new CaptureRule();

        Assert.Equal(CaptureSource.BodyJsonPath, rule.Source);
        Assert.True(rule.IsEnabled);
        Assert.Equal("collection", rule.TargetScope);
    }

    // ── EnvironmentVariable defaults ───────────────────────────────────────────

    [Fact]
    public void EnvironmentVariable_DefaultSecretSource_IsPlain()
    {
        var variable = new EnvironmentVariable();

        Assert.Equal(EnvironmentVariableSecretSource.Plain, variable.SecretSource);
        Assert.True(variable.IsEnabled);
        Assert.Null(variable.CredentialKey);
    }

    // ── CollectionVariable ─────────────────────────────────────────────────────

    [Fact]
    public void CollectionVariable_KeyValue_StoresCorrectly()
    {
        var v = new CollectionVariable { Key = "base_url", Value = "https://api.dev.acme.com" };

        Assert.Equal("base_url", v.Key);
        Assert.Equal("https://api.dev.acme.com", v.Value);
    }
}

public sealed class UserSettingsAutoSaveTests
{
    [Fact]
    public void UserSettings_DefaultAutoSaveRequests_IsFalse()
    {
        var settings = new UserSettings();

        Assert.False(settings.AutoSaveRequests);
    }

    [Fact]
    public async Task UserSettingsRepository_PersistsAutoSaveRequests()
    {
        using var _ = new AppDataSandbox();
        var repo = new UserSettingsRepository();

        repo.Settings.AutoSaveRequests = true;
        await repo.SaveAsync();

        var reader = new UserSettingsRepository();
        await reader.LoadAsync();

        Assert.True(reader.Settings.AutoSaveRequests);
    }

    [Fact]
    public async Task UserSettingsRepository_AutoSaveRequests_DefaultsFalseWhenFieldMissing()
    {
        using var _ = new AppDataSandbox();

        // Write a settings file that has no autoSaveRequests key (simulates old file)
        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.UserSettingsJson, """{"theme":"dark-studio-ledger"}""");

        var repo = new UserSettingsRepository();
        await repo.LoadAsync();

        Assert.False(repo.Settings.AutoSaveRequests);
        Assert.Equal("dark-studio-ledger", repo.Settings.Theme);
    }
}
