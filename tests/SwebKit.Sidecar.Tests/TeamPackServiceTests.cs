using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Security;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

public class TeamPackServiceTests
{
    private sealed record Fixture(
        TeamPackService Svc,
        ProfileRepository Profiles,
        SqlQueryRepository SqlQueries,
        AlertRuleRepository AlertRules,
        CollectionRepository Collections,
        EnvironmentRepository Environments,
        LinkedCollectionRootRepository LinkedRoots,
        DemoModeService Demo) : IDisposable
    {
        public void Dispose() => SqlQueries.Dispose();
    }

    private static Fixture CreateFixture()
    {
        var profiles = new ProfileRepository();
        var sql = new SqlQueryRepository();
        var rules = new AlertRuleRepository();
        var collections = new CollectionRepository();
        var envs = new EnvironmentRepository();
        var roots = new LinkedCollectionRootRepository();
        var demo = new DemoModeService();
        return new Fixture(
            new TeamPackService(profiles, sql, rules, collections, envs, roots, demo),
            profiles, sql, rules, collections, envs, roots, demo);
    }

    // ── Export: secret hygiene ───────────────────────────────────────────────

    [Fact]
    public async Task Export_StripsCredentialSecrets_AndEmitsNameOnlyCredentialRefManifest()
    {
        using var _ = new AppDataSandbox();
        using var f = CreateFixture();

        await f.Collections.ReplaceStoreAsync(new CollectionsStore
        {
            Collections =
            [
                new ApiCollection
                {
                    Id = "col-1",
                    Name = "Team API",
                    DefaultAuth = new AuthConfig
                    {
                        Type = AuthType.BearerToken,
                        CredentialKey = "orders-svc-token",
                        CredentialSecret = "supersecret-value",
                    },
                    Nodes =
                    [
                        new ApiCollectionNode
                        {
                            Id = "n1",
                            Type = ApiCollectionNodeType.Request,
                            Name = "Get order",
                            Request = new HttpRequestEntry
                            {
                                Name = "Get order",
                                Auth = new AuthConfig
                                {
                                    Type = AuthType.OAuth2,
                                    OAuth2TokenCredentialKey = "oauth-token-1",
                                    CredentialSecret = "another-secret",
                                },
                            },
                        },
                    ],
                },
            ],
        });
        await f.Environments.ReplaceStoreAsync(new EnvironmentsStore
        {
            Environments =
            [
                new ApiEnvironment
                {
                    Id = "env-1",
                    Name = "dev",
                    Variables =
                    [
                        new EnvironmentVariable
                        {
                            Key = "apiKey",
                            SecretSource = EnvironmentVariableSecretSource.AzureKeyVault,
                            CredentialKey = "payments-api-key",
                            KeyVaultName = "prod-secrets",
                        },
                        new EnvironmentVariable { Key = "baseUrl", Value = "https://api.dev" },
                    ],
                },
            ],
        });

        var pack = await f.Svc.ExportAsync();
        var json = f.Svc.Serialize(pack);

        Assert.DoesNotContain("supersecret-value", json);
        Assert.DoesNotContain("another-secret", json);
        Assert.Contains(pack.CredentialRefs, r => r.Key == "orders-svc-token" && r.Kind == "credentialStore");
        Assert.Contains(pack.CredentialRefs, r => r.Key == "oauth-token-1" && r.Kind == "oauthToken");
        Assert.Contains(pack.CredentialRefs, r => r.Key == "payments-api-key" && r.Kind == "azureKeyVault");
        // The baseUrl var is plain but not secret-named — no warning for it.
        Assert.Empty(pack.Warnings);
    }

    [Fact]
    public async Task Export_Warns_WhenPlainVariableLooksLikeASecret()
    {
        using var _ = new AppDataSandbox();
        using var f = CreateFixture();

        await f.Environments.ReplaceStoreAsync(new EnvironmentsStore
        {
            Environments =
            [
                new ApiEnvironment
                {
                    Id = "env-1",
                    Name = "dev",
                    Variables =
                    [
                        new EnvironmentVariable { Key = "api_token", Value = "abc123" },
                        new EnvironmentVariable { Key = "region", Value = "westeurope" },
                    ],
                },
            ],
        });

        var pack = await f.Svc.ExportAsync(new HashSet<string> { TeamPackSections.Environments });

        Assert.Single(pack.Warnings);
        Assert.Contains("api_token", pack.Warnings[0]);
    }

    [Fact]
    public async Task Export_DropsDemoEntities_AndDemoSqlHints()
    {
        using var _ = new AppDataSandbox();
        using var f = CreateFixture();

        await f.Collections.ReplaceStoreAsync(new CollectionsStore
        {
            Collections =
            [
                new ApiCollection { Id = DemoApiCollectionFactory.DemoCollectionId, Name = "Demo" },
                new ApiCollection { Id = "real-1", Name = "Real" },
            ],
        });
        await f.SqlQueries.AddQueryAsync(new SavedSqlQuery
        {
            Name = "demo-scoped",
            Sql = "SELECT 1",
            ConnectionId = DemoModeService.DemoSqlConnectionIdRestricted,
        });

        var pack = await f.Svc.ExportAsync();

        Assert.Single(pack.CollectionsData!.Collections);
        Assert.Equal("Real", pack.CollectionsData.Collections[0].Name);
        // A query saved against a demo connection must not carry a portable hint for it.
        Assert.Null(pack.SavedSqlQueries!.Single().ConnectionHint);
    }

