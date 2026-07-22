using SwebKit.Core.Configuration;

namespace SwebKit.Core.Tests;

public class UiStateFilterTests
{
    private const string ScopeKey = "ns-guid:my-queue";

    [Fact]
    public async Task SaveFilterAsync_FilterAppearsInGetFilters()
    {
        using var _ = new AppDataSandbox();
        var repo = new UiStateRepository();

        await repo.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "errors", Value = "level=error" });

        var result = repo.GetFilters(ScopeKey);
        Assert.Single(result);
        Assert.Equal("errors", result[0].Name);
        Assert.Equal("level=error", result[0].Value);
    }

    [Fact]
    public async Task GetFilters_UnknownScope_ReturnsEmpty()
    {
        using var _ = new AppDataSandbox();
        var repo = new UiStateRepository();

        var result = repo.GetFilters("no-such-scope");

        Assert.Empty(result);
    }

    [Fact]
    public async Task SaveFilterAsync_SameName_Overwrites()
    {
        using var _ = new AppDataSandbox();
        var repo = new UiStateRepository();

        await repo.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "errors", Value = "old-value" });
        await repo.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "errors", Value = "new-value" });

        var result = repo.GetFilters(ScopeKey);
        Assert.Single(result);
        Assert.Equal("new-value", result[0].Value);
    }

    [Fact]
    public async Task SaveFilterAsync_SameNameDifferentCase_Overwrites()
    {
        using var _ = new AppDataSandbox();
        var repo = new UiStateRepository();

        await repo.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "Errors", Value = "v1" });
        await repo.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "errors", Value = "v2" });

        var result = repo.GetFilters(ScopeKey);
        Assert.Single(result);
        Assert.Equal("v2", result[0].Value);
    }

    [Fact]
    public async Task DeleteFilterAsync_RemovesMatchingFilter()
    {
        using var _ = new AppDataSandbox();
        var repo = new UiStateRepository();

        await repo.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "errors", Value = "level=error" });
        await repo.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "warnings", Value = "level=warn" });

        await repo.DeleteFilterAsync(ScopeKey, "errors");

        var result = repo.GetFilters(ScopeKey);
        Assert.Single(result);
        Assert.Equal("warnings", result[0].Name);
    }

    [Fact]
    public async Task DeleteFilterAsync_UnknownScope_DoesNotThrow()
    {
        using var _ = new AppDataSandbox();
        var repo = new UiStateRepository();

        await repo.DeleteFilterAsync("no-such-scope", "no-such-filter"); // should not throw
    }

    [Fact]
    public async Task SaveFilterAsync_IsolatedPerScope()
    {
        using var _ = new AppDataSandbox();
        var repo = new UiStateRepository();
        const string scopeA = "ns-a:queue";
        const string scopeB = "ns-b:queue";

        await repo.SaveFilterAsync(scopeA, new SavedFilter { Name = "f1", Value = "v1" });
        await repo.SaveFilterAsync(scopeB, new SavedFilter { Name = "f2", Value = "v2" });

        Assert.Equal("v1", repo.GetFilters(scopeA)[0].Value);
        Assert.Equal("v2", repo.GetFilters(scopeB)[0].Value);
        Assert.Single(repo.GetFilters(scopeA));
        Assert.Single(repo.GetFilters(scopeB));
    }

    [Fact]
    public async Task PersistenceRoundtrip_FiltersRestoreAfterReload()
    {
        using var _ = new AppDataSandbox();

        var writer = new UiStateRepository();
        await writer.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "errors", Value = "level=error" });

        var reader = new UiStateRepository();
        await reader.LoadAsync();

        var result = reader.GetFilters(ScopeKey);
        Assert.Single(result);
        Assert.Equal("errors", result[0].Name);
        Assert.Equal("level=error", result[0].Value);
    }

    [Fact]
    public async Task LoadAsync_WithCorruptedPrimaryAndBackup_RecoversFilters()
    {
        using var _ = new AppDataSandbox();

        var writer = new UiStateRepository();
        await writer.SaveFilterAsync(ScopeKey, new SavedFilter { Name = "errors", Value = "level=error" });

        var backupPath = $"{AppDataPaths.UiStateJson}.bak";
        Assert.True(File.Exists(backupPath));

        await File.WriteAllTextAsync(AppDataPaths.UiStateJson, "{ invalid json");

        var reader = new UiStateRepository();
        await reader.LoadAsync();

        var result = reader.GetFilters(ScopeKey);
        Assert.Single(result);
        Assert.Equal("errors", result[0].Name);
        Assert.Equal("level=error", result[0].Value);
    }

    [Fact]
    public async Task ViewState_PersistenceRoundtrip_RestoresBooleanFlag()
    {
        using var _ = new AppDataSandbox();

        var writer = new UiStateRepository();
        await writer.SaveViewStateAsync("service-bus:namespace-pane-collapsed", true);

        var reader = new UiStateRepository();
        await reader.LoadAsync();

        Assert.True(reader.GetViewState("service-bus:namespace-pane-collapsed", defaultValue: false));
        Assert.False(reader.GetViewState("service-bus:missing-pane-state", defaultValue: false));
    }

    [Fact]
    public async Task MessageListPreferences_PersistenceRoundtrip_RestoresColumnsAndDensity()
    {
        using var _ = new AppDataSandbox();
        const string preferenceScope = "ns-guid:my-queue:active";

        var writer = new UiStateRepository();
        await writer.SaveMessageListPreferencesAsync(preferenceScope, new MessageListPreferences
        {
            RowDensity = "compact",
            BuiltInColumns = new Dictionary<string, bool>
            {
                ["message-id"] = true,
                ["subject"] = false,
            },
            CustomPropertyColumns = ["region", "tenant"]
        });

        var reader = new UiStateRepository();
        await reader.LoadAsync();

        var restored = reader.GetMessageListPreferences(preferenceScope);
        Assert.Equal("compact", restored.RowDensity);
        Assert.True(restored.BuiltInColumns["message-id"]);
        Assert.False(restored.BuiltInColumns["subject"]);
        Assert.Equal(["region", "tenant"], restored.CustomPropertyColumns);
    }

    [Fact]
    public async Task LoadAsync_LegacyUiStateWithoutWave3Fields_UsesSafeDefaults()
    {
        using var _ = new AppDataSandbox();

        var legacyJson =
                """
                        {
                            "savedFilters": {
                                "ns-guid:my-queue": [
                                    { "name": "legacy", "value": "priority=high" }
                                ]
                            }
                        }
                        """;

        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.UiStateJson, legacyJson);

        var repo = new UiStateRepository();
        await repo.LoadAsync();

        var restoredFilter = repo.GetFilters(ScopeKey);
        Assert.Single(restoredFilter);
        Assert.Equal("legacy", restoredFilter[0].Name);

        var preferences = repo.GetMessageListPreferences("ns-guid:my-queue:active");
        Assert.Equal("default", preferences.RowDensity);
        Assert.Empty(preferences.BuiltInColumns);
        Assert.Empty(preferences.CustomPropertyColumns);
    }

    [Fact]
    public async Task LoadAsync_NullWave3Fields_AreNormalizedOnRead()
    {
        using var _ = new AppDataSandbox();
        const string preferenceScope = "ns-guid:my-queue:dlq";

        var jsonWithNulls =
                """
                        {
                            "messageListPreferences": {
                                "ns-guid:my-queue:dlq": {
                                    "rowDensity": null,
                                    "builtInColumns": null,
                                    "customPropertyColumns": [" region ", "REGION", " "]
                                }
                            }
                        }
                        """;

        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.UiStateJson, jsonWithNulls);

        var repo = new UiStateRepository();
        await repo.LoadAsync();

        var restored = repo.GetMessageListPreferences(preferenceScope);
        Assert.Equal("default", restored.RowDensity);
        Assert.Empty(restored.BuiltInColumns);
        Assert.Single(restored.CustomPropertyColumns);
        Assert.Equal("region", restored.CustomPropertyColumns[0]);
    }

    [Fact]
    public async Task DashboardPreferences_MissingPayload_UsesDefaultTiles()
    {
        using var _ = new AppDataSandbox();

        var repo = new UiStateRepository();
        await repo.LoadAsync();

        var preferences = repo.GetDashboardPreferences(DefaultDashboardTiles());

        Assert.Equal(["shell.favorites", "shell.recent-resources", "service-bus.dead-letters", "service-bus.entity-watch"],
            preferences.Tiles.Select(static tile => tile.TileId));
        Assert.True(preferences.Tiles[0].IsVisible);
        Assert.False(preferences.Tiles[1].IsVisible);
        Assert.Equal("3x2", preferences.Tiles[0].Size);
        Assert.Single(preferences.Views);
        Assert.Equal("Default view", preferences.Views[0].Title);
        Assert.Equal(preferences.Views[0].Id, preferences.ActiveViewId);
    }

    [Fact]
    public async Task DashboardPreferences_DropsUnknownTilesAndAppendsMissingDefaults()
    {
        using var _ = new AppDataSandbox();

        var json =
                """
                        {
                            "dashboard": {
                                "tiles": [
                                    { "tileId": "unknown.tile", "isVisible": true, "size": "wide" },
                                    { "tileId": "service-bus.dead-letters", "isVisible": false, "size": "small" },
                                    { "tileId": "shell.favorites", "isVisible": true, "size": "not-real" }
                                ]
                            }
                        }
                        """;

        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.UiStateJson, json);

        var repo = new UiStateRepository();
        await repo.LoadAsync();

        var preferences = repo.GetDashboardPreferences(DefaultDashboardTiles());

        Assert.Equal(["service-bus.dead-letters", "shell.favorites", "shell.recent-resources", "service-bus.entity-watch"],
            preferences.Tiles.Select(static tile => tile.TileId));
        Assert.False(preferences.Tiles[0].IsVisible);
        Assert.Equal("1x1", preferences.Tiles[0].Size);
        Assert.Equal("2x1", preferences.Tiles[1].Size);
    }

    [Fact]
    public async Task SaveDashboardPreferencesAsync_RoundTripsVisibilityOrderAndSize()
    {
        using var _ = new AppDataSandbox();

        var writer = new UiStateRepository();
        await writer.SaveDashboardPreferencesAsync(new DashboardPreferences
        {
            Tiles =
            [
                new DashboardTilePreference { TileId = "service-bus.dead-letters", IsVisible = false, Size = "small" },
                new DashboardTilePreference { TileId = "shell.favorites", IsVisible = true, Size = "wide" }
            ]
        }, DefaultDashboardTiles());

        var reader = new UiStateRepository();
        await reader.LoadAsync();

        var preferences = reader.GetDashboardPreferences(DefaultDashboardTiles());

        Assert.Equal(["service-bus.dead-letters", "shell.favorites", "shell.recent-resources", "service-bus.entity-watch"],
            preferences.Tiles.Select(static tile => tile.TileId));
        Assert.False(preferences.Tiles[0].IsVisible);
        Assert.Equal("1x1", preferences.Tiles[0].Size);
        Assert.True(preferences.Tiles[1].IsVisible);
        Assert.Equal("3x2", preferences.Tiles[1].Size);
        Assert.False(preferences.Tiles[2].IsVisible);
    }

    [Fact]
    public async Task DashboardPreferences_PreservesKnownTemplateInstances()
    {
        using var _ = new AppDataSandbox();

        var writer = new UiStateRepository();
        await writer.SaveDashboardPreferencesAsync(new DashboardPreferences
        {
            Tiles =
            [
                new DashboardTilePreference
                {
                    TileId = "service-bus.entity-watch:abc123",
                    IsVisible = true,
                    Size = "medium",
                    Settings = new Dictionary<string, string>
                    {
                        ["namespaceId"] = "00000000-0000-0000-0000-000000000001",
                        ["entityPath"] = "order-created"
                    }
                },
                new DashboardTilePreference { TileId = "unknown.template:abc123", IsVisible = true, Size = "wide" }
            ]
        }, DefaultDashboardTiles());

        var reader = new UiStateRepository();
        await reader.LoadAsync();

        var preferences = reader.GetDashboardPreferences(DefaultDashboardTiles());

        Assert.Equal("service-bus.entity-watch:abc123", preferences.Tiles[0].TileId);
        Assert.Equal("order-created", preferences.Tiles[0].Settings["entityPath"]);
        Assert.Equal("2x1", preferences.Tiles[0].Size);
        Assert.DoesNotContain(preferences.Tiles, static tile => tile.TileId.StartsWith("unknown.template", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DashboardPreferences_LegacyPayload_CleanResetsTilesToDefaults()
    {
        // A pre-redesign payload (schema < 3) must load without crashing and reset tile
        // visibility/order/size to the new defaults — no migration of the old command-center
        // layout (DEC-DR-2). The persisted dead-letters "hidden/small" is discarded.
        using var _ = new AppDataSandbox();

        var json =
            """
            {
                "dashboard": {
                    "schemaVersion": 1,
                    "tiles": [
                        { "tileId": "service-bus.dead-letters", "isVisible": false, "size": "small" },
                        { "tileId": "shell.favorites", "isVisible": true, "size": "wide" }
                    ]
                }
            }
            """;

        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.UiStateJson, json);

        var repo = new UiStateRepository();
        await repo.LoadAsync();

        var preferences = repo.GetDashboardPreferences(DefaultDashboardTiles());

        Assert.Equal(DashboardPreferences.CurrentSchemaVersion, preferences.SchemaVersion);
        Assert.Single(preferences.Views);
        Assert.Equal("Default view", preferences.Views[0].Title);
        Assert.Equal(preferences.Views[0].Id, preferences.ActiveViewId);

        // Tiles are the fresh defaults, in default order — not the legacy persisted values.
        Assert.Equal(
            DefaultDashboardTiles().Select(static tile => tile.TileId),
            preferences.Views[0].Tiles.Select(static tile => tile.TileId));
        var deadLetters = preferences.Views[0].Tiles.Single(static tile => tile.TileId == "service-bus.dead-letters");
        Assert.True(deadLetters.IsVisible);   // reset to default, ignoring persisted isVisible:false
        Assert.Equal("1x1", deadLetters.Size); // reset to default, ignoring persisted "small"
    }

    [Fact]
    public async Task DashboardPreferences_LegacyPayload_PreservesSavedViewsButResetsTheirTiles()
    {
        // Clean reset preserves view identity (id/title/filters) across a schema upgrade while
        // re-seeding each view's tiles from the defaults (test-plan C3).
        using var _ = new AppDataSandbox();

        var json =
            """
            {
                "dashboard": {
                    "schemaVersion": 2,
                    "activeViewId": "focus",
                    "views": [
                        {
                            "id": "default",
                            "title": "Default view",
                            "isDefault": true,
                            "tiles": [ { "tileId": "shell.open-tabs", "isVisible": true, "size": "4x2" } ]
                        },
                        {
                            "id": "focus",
                            "title": "Attention only",
                            "tiles": [ { "tileId": "legacy.removed-tile", "isVisible": true, "size": "small" } ],
                            "filters": { "area": "service-bus", "severity": "attention" }
                        }
                    ]
                }
            }
            """;

        AppDataPaths.EnsureDirectoryExists();
        await File.WriteAllTextAsync(AppDataPaths.UiStateJson, json);

        var repo = new UiStateRepository();
        await repo.LoadAsync();

        var preferences = repo.GetDashboardPreferences(DefaultDashboardTiles());

        Assert.Equal(DashboardPreferences.CurrentSchemaVersion, preferences.SchemaVersion);
        Assert.Equal(2, preferences.Views.Count);
        Assert.Equal("focus", preferences.ActiveViewId); // active view honored

        var focus = preferences.Views.Single(static view => view.Id == "focus");
        Assert.Equal("Attention only", focus.Title);            // title preserved
        Assert.Equal("service-bus", focus.Filters.Area);        // filters preserved
        Assert.Equal("attention", focus.Filters.Severity);
        Assert.Equal(                                           // tiles reset to defaults
            DefaultDashboardTiles().Select(static tile => tile.TileId),
            focus.Tiles.Select(static tile => tile.TileId));
    }

    [Fact]
    public async Task SaveDashboardPreferencesAsync_RoundTripsViewsAndActiveView()
    {
        using var _ = new AppDataSandbox();

        var writer = new UiStateRepository();
        await writer.SaveDashboardPreferencesAsync(new DashboardPreferences
        {
            ActiveViewId = "focus",
            Views =
            [
                new DashboardViewPreference
                {
                    Id = "default",
                    Title = "Default view",
                    IsDefault = true,
                    Tiles = DefaultDashboardTiles().Select(static tile => tile with { }).ToList(),
                    Filters = new DashboardViewFilterPreference { Area = "all", Severity = "all", TimeWindow = "live", LiveMode = "live" },
                    Layout = new DashboardViewLayoutPreference { ShowKpiRibbon = true, CollapseInsightDock = false }
                },
                new DashboardViewPreference
                {
                    Id = "focus",
                    Title = "Attention only",
                    Tiles =
                    [
                        new DashboardTilePreference { TileId = "service-bus.dead-letters", IsVisible = true, Size = "small" },
                        new DashboardTilePreference { TileId = "service-bus.entity-watch:abc123", IsVisible = true, Size = "2x2" }
                    ],
                    Filters = new DashboardViewFilterPreference { Area = "service-bus", Severity = "attention", TimeWindow = "1h", LiveMode = "snapshot" },
                    Layout = new DashboardViewLayoutPreference { ShowKpiRibbon = false, CollapseInsightDock = true, DensityMode = "compact", BackgroundStyle = "contrast" }
                }
            ]
        }, DefaultDashboardTiles());

        var reader = new UiStateRepository();
        await reader.LoadAsync();

        var preferences = reader.GetDashboardPreferences(DefaultDashboardTiles());
        var activeView = Assert.Single(preferences.Views, static view => view.Id == "focus");

        Assert.Equal("focus", preferences.ActiveViewId);
        Assert.Equal(2, preferences.Views.Count);
        Assert.Equal("Attention only", activeView.Title);
        Assert.Equal("service-bus", activeView.Filters.Area);
        Assert.Equal("attention", activeView.Filters.Severity);
        Assert.Equal("snapshot", activeView.Filters.LiveMode);
        Assert.False(activeView.Layout.ShowKpiRibbon);
        Assert.True(activeView.Layout.CollapseInsightDock);
        Assert.Equal("compact", activeView.Layout.DensityMode);
        Assert.Equal("contrast", activeView.Layout.BackgroundStyle);
        Assert.Equal(activeView.Tiles.Select(static tile => tile.TileId), preferences.Tiles.Select(static tile => tile.TileId));
        Assert.Equal("1x1", activeView.Tiles[0].Size);
        Assert.Equal("2x2", activeView.Tiles[1].Size);
    }

    private static IReadOnlyList<DashboardTilePreference> DefaultDashboardTiles() =>
    [
        new DashboardTilePreference { TileId = "shell.favorites", IsVisible = true, Size = "3x2" },
        new DashboardTilePreference { TileId = "shell.recent-resources", IsVisible = false, Size = "3x2" },
        new DashboardTilePreference { TileId = "service-bus.dead-letters", IsVisible = true, Size = "1x1" },
        new DashboardTilePreference { TileId = "service-bus.entity-watch", IsVisible = false, Size = "2x1" }
    ];
}
