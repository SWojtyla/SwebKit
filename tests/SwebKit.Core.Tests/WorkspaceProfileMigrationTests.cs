using System.Text.Json;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Tests;

public class WorkspaceProfileMigrationTests
{
    [Fact]
    public async Task LoadAsync_LegacyServiceBusLinks_MigratesFavoriteResources()
    {
        using var _ = new AppDataSandbox();
        var namespaceId = Guid.NewGuid();

        var profile = new ProfileData
        {
            Config = new AppConfig
            {
                ServiceBusEntityLinks =
                [
                    new SbEntityLink
                    {
                        NamespaceId = namespaceId,
                        EntityPath = "orders",
                    },
                ],
            },
            ServiceBusNamespaces =
            [
                new ServiceBusNamespace
                {
                    Id = namespaceId,
                    Alias = "shared",
                    FullyQualifiedNamespace = "shared.servicebus.windows.net",
                    CredentialKey = "sb:ns:test",
                },
            ],
            SchemaVersion = 2,
        };

        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(
            AppDataPaths.ProfilesJson,
            JsonSerializer.Serialize(profile, SwebKitJsonOptions.Indented));

        var repository = new ProfileRepository();
        await repository.LoadAsync();

        var favorite = Assert.Single(repository.Config.FavoriteResources);
        Assert.Equal("service-bus", favorite.Snapshot.Resource.Area);
        Assert.Equal("orders", favorite.Snapshot.Resource.DisplayName);
        Assert.Equal("shared/orders", favorite.Snapshot.Resource.DisplayPath);
        Assert.Equal(namespaceId.ToString("D"), favorite.Snapshot.RestoreState["namespaceId"]);
        Assert.Equal("orders", favorite.Snapshot.RestoreState["entityPath"]);
    }

    [Fact]
    public async Task LoadAsync_SavedWorkspaces_MigratesIntoNamedFavorites()
    {
        using var _ = new AppDataSandbox();

        var profile = new ProfileData
        {
            Config = new AppConfig
            {
                SavedWorkspaces =
                [
                    new SavedWorkspace
                    {
                        Name = "Prod API",
                        SavedAt = new DateTimeOffset(2026, 4, 14, 8, 30, 0, TimeSpan.Zero),
                        Snapshot = new WorkspaceSnapshot
                        {
                            Resource = new OperatorResourceReference
                            {
                                Key = "aks:deployment:ops:api",
                                Area = "aks",
                                Kind = "deployment",
                                DisplayName = "api",
                                DisplayPath = "ops/api",
                            },
                            RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["resourceType"] = "Deployments",
                                ["namespace"] = "ops",
                            },
                        },
                    },
                ],
            },
            SchemaVersion = 3,
        };

        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(
            AppDataPaths.ProfilesJson,
            JsonSerializer.Serialize(profile, SwebKitJsonOptions.Indented));

        var repository = new ProfileRepository();
        await repository.LoadAsync();

        var favorite = Assert.Single(repository.Config.FavoriteResources);
        Assert.Equal("Prod API", favorite.Name);
        Assert.Equal("aks:deployment:ops:api", favorite.Snapshot.Resource.Key);
        Assert.Equal("ops", favorite.Snapshot.RestoreState["namespace"]);
        Assert.Empty(repository.Config.SavedWorkspaces);
    }
}