    // ── Import: merge keys / hints / dry-run ─────────────────────────────────

    [Fact]
    public async Task Import_Merge_RebindsConnectionHint_AndUpdatesByNamePlusFolder()
    {
        using var _ = new AppDataSandbox();
        using var f = CreateFixture();
        f.Profiles.Config.SqlConfig = new SqlConfig
        {
            Connections =
            [
                new SqlConnectionEntry
                {
                    Id = "local-conn",
                    Server = "orders-sql.database.windows.net",
                    Database = "orders",
                },
            ],
        };
        var existing = await f.SqlQueries.AddQueryAsync(new SavedSqlQuery
        {
            Name = "top-orders",
            Folder = "ops",
            Sql = "SELECT 1",
        });

        var pack = new TeamPack
        {
            SavedSqlQueries =
            [
                new TeamPackSqlQuery
                {
                    Name = "top-orders",
                    Folder = "ops",
                    Sql = "SELECT TOP 5 * FROM orders",
                    ConnectionHint = new TeamPackConnectionHint
                    {
                        Server = "ORDERS-SQL.database.windows.net",
                        Database = "ORDERS",
                    },
                },
                new TeamPackSqlQuery
                {
                    Name = "orphan",
                    Sql = "SELECT 2",
                    ConnectionHint = new TeamPackConnectionHint { Server = "nope", Database = "none" },
                },
            ],
        };

        var result = await f.Svc.ImportAsync(pack, new TeamPackImportOptions());

        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.Added);
        var queries = await f.SqlQueries.GetQueriesAsync();
        var updated = queries.Single(q => q.Id == existing.Id);
        Assert.Equal("local-conn", updated.ConnectionId);
        Assert.Equal("SELECT TOP 5 * FROM orders", updated.Sql);
        var orphan = queries.Single(q => q.Name == "orphan");
        Assert.Null(orphan.ConnectionId);
        Assert.Contains(result.Conflicts, c => c.Contains("orphan") && c.Contains("nope"));
    }

    [Fact]
    public async Task Import_Merge_CollectionsGetFreshIds_AndEnvironmentsRebind()
    {
        using var _ = new AppDataSandbox();
        using var f = CreateFixture();

        var pack = new TeamPack
        {
            CollectionsData = new CollectionsStore
            {
                Collections = [new ApiCollection { Id = "pack-col", Name = "Team API" }],
            },
            EnvironmentsData = new EnvironmentsStore
            {
                Environments =
                [
                    new ApiEnvironment { Id = "pack-env", Name = "dev", CollectionId = "pack-col" },
                ],
            },
        };

        var result = await f.Svc.ImportAsync(pack, new TeamPackImportOptions());

        Assert.Equal(2, result.Added);
        var collection = Assert.Single(f.Collections.Collections);
        Assert.NotEqual("pack-col", collection.Id);
        var env = Assert.Single(f.Environments.Environments);
        Assert.Equal(collection.Id, env.CollectionId);
    }

    [Fact]
    public async Task Import_DryRun_ReportsTheSameCounts_ButWritesNothing()
    {
        using var _ = new AppDataSandbox();
        using var f = CreateFixture();

        var pack = new TeamPack
        {
            Maps =
            [
                new WorkspaceMap
                {
                    Id = "pack-map",
                    Name = "orders",
                    Nodes = [new WorkspaceResourceNode { Id = "a", ResourceKey = "k", Area = WorkspaceResourceArea.Sql }],
                },
            ],
            CollectionsData = new CollectionsStore
            {
                Collections = [new ApiCollection { Id = "c", Name = "Team API" }],
            },
        };

        var result = await f.Svc.ImportAsync(pack, new TeamPackImportOptions { DryRun = true });

        Assert.True(result.DryRun);
        Assert.Equal(2, result.Added);
        Assert.Empty(f.Profiles.Config.Maps);
        Assert.Empty(f.Collections.Collections);
    }

    [Fact]
    public async Task Import_Replace_SwapsTheWholeSection()
    {
        using var _ = new AppDataSandbox();
        using var f = CreateFixture();
        await f.Collections.ReplaceStoreAsync(new CollectionsStore
        {
            Collections = [new ApiCollection { Id = "old", Name = "Old collection" }],
        });

        var pack = new TeamPack
        {
            CollectionsData = new CollectionsStore
            {
                Collections = [new ApiCollection { Id = "pack-col", Name = "Team API" }],
            },
        };

        var result = await f.Svc.ImportAsync(pack, new TeamPackImportOptions
        {
            Strategy = TeamPackMergeStrategy.Replace,
        });

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Skipped);
        var collection = Assert.Single(f.Collections.Collections);
        Assert.Equal("Team API", collection.Name);
    }

    [Fact]
    public async Task Import_StripsCredentialSecrets_SoAHandEditedPackCannotPersistOne()
    {
        using var _ = new AppDataSandbox();
        using var f = CreateFixture();

        var pack = new TeamPack
        {
            CollectionsData = new CollectionsStore
            {
                Collections =
                [
                    new ApiCollection
                    {
                        Id = "c1",
                        Name = "Team API",
                        DefaultAuth = new AuthConfig
                        {
                            Type = AuthType.BearerToken,
                            CredentialKey = "ref-only",
                            CredentialSecret = "must-never-persist",
                        },
                    },
                ],
            },
        };

        await f.Svc.ImportAsync(pack, new TeamPackImportOptions());

        var stored = Assert.Single(f.Collections.Collections);
        Assert.Equal("ref-only", stored.DefaultAuth!.CredentialKey);
        Assert.Null(stored.DefaultAuth.CredentialSecret);
    }

    [Fact]
    public async Task Import_Merge_AlertRules_MatchByNameAndKeepLocalId()
    {
        using var _ = new AppDataSandbox();
        using var f = CreateFixture();
        var existing = new MonitoringAlertRule
        {
            Id = "local-rule",
            Name = "dlq growth",
            Source = AlertRuleSource.ServiceBusDlqDepth,
            IntervalSeconds = 60,
        };
        await f.AlertRules.UpsertAsync(existing);

        var pack = new TeamPack
        {
            AlertRules =
            [
                new MonitoringAlertRule
                {
                    Id = "pack-rule",
                    Name = "DLQ Growth", // case-insensitive name match
                    Source = AlertRuleSource.ServiceBusDlqDepth,
                    IntervalSeconds = 120,
                },
            ],
        };

        var result = await f.Svc.ImportAsync(pack, new TeamPackImportOptions());

        Assert.Equal(1, result.Updated);
        var rules = await f.AlertRules.GetAllAsync();
        var rule = Assert.Single(rules);
        Assert.Equal("local-rule", rule.Id);
        Assert.Equal(120, rule.IntervalSeconds);
    }

    // ── Deserialize / endpoint guards ────────────────────────────────────────

    [Fact]
    public void Deserialize_RejectsNonTeamPackFormat()
    {
        using var _ = new AppDataSandbox();
        using var f = CreateFixture();

        var ex = Assert.Throws<InvalidOperationException>(
            () => f.Svc.Deserialize("""{"format":"swebkit-config"}"""));
        Assert.Contains("team pack", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportEndpoint_DemoMode_ReturnsBadRequest()
    {
        using var _ = new AppDataSandbox();
        using var f = CreateFixture();
        f.Demo.IsDemoMode = true;

        var request = new DefaultHttpContext().Request;
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        var result = await ConfigEndpoints.ImportTeamPackAsync(
            f.Svc, request, f.Demo, new FakeAccessReports());

        Assert.Equal((int)HttpStatusCode.BadRequest, StatusCode(result));
    }

    [Fact]
    public async Task ImportEndpoint_DryRun_ReturnsReport_WithoutPersisting()
    {
        using var _ = new AppDataSandbox();
        using var f = CreateFixture();
        var pack = new TeamPack
        {
            Maps = [new WorkspaceMap { Id = "m", Name = "orders" }],
        };
        var request = new DefaultHttpContext().Request;
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes(f.Svc.Serialize(pack)));
        var access = new FakeAccessReports();

        var result = await ConfigEndpoints.ImportTeamPackAsync(
            f.Svc, request, f.Demo, access, dryRun: true);

        var ok = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<TeamPackImportResult>>(result);
        Assert.True(ok.Value!.DryRun);
        Assert.Equal(1, ok.Value.Added);
        Assert.Empty(f.Profiles.Config.Maps);
        Assert.Equal(0, access.InvalidateAllCount);
    }

    [Fact]
    public async Task ImportEndpoint_InvalidJson_ReturnsBadRequest()
    {
        using var _ = new AppDataSandbox();
        using var f = CreateFixture();
        var request = new DefaultHttpContext().Request;
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes("not-json{{{"));

        var result = await ConfigEndpoints.ImportTeamPackAsync(
            f.Svc, request, f.Demo, new FakeAccessReports());

        Assert.Equal((int)HttpStatusCode.BadRequest, StatusCode(result));
    }

    private static int StatusCode(IResult result) =>
        (int)(result.GetType().GetProperty("StatusCode")?.GetValue(result) ?? 200);

    private sealed class FakeAccessReports : IAccessReportService
    {
        public int InvalidateAllCount;

        public Task<AccessReport> GetReportAsync(bool forceRefresh = false, CancellationToken ct = default) =>
            Task.FromResult(new AccessReport([], DateTimeOffset.UtcNow));

        public void Invalidate(string featureArea, string? connectionKey = null) { }

        public void InvalidateAll() => InvalidateAllCount++;

        public void RecordObservedDenial(AccessDenial denial, string connectionKey) { }

        public bool TryGetKnownDenial(
            string featureArea, string connectionKey, string capability, out AccessDenial denial)
        {
            denial = null!;
            return false;
        }
    }
}
