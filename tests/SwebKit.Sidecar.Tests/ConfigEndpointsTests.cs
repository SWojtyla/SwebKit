using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Exercises the extracted export/import handlers against a real <see cref="ConfigurationBundleService"/>
/// backed by real (file-persisting) repositories, sandboxed to a temp app-data root so the round trip
/// is genuine — including the actual JSON (de)serialization path — without touching the developer's
/// real %APPDATA%.
/// </summary>
public class ConfigEndpointsTests
{
    private static ConfigurationBundleService BuildService(out CollectionRepository collections, out ProfileRepository profiles)
    {
        profiles = new ProfileRepository();
        var uiState = new UiStateRepository();
        var userSettings = new UserSettingsRepository();
        var releases = new ReleaseRepository();
        var scheduledMessages = new ScheduledMessageRepository();
        var events = new AppEventBus(NullLogger<AppEventBus>.Instance);
        var appState = new AppStateService(profiles, uiState, events);
        collections = new CollectionRepository();
        var environments = new EnvironmentRepository();

        return new ConfigurationBundleService(profiles, uiState, userSettings, releases, scheduledMessages, appState, collections, environments);
    }

    private static DefaultHttpContext BuildImportHttpContext(string json)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return ctx;
    }

    [Fact]
    public async Task ExportAsync_ReturnsJsonContentType_WithSerializedBundle()
    {
        using var sandbox = new AppDataSandbox();
        var svc = BuildService(out _, out var profiles);
        profiles.Config.Name = "export-test-profile";

        var result = ConfigEndpoints.ExportAsync(svc);

        Assert.Equal("application/json", result.ContentType);
        Assert.Contains("export-test-profile", result.ResponseContent);
        Assert.Contains("\"schemaVersion\"", result.ResponseContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_RoundTrip_RestoresCollectionsIntoRepository()
    {
        using var sandbox = new AppDataSandbox();
        var exportSvc = BuildService(out var exportCollections, out _);
        await exportCollections.AddCollectionAsync("Imported Collection");
        var exportedJson = exportSvc.Serialize(exportSvc.Export());

        // Import into a fresh service/repository set (still sandboxed) to prove the round trip
        // actually persists via the extracted handler rather than relying on shared in-memory state.
        var importSvc = BuildService(out var importCollections, out _);
        var ctx = BuildImportHttpContext(exportedJson);

        var result = await ConfigEndpoints.ImportAsync(importSvc, ctx.Request);

        Assert.Equal(200, result.StatusCode);
        await importCollections.LoadAsync();
        Assert.Contains(importCollections.Collections, c => c.Name == "Imported Collection");
    }

    [Fact]
    public async Task ImportAsync_UnsupportedSchemaVersion_Throws_AndNeverPersists()
    {
        using var sandbox = new AppDataSandbox();
        var svc = BuildService(out var collections, out _);
        var badJson = """{"schemaVersion":99}""";
        var ctx = BuildImportHttpContext(badJson);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ConfigEndpoints.ImportAsync(svc, ctx.Request));

        // Nothing should have been written for an unsupported schema version.
        Assert.Empty(collections.Collections);
    }
}
