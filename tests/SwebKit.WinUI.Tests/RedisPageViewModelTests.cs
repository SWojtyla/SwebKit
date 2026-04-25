using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;
using SwebKit.WinUI.ViewModels.Redis;
using System.Reflection;

namespace SwebKit.WinUI.Tests;

public sealed class RedisPageViewModelTests
{
    [Fact]
    public async Task AnalyzeKeyspaceHealth_PopulatesFindingsForLoadedKeys()
    {
        var fakeClient = TestRedisClient.CreateDefault();
        var viewModel = CreateViewModel(fakeClient, isProduction: false);

        await viewModel.LoadAsync();
        await viewModel.AnalyzeKeyspaceHealthCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasHealthReport);
        Assert.NotEmpty(viewModel.HealthFindings);
        Assert.Contains(viewModel.HealthFindings, item => item.Finding.RiskType == RedisHealthRiskType.NoTtl);
        Assert.Contains("Coverage:", viewModel.HealthCoverageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzePrefixMemory_PopulatesBucketsForLoadedKeys()
    {
        var fakeClient = TestRedisClient.CreateDefault();
        var viewModel = CreateViewModel(fakeClient, isProduction: false);

        await viewModel.LoadAsync();
        await viewModel.AnalyzePrefixMemoryCommand.ExecuteAsync(null);

        Assert.NotEmpty(viewModel.PrefixMemoryBuckets);
        Assert.Contains(viewModel.PrefixMemoryBuckets, bucket => string.Equals(bucket.Prefix, "app", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadSlowLogAndPubSub_PopulatesInsightsCollections()
    {
        var fakeClient = TestRedisClient.CreateDefault();
        var viewModel = CreateViewModel(fakeClient, isProduction: false);

        await viewModel.LoadAsync();
        await viewModel.LoadSlowLogCommand.ExecuteAsync(null);
        await viewModel.LoadPubSubCommand.ExecuteAsync(null);

        Assert.NotEmpty(viewModel.SlowLogEntries);
        Assert.NotEmpty(viewModel.HotKeySignals);
        Assert.NotEmpty(viewModel.PubSubChannels);
        Assert.Contains("Loaded", viewModel.SlowLogSummaryText, StringComparison.Ordinal);
        Assert.Contains("channel", viewModel.PubSubSummaryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reload_CancelsInFlightSlowLogWithoutWritingStaleResults()
    {
        var fakeClient = TestRedisClient.CreateWithCancelableSlowLog();
        var viewModel = CreateViewModel(fakeClient, isProduction: false);

        await viewModel.LoadAsync();

        var slowLogTask = viewModel.LoadSlowLogCommand.ExecuteAsync(null);
        await Task.Yield();

        await viewModel.ReloadCommand.ExecuteAsync(null);
        await slowLogTask;

        Assert.Empty(viewModel.SlowLogEntries);
        Assert.Empty(viewModel.HotKeySignals);
        Assert.Equal("Load slowlog to inspect expensive Redis commands and inferred hot keys.", viewModel.SlowLogSummaryText);
    }

    [Fact]
    public async Task DeleteSelectedKeys_RemovesLoadedKeysAndClearsSelectionMode()
    {
        var fakeClient = TestRedisClient.CreateDefault();
        var viewModel = CreateViewModel(fakeClient, isProduction: false);

        await viewModel.LoadAsync();

        viewModel.ToggleSelectionModeCommand.Execute(null);
        viewModel.SelectAllLoadedCommand.Execute(null);

        Assert.True(viewModel.IsSelectionMode);
        Assert.True(viewModel.SelectedKeyCount > 0);

        await viewModel.DeleteSelectedKeysCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsSelectionMode);
        Assert.Equal(0, viewModel.SelectedKeyCount);
        Assert.Empty(fakeClient.Keys);
        Assert.Empty(viewModel.TreeRows);
        Assert.Contains("No keys loaded", viewModel.ScanSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteSelectedKeys_InProductionRequiresConfirmText()
    {
        var fakeClient = TestRedisClient.CreateDefault();
        var viewModel = CreateViewModel(fakeClient, isProduction: true);

        await viewModel.LoadAsync();

        viewModel.ToggleSelectionModeCommand.Execute(null);
        viewModel.SelectAllLoadedCommand.Execute(null);

        Assert.False(viewModel.CanDeleteSelectedKeys);

        await viewModel.DeleteSelectedKeysCommand.ExecuteAsync(null);

        Assert.NotEmpty(fakeClient.Keys);
        Assert.Equal(3, viewModel.SelectedKeyCount);
        Assert.Equal("Type CONFIRM before deleting selected production keys.", viewModel.ErrorMessage);

        viewModel.BulkDeleteConfirmText = "CONFIRM";

        await viewModel.DeleteSelectedKeysCommand.ExecuteAsync(null);

        Assert.Empty(fakeClient.Keys);
        Assert.False(viewModel.IsSelectionMode);
    }

    [Fact]
    public async Task DeleteSelectedKeys_DisablesReloadAndCacheSwitchUntilComplete()
    {
        var fakeClient = TestRedisClient.CreateWithBlockingDelete();
        var viewModel = CreateViewModel(fakeClient, isProduction: false);

        await viewModel.LoadAsync();

        viewModel.ToggleSelectionModeCommand.Execute(null);
        viewModel.SelectAllLoadedCommand.Execute(null);

        var deleteTask = viewModel.DeleteSelectedKeysCommand.ExecuteAsync(null);
        await fakeClient.WaitForDeleteStartedAsync();

        Assert.False(viewModel.CanReload);
        Assert.False(viewModel.CanChangeCache);

        fakeClient.ReleaseDelete();
        await deleteTask;

        Assert.True(viewModel.CanReload);
        Assert.True(viewModel.CanChangeCache);
    }

    private static RedisPageViewModel CreateViewModel(TestRedisClient fakeClient, bool isProduction)
    {
        var profileRepository = new ProfileRepository();
        var uiStateRepository = new UiStateRepository();
        var appState = new AppStateService(
            profileRepository,
            uiStateRepository,
            new AppEventBus(NullLogger<AppEventBus>.Instance));

        MarkInitialized(appState);

        appState.Config.IsProduction = isProduction;

        var redisConfig = new RedisConfig
        {
            NamespaceSeparator = ":",
        };

        var cacheEntry = new RedisCacheEntry
        {
            Id = "cache1",
            DisplayName = "Primary Redis",
            ConnectionString = "localhost:6379",
            Database = 0,
        };

        redisConfig.Caches.Add(cacheEntry);
        redisConfig.ActiveCacheId = cacheEntry.Id;
        appState.Config.RedisConfig = redisConfig;

        var navigation = new TestShellNavigationService();
        var workspaceService = new OperatorWorkspaceService(
            appState,
            uiStateRepository,
            navigation,
            Array.Empty<IOperatorResourceSearchProvider>());

        return new RedisPageViewModel(
            appState,
            new TestRedisClientFactory(fakeClient),
            new RedisOpsInsightsAggregator(),
            new TestNotificationService(),
            workspaceService,
            NullLogger<RedisPageViewModel>.Instance);
    }

    private static void MarkInitialized(AppStateService appState)
    {
        var initializedField = typeof(AppStateService).GetField("<IsInitialized>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        initializedField?.SetValue(appState, true);

        var initializedTcsField = typeof(AppStateService).GetField("_initializedTcs", BindingFlags.Instance | BindingFlags.NonPublic);
        var initializedTcs = (TaskCompletionSource?)initializedTcsField?.GetValue(appState);
        initializedTcs?.TrySetResult();
    }

    private sealed class TestRedisClientFactory(TestRedisClient client) : IRedisClientFactory
    {
        public Task<IRedisClient> CreateAsync(RedisCacheEntry cacheEntry, CancellationToken ct = default) => Task.FromResult<IRedisClient>(client);
    }

    private sealed class TestRedisClient : IRedisClient
    {
        private readonly Dictionary<string, RedisKeyInfo> _keyInfos;
        private readonly Dictionary<string, string?> _stringValues;
        private readonly RedisSlowLogSummary _slowLogSummary;
        private readonly RedisPubSubSnapshot _pubSubSnapshot;

        private TestRedisClient(
            Dictionary<string, RedisKeyInfo> keyInfos,
            Dictionary<string, string?> stringValues,
            RedisSlowLogSummary slowLogSummary,
            RedisPubSubSnapshot pubSubSnapshot)
        {
            _keyInfos = keyInfos;
            _stringValues = stringValues;
            _slowLogSummary = slowLogSummary;
            _pubSubSnapshot = pubSubSnapshot;
        }

        public IReadOnlyCollection<string> Keys => _keyInfos.Keys;

        public static TestRedisClient CreateDefault()
        {
            return CreateCore(blockSlowLogUntilCanceled: false);
        }

        public static TestRedisClient CreateWithCancelableSlowLog()
        {
            return CreateCore(blockSlowLogUntilCanceled: true);
        }

        public static TestRedisClient CreateWithBlockingDelete()
        {
            var client = CreateCore(blockSlowLogUntilCanceled: false);
            client.BlockDeleteUntilReleased = true;
            return client;
        }

        private static TestRedisClient CreateCore(bool blockSlowLogUntilCanceled)
        {
            var keyInfos = new Dictionary<string, RedisKeyInfo>(StringComparer.Ordinal)
            {
                ["app:cache:1"] = new()
                {
                    Key = "app:cache:1",
                    Type = "string",
                    Ttl = null,
                    MemoryBytes = 320_000,
                    Encoding = "raw",
                    Frequency = 40,
                    IdleSeconds = 4,
                },
                ["app:cache:2"] = new()
                {
                    Key = "app:cache:2",
                    Type = "string",
                    Ttl = TimeSpan.FromMinutes(30),
                    MemoryBytes = 96_000,
                    Encoding = "raw",
                    Frequency = 10,
                    IdleSeconds = 12,
                },
                ["user:profile:1"] = new()
                {
                    Key = "user:profile:1",
                    Type = "hash",
                    Ttl = TimeSpan.FromHours(2),
                    MemoryBytes = 2_048,
                    Encoding = "hashtable",
                    Frequency = 1,
                    IdleSeconds = 300,
                },
            };

            var stringValues = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["app:cache:1"] = "primary cached payload",
                ["app:cache:2"] = "secondary cached payload",
            };

            var slowLogSummary = new RedisSlowLogSummary(
                [
                    new RedisSlowLogEntryInfo(
                        1,
                        DateTimeOffset.UtcNow,
                        TimeSpan.FromMilliseconds(12),
                        "GET",
                        "app:cache:1",
                        "tests")
                ],
                false,
                128,
                RedisInsightCapability.Loaded);

            var pubSubSnapshot = new RedisPubSubSnapshot(
                [
                    new RedisPubSubChannelInfo("cache:updates", 2),
                    new RedisPubSubChannelInfo("cache:invalidations", 1),
                ],
                1,
                false,
                200,
                RedisInsightCapability.Loaded);

            return new TestRedisClient(keyInfos, stringValues, slowLogSummary, pubSubSnapshot)
            {
                BlockSlowLogUntilCanceled = blockSlowLogUntilCanceled,
            };
        }

        public bool BlockSlowLogUntilCanceled { get; private set; }

        public bool BlockDeleteUntilReleased { get; private set; }

        private TaskCompletionSource DeleteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource DeleteRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForDeleteStartedAsync() => DeleteStarted.Task;

        public void ReleaseDelete() => DeleteRelease.TrySetResult();

        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<KeyScanResult> ScanKeysAsync(string pattern = "*", long cursor = 0, int pageSize = 100, CancellationToken ct = default)
        {
            var keys = _keyInfos.Keys
                .Where(key => MatchesPattern(key, pattern))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(new KeyScanResult
            {
                Cursor = 0,
                Keys = keys,
                IsComplete = true,
            });
        }

        public Task<string> GetKeyTypeAsync(string key, CancellationToken ct = default) => Task.FromResult(_keyInfos[key].Type);

        public Task<RedisKeyInfo> GetKeyInfoAsync(string key, CancellationToken ct = default) => Task.FromResult(CloneKeyInfo(_keyInfos[key]));

        public Task<string?> GetKeyValueAsync(string key, CancellationToken ct = default) => Task.FromResult(_stringValues.GetValueOrDefault(key));

        public Task<IReadOnlyList<RedisHashField>> GetHashFieldsAsync(string key, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RedisHashField>>([
            new RedisHashField { Field = "id", Value = "1" },
            new RedisHashField { Field = "name", Value = "Ada" },
        ]);

        public Task<IReadOnlyList<string>> GetListItemsAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetSetMembersAsync(string key, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<RedisSortedSetEntry>> GetSortedSetMembersAsync(string key, long start = 0, long stop = -1, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RedisSortedSetEntry>>([]);

        public Task SetKeyValueAsync(string key, string value, TimeSpan? expiry = null, CancellationToken ct = default)
        {
            _stringValues[key] = value;
            return Task.CompletedTask;
        }

        public Task SetHashFieldAsync(string key, string field, string value, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default)
        {
            if (BlockDeleteUntilReleased)
            {
                DeleteStarted.TrySetResult();
                return CompleteBlockedDeleteAsync(keys, ct);
            }

            foreach (var key in keys)
            {
                _keyInfos.Remove(key);
                _stringValues.Remove(key);
            }

            return Task.CompletedTask;
        }

        private async Task CompleteBlockedDeleteAsync(IReadOnlyList<string> keys, CancellationToken ct)
        {
            await DeleteRelease.Task.WaitAsync(ct);

            foreach (var key in keys)
            {
                _keyInfos.Remove(key);
                _stringValues.Remove(key);
            }
        }

        public Task<TimeSpan?> GetTtlAsync(string key, CancellationToken ct = default) => Task.FromResult(_keyInfos[key].Ttl);

        public Task SetTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default)
        {
            _keyInfos[key].Ttl = ttl;
            return Task.CompletedTask;
        }

        public Task RemoveTtlAsync(string key, CancellationToken ct = default)
        {
            _keyInfos[key].Ttl = null;
            return Task.CompletedTask;
        }

        public Task FlushDatabaseAsync(CancellationToken ct = default)
        {
            _keyInfos.Clear();
            _stringValues.Clear();
            return Task.CompletedTask;
        }

        public Task<RedisServerInfo> GetServerInfoAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new RedisServerInfo
            {
                Databases = [new RedisDatabaseInfo { Index = 0, Keys = _keyInfos.Count }],
            });
        }

        public Task UpdateSortedSetScoreAsync(string key, string member, double score, CancellationToken ct = default) => Task.CompletedTask;

        public Task RenameKeyAsync(string oldKey, string newKey, CancellationToken ct = default)
        {
            var keyInfo = _keyInfos[oldKey];
            _keyInfos.Remove(oldKey);
            keyInfo.Key = newKey;
            _keyInfos[newKey] = keyInfo;

            if (_stringValues.TryGetValue(oldKey, out var value))
            {
                _stringValues.Remove(oldKey);
                _stringValues[newKey] = value;
            }

            return Task.CompletedTask;
        }

        public Task DeleteHashFieldAsync(string key, string field, CancellationToken ct = default) => Task.CompletedTask;

        public Task<SetScanResult> GetSetMembersPageAsync(string key, long cursor, int pageSize, CancellationToken ct = default) => Task.FromResult(new SetScanResult([], 0, true));

        public async Task<RedisSlowLogSummary> GetSlowLogAsync(int top = 128, CancellationToken ct = default)
        {
            if (BlockSlowLogUntilCanceled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }

            return _slowLogSummary;
        }

        public Task<RedisPubSubSnapshot> GetPubSubSnapshotAsync(string? pattern = null, int maxChannels = 200, CancellationToken ct = default) => Task.FromResult(_pubSubSnapshot);

        public void Dispose()
        {
        }

        private static bool MatchesPattern(string key, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern) || string.Equals(pattern, "*", StringComparison.Ordinal))
            {
                return true;
            }

            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            return System.Text.RegularExpressions.Regex.IsMatch(key, regex, System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }

        private static RedisKeyInfo CloneKeyInfo(RedisKeyInfo source) => new()
        {
            Key = source.Key,
            Type = source.Type,
            Ttl = source.Ttl,
            MemoryBytes = source.MemoryBytes,
            Encoding = source.Encoding,
            Frequency = source.Frequency,
            IdleSeconds = source.IdleSeconds,
        };
    }

    private sealed class TestShellNavigationService : IShellNavigationService
    {
        public string? CurrentArea { get; private set; }

        public event Action? NavigationChanged;

        public void NavigateTo(string area)
        {
            CurrentArea = area;
            NavigationChanged?.Invoke();
        }
    }

    private sealed class TestNotificationService : INotificationService
    {
        private readonly List<Notification> _all = [];

        public IReadOnlyList<Notification> All => _all;

        public event Action? NotificationsChanged;

        public void ShowSuccess(string message, string? detail = null) => Add(NotificationSeverity.Success, message, detail);

        public void ShowWarning(string message, string? detail = null) => Add(NotificationSeverity.Warning, message, detail);

        public void ShowError(string message, string? detail = null, Exception? ex = null) => Add(NotificationSeverity.Error, message, detail ?? ex?.Message);

        public void ShowInfo(string message, string? detail = null) => Add(NotificationSeverity.Info, message, detail);

        public void Dismiss(Guid id)
        {
            _all.RemoveAll(candidate => candidate.Id == id);
            NotificationsChanged?.Invoke();
        }

        public void ClearAll()
        {
            _all.Clear();
            NotificationsChanged?.Invoke();
        }

        private void Add(NotificationSeverity severity, string message, string? detail)
        {
            _all.Add(new Notification(Guid.NewGuid(), severity, message, detail, DateTimeOffset.UtcNow));
            NotificationsChanged?.Invoke();
        }
    }
}