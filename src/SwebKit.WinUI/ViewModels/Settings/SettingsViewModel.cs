using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;

namespace SwebKit.WinUI.ViewModels.Settings;

public sealed partial class SettingsViewModel : ObservableObject
{
    private const string DevOpsCredentialKey = "swebkit:devops:global:pat";

    private readonly UserSettingsRepository _userSettings;
    private readonly AppStateService _appState;
    private readonly ThemeCoordinator _themeCoordinator;
    private readonly IConfigurationHealthService _configurationHealth;
    private readonly IConfigurationProbeService _configurationProbes;
    private readonly ICredentialStore _credentialStore;
    private readonly IDevOpsClientFactory _devOpsClientFactory;
    private readonly IStorageClientFactory _storageClientFactory;
    private readonly IRedisClientFactory _redisClientFactory;
    private readonly IServiceBusClientFactory _serviceBusClientFactory;

    private ConfigurationAreaHealth? _currentAreaHealth;

    [ObservableProperty]
    public partial SettingsSectionOption? SelectedSectionOption { get; set; }

    [ObservableProperty]
    public partial string SelectedTheme { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsProduction { get; set; }

    [ObservableProperty]
    public partial bool WarmupConnectionsOnStartup { get; set; }

    [ObservableProperty]
    public partial bool IsDemoModeEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsRunningLiveCheck { get; set; }

    [ObservableProperty]
    public partial string? SectionStatusMessage { get; set; }

    [ObservableProperty]
    public partial string AksKubeconfigPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AksKubeconfigContext { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AksDefaultNamespace { get; set; } = "default";

    [ObservableProperty]
    public partial string RedisNamespaceSeparator { get; set; } = "-";

    [ObservableProperty]
    public partial string? SelectedActiveRedisCacheId { get; set; }

    [ObservableProperty]
    public partial string RedisDisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RedisConnectionString { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int RedisDatabase { get; set; }

    [ObservableProperty]
    public partial string? EditingRedisCacheId { get; set; }

    [ObservableProperty]
    public partial string DevOpsOrganization { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DevOpsPat { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasStoredDevOpsPat { get; set; }

    [ObservableProperty]
    public partial string StorageDisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StorageAccountName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StorageConnectionStringRef { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool StorageUseAad { get; set; }

    [ObservableProperty]
    public partial bool StorageAllowMutations { get; set; }

    [ObservableProperty]
    public partial string? EditingStorageAccountId { get; set; }

    [ObservableProperty]
    public partial int ObservabilityMaxRowsPerQuery { get; set; } = 500;

    [ObservableProperty]
    public partial double ObservabilityFailureRateAmberThreshold { get; set; } = 0.01;

    [ObservableProperty]
    public partial double ObservabilityFailureRateRedThreshold { get; set; } = 0.05;

    [ObservableProperty]
    public partial double ObservabilityLatencyAmberThresholdMs { get; set; } = 500;

    [ObservableProperty]
    public partial double ObservabilityLatencyRedThresholdMs { get; set; } = 2000;

    public SettingsViewModel(
        UserSettingsRepository userSettings,
        AppStateService appState,
        ThemeCoordinator themeCoordinator,
        IConfigurationHealthService configurationHealth,
        IConfigurationProbeService configurationProbes,
        ICredentialStore credentialStore,
        IDevOpsClientFactory devOpsClientFactory,
        IStorageClientFactory storageClientFactory,
        IRedisClientFactory redisClientFactory,
        IServiceBusClientFactory serviceBusClientFactory)
    {
        _userSettings = userSettings;
        _appState = appState;
        _themeCoordinator = themeCoordinator;
        _configurationHealth = configurationHealth;
        _configurationProbes = configurationProbes;
        _serviceBusClientFactory = serviceBusClientFactory;
        _credentialStore = credentialStore;
        _devOpsClientFactory = devOpsClientFactory;
        _storageClientFactory = storageClientFactory;
        _redisClientFactory = redisClientFactory;

        ThemeOptions = _themeCoordinator.ThemeOptions;

        Sections =
        [
            new(SettingsSections.Appearance, "Appearance", "Theme, startup warmup, demo mode, and production safety defaults."),
            new(SettingsSections.ServiceBus, "Service Bus", "Add or remove namespace connections, review credential health, and manage pinned entity links."),
            new(SettingsSections.Aks, "AKS", "Kubeconfig defaults and namespace bootstrap settings."),
            new(SettingsSections.Redis, "Redis", "Active cache selection, connection profiles, and namespace separator."),
            new(SettingsSections.DevOps, "Azure DevOps", "Organization, PAT, and pipeline readiness configuration."),
            new(SettingsSections.Storage, "Storage", "Blob account access profiles and mutation safeguards."),
            new(SettingsSections.Observability, "Observability", "Application Insights identity guidance and threshold tuning."),
            new(SettingsSections.IncidentTimeline, "Incident Timeline", "Deferred in the current WinUI settings parity wave."),
        ];
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public ObservableCollection<SettingsSectionOption> Sections { get; } = [];

    [ObservableProperty]
    public partial string ServiceBusConnectionStringInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ServiceBusAddError { get; set; }

    public Visibility ServiceBusAddErrorVisibility => ToVisibility(!string.IsNullOrWhiteSpace(ServiceBusAddError));

    public ObservableCollection<ServiceBusNamespaceStatusItem> ServiceBusNamespaces { get; } = [];

    public ObservableCollection<ServiceBusPinnedEntityItem> ServiceBusPinnedEntities { get; } = [];

    public ObservableCollection<RedisCacheDisplayItem> RedisCaches { get; } = [];

    public ObservableCollection<StorageAccountDisplayItem> StorageAccounts { get; } = [];

    public ObservableCollection<CredentialReferenceHealth> CurrentCredentialReferences { get; } = [];

    public ObservableCollection<ConfigurationActionItem> CurrentActionItems { get; } = [];

    public string CurrentSectionKey => SelectedSectionOption?.Key ?? SettingsSections.Appearance;

    public string CurrentSectionTitle => SelectedSectionOption?.Title ?? "Settings";

    public string CurrentSectionSubtitle => SelectedSectionOption?.Description ?? "Manage the current WinUI operator settings surface.";

    public string CurrentAreaStatusText => _currentAreaHealth is null ? "Unknown" : FormatStatus(_currentAreaHealth.Status);

    public string CurrentAreaSummary => _currentAreaHealth?.Summary ?? "Select a settings section to review configuration readiness.";

    public string CurrentAreaDetail => _currentAreaHealth?.Detail ?? string.Empty;

    public string CurrentAreaProbeSummary => _currentAreaHealth?.LiveProbe is null
        ? "No live check has been recorded for this section in the current session."
        : $"{FormatStatus(_currentAreaHealth.LiveProbe.Status)} live check at {_currentAreaHealth.LiveProbe.CheckedAt.LocalDateTime:g}: {_currentAreaHealth.LiveProbe.Summary}";

    public string RunLiveCheckButtonLabel => IsRunningLiveCheck ? "Checking..." : "Run live check";

    public string RedisEditorTitle => string.IsNullOrWhiteSpace(EditingRedisCacheId) ? "Add cache" : "Edit cache";

    public string StorageEditorTitle => string.IsNullOrWhiteSpace(EditingStorageAccountId) ? "Add account" : "Edit account";

    public Visibility AppearanceVisibility => ToVisibility(CurrentSectionKey == SettingsSections.Appearance);

    public Visibility ServiceBusVisibility => ToVisibility(CurrentSectionKey == SettingsSections.ServiceBus);

    public Visibility AksVisibility => ToVisibility(CurrentSectionKey == SettingsSections.Aks);

    public Visibility RedisVisibility => ToVisibility(CurrentSectionKey == SettingsSections.Redis);

    public Visibility DevOpsVisibility => ToVisibility(CurrentSectionKey == SettingsSections.DevOps);

    public Visibility StorageVisibility => ToVisibility(CurrentSectionKey == SettingsSections.Storage);

    public Visibility ObservabilityVisibility => ToVisibility(CurrentSectionKey == SettingsSections.Observability);

    public Visibility IncidentTimelineVisibility => ToVisibility(CurrentSectionKey == SettingsSections.IncidentTimeline);

    public Visibility RunLiveCheckVisibility => ToVisibility(_currentAreaHealth?.CanRunLiveProbe ?? false);

    public Visibility SectionStatusVisibility => ToVisibility(!string.IsNullOrWhiteSpace(SectionStatusMessage));

    public Visibility CurrentAreaDetailVisibility => ToVisibility(!string.IsNullOrWhiteSpace(CurrentAreaDetail));

    public Visibility CurrentAreaProbeSummaryVisibility => ToVisibility(!string.IsNullOrWhiteSpace(CurrentAreaProbeSummary));

    public Visibility CurrentCredentialReferencesVisibility => ToVisibility(CurrentCredentialReferences.Count > 0);

    public Visibility CurrentActionItemsVisibility => ToVisibility(CurrentActionItems.Count > 0);

    public Visibility ServiceBusNamespacesEmptyVisibility => ToVisibility(ServiceBusNamespaces.Count == 0);

    public Visibility ServiceBusPinnedEntitiesEmptyVisibility => ToVisibility(ServiceBusPinnedEntities.Count == 0);

    public Visibility RedisCachesEmptyVisibility => ToVisibility(RedisCaches.Count == 0);

    public Visibility RedisEditCancelVisibility => ToVisibility(!string.IsNullOrWhiteSpace(EditingRedisCacheId));

    public Visibility StorageAccountsEmptyVisibility => ToVisibility(StorageAccounts.Count == 0);

    public Visibility StorageEditCancelVisibility => ToVisibility(!string.IsNullOrWhiteSpace(EditingStorageAccountId));

    public Visibility StoredDevOpsPatVisibility => ToVisibility(HasStoredDevOpsPat);

    public async Task LoadAsync(SettingsNavigationRequest? request = null)
    {
        await _appState.WhenInitializedAsync();
        await _userSettings.LoadAsync();

        LoadFromState();
        SetSelectedSection(request?.Section);
    }

    partial void OnSelectedSectionOptionChanged(SettingsSectionOption? value)
    {
        SectionStatusMessage = null;
        NotifySectionSelectionChanged();
        RefreshReadiness();
    }

    partial void OnServiceBusAddErrorChanged(string? value) => OnPropertyChanged(nameof(ServiceBusAddErrorVisibility));

    partial void OnIsRunningLiveCheckChanged(bool value) => OnPropertyChanged(nameof(RunLiveCheckButtonLabel));

    partial void OnHasStoredDevOpsPatChanged(bool value) => OnPropertyChanged(nameof(StoredDevOpsPatVisibility));

    partial void OnEditingRedisCacheIdChanged(string? value)
    {
        OnPropertyChanged(nameof(RedisEditorTitle));
        OnPropertyChanged(nameof(RedisEditCancelVisibility));
    }

    partial void OnEditingStorageAccountIdChanged(string? value)
    {
        OnPropertyChanged(nameof(StorageEditorTitle));
        OnPropertyChanged(nameof(StorageEditCancelVisibility));
    }

    [RelayCommand]
    private async Task RunLiveCheckAsync()
    {
        if (IsRunningLiveCheck || !(_currentAreaHealth?.CanRunLiveProbe ?? false))
        {
            return;
        }

        IsRunningLiveCheck = true;
        try
        {
            var baseContext = CreateReadinessContext(includeLatestProbe: false);
            await _configurationProbes.RunAsync(baseContext);
            RefreshReadiness();
            SetStatus("Live check completed.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
        finally
        {
            IsRunningLiveCheck = false;
        }
    }

    [RelayCommand]
    private async Task SaveAppearanceAsync()
    {
        _userSettings.Settings.Theme = SelectedTheme;
        _userSettings.Settings.WarmupConnectionsOnStartup = WarmupConnectionsOnStartup;
        await _userSettings.SaveAsync();
        _themeCoordinator.ApplyTheme(SelectedTheme);

        await _appState.SetDemoModeAsync(IsDemoModeEnabled);
        _appState.Config.IsProduction = IsProduction;

        await PersistConfigAsync("Shell settings saved.");
    }

    [RelayCommand]
    private async Task SaveAksAsync()
    {
        var config = _appState.Config.AksConfig ??= new AksConfig();
        config.KubeconfigPath = NormalizeOptional(AksKubeconfigPath);
        config.KubeconfigContext = NormalizeOptional(AksKubeconfigContext);
        config.DefaultNamespace = string.IsNullOrWhiteSpace(AksDefaultNamespace) ? "default" : AksDefaultNamespace.Trim();

        await PersistConfigAsync("AKS settings saved.");
    }

    [RelayCommand]
    private async Task RemoveServiceBusLinkAsync(ServiceBusPinnedEntityItem? item)
    {
        if (item is null)
        {
            return;
        }

        _appState.Config.ServiceBusEntityLinks.Remove(item.Link);
        await PersistConfigAsync("Pinned Service Bus entity removed.");
    }

    [RelayCommand]
    private async Task AddServiceBusNamespaceAsync()
    {
        var raw = ServiceBusConnectionStringInput.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            ServiceBusAddError = "Connection string is required.";
            return;
        }

        string fullyQualifiedNamespace;
        try
        {
            fullyQualifiedNamespace = _serviceBusClientFactory.ParseFullyQualifiedNamespace(raw);
        }
        catch (Exception ex)
        {
            ServiceBusAddError = $"Invalid connection string: {ex.Message}";
            return;
        }

        if (_appState.ServiceBusNamespaces.Any(ns => string.Equals(ns.FullyQualifiedNamespace, fullyQualifiedNamespace, StringComparison.OrdinalIgnoreCase)))
        {
            ServiceBusAddError = "This namespace is already added.";
            return;
        }

        ServiceBusAddError = null;

        var credentialKey = $"sb:ns:{Guid.NewGuid()}";
        _credentialStore.Save(credentialKey, raw);

        var serviceBusNamespace = new ServiceBusNamespace
        {
            Alias = fullyQualifiedNamespace.Split('.')[0],
            FullyQualifiedNamespace = fullyQualifiedNamespace,
            CredentialKey = credentialKey,
        };

        await _appState.AddServiceBusNamespaceAsync(serviceBusNamespace);
        ServiceBusConnectionStringInput = string.Empty;
        LoadServiceBusState();
        SetStatus("Namespace added.");
    }

    [RelayCommand]
    private async Task RemoveServiceBusNamespaceAsync(ServiceBusNamespaceStatusItem? item)
    {
        if (item is null)
        {
            return;
        }

        _credentialStore.Delete(item.CredentialKey);
        await _appState.RemoveServiceBusNamespaceAsync(item.Namespace.Id);
        LoadServiceBusState();
        SetStatus("Namespace removed.");
    }

    [RelayCommand]
    private void EditRedisCache(RedisCacheDisplayItem? item)
    {
        if (item is null)
        {
            return;
        }

        EditingRedisCacheId = item.Cache.Id;
        RedisDisplayName = item.Cache.DisplayName;
        RedisConnectionString = item.Cache.ConnectionString;
        RedisDatabase = item.Cache.Database;
        SectionStatusMessage = null;
    }

    [RelayCommand]
    private void CancelRedisEdit() => ResetRedisEditor();

    [RelayCommand]
    private async Task RemoveRedisCacheAsync(RedisCacheDisplayItem? item)
    {
        if (item is null)
        {
            return;
        }

        var config = _appState.Config.RedisConfig;
        if (config is null)
        {
            return;
        }

        config.EnsureMigrated();
        config.Caches.Remove(item.Cache);
        if (string.Equals(config.ActiveCacheId, item.Cache.Id, StringComparison.Ordinal))
        {
            config.ActiveCacheId = config.Caches.FirstOrDefault()?.Id;
        }

        if (string.Equals(EditingRedisCacheId, item.Cache.Id, StringComparison.Ordinal))
        {
            ResetRedisEditor();
        }

        await PersistConfigAsync("Redis cache removed.");
    }

    [RelayCommand]
    private async Task SaveRedisAsync()
    {
        var config = _appState.Config.RedisConfig ??= new RedisConfig();
        config.EnsureMigrated();
        config.NamespaceSeparator = string.IsNullOrWhiteSpace(RedisNamespaceSeparator) ? "-" : RedisNamespaceSeparator.Trim();

        var hasEditorDraft = !string.IsNullOrWhiteSpace(EditingRedisCacheId)
            || !string.IsNullOrWhiteSpace(RedisDisplayName)
            || !string.IsNullOrWhiteSpace(RedisConnectionString);

        if (hasEditorDraft)
        {
            if (string.IsNullOrWhiteSpace(RedisDisplayName))
            {
                SetStatus("Redis display name is required.");
                return;
            }

            if (string.IsNullOrWhiteSpace(RedisConnectionString))
            {
                SetStatus("Redis connection string is required.");
                return;
            }

            var normalizedDatabase = Math.Clamp(RedisDatabase, 0, 15);
            if (!string.IsNullOrWhiteSpace(EditingRedisCacheId))
            {
                var existing = config.Caches.FirstOrDefault(candidate => string.Equals(candidate.Id, EditingRedisCacheId, StringComparison.Ordinal));
                if (existing is not null)
                {
                    existing.DisplayName = RedisDisplayName.Trim();
                    existing.ConnectionString = RedisConnectionString.Trim();
                    existing.Database = normalizedDatabase;
                }
            }
            else
            {
                var cache = new RedisCacheEntry
                {
                    DisplayName = RedisDisplayName.Trim(),
                    ConnectionString = RedisConnectionString.Trim(),
                    Database = normalizedDatabase,
                };

                config.Caches.Add(cache);
                SelectedActiveRedisCacheId ??= cache.Id;
            }
        }

        if (config.Caches.Count > 0)
        {
            config.ActiveCacheId = string.IsNullOrWhiteSpace(SelectedActiveRedisCacheId)
                ? config.Caches[0].Id
                : SelectedActiveRedisCacheId;
        }

        await PersistConfigAsync("Redis settings saved.");
        ResetRedisEditor();
        LoadRedisState();
    }

    [RelayCommand]
    private async Task TestRedisConnectionAsync()
    {
        var probeCache = BuildRedisProbeCache();
        if (probeCache is null)
        {
            SetStatus("Enter a Redis cache connection string before testing.");
            return;
        }

        try
        {
            using var client = await _redisClientFactory.CreateAsync(probeCache);
            var connected = await client.TestConnectionAsync();
            SetStatus(connected ? "Redis connection succeeded." : "Redis connection failed.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveDevOpsAsync()
    {
        if (string.IsNullOrWhiteSpace(DevOpsOrganization))
        {
            SetStatus("Azure DevOps organization is required.");
            return;
        }

        var existingCredentialKey = string.IsNullOrWhiteSpace(_appState.Config.DevOpsConfig?.PatCredentialKey)
            ? DevOpsCredentialKey
            : _appState.Config.DevOpsConfig!.PatCredentialKey;

        var storedPat = _credentialStore.Get(existingCredentialKey);
        if (!string.IsNullOrWhiteSpace(DevOpsPat))
        {
            _credentialStore.Save(DevOpsCredentialKey, DevOpsPat.Trim());
            storedPat = DevOpsPat.Trim();
            existingCredentialKey = DevOpsCredentialKey;
        }

        if (string.IsNullOrWhiteSpace(storedPat))
        {
            SetStatus("Enter a PAT before saving Azure DevOps settings.");
            return;
        }

        var existingConfig = _appState.Config.DevOpsConfig;
        var config = new DevOpsConfig
        {
            Organization = DevOpsOrganization.Trim(),
            PatCredentialKey = existingCredentialKey,
            PinnedProjects = existingConfig?.PinnedProjects?.ToList() ?? [],
            PipelineGroups = existingConfig?.PipelineGroups?.ToList() ?? [],
        };

        try
        {
            _ = _devOpsClientFactory.Create(config);
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message);
            return;
        }

        _appState.Config.DevOpsConfig = config;
        await PersistConfigAsync("Azure DevOps settings saved.");
    }

    [RelayCommand]
    private async Task TestDevOpsConnectionAsync()
    {
        var existingCredentialKey = string.IsNullOrWhiteSpace(_appState.Config.DevOpsConfig?.PatCredentialKey)
            ? DevOpsCredentialKey
            : _appState.Config.DevOpsConfig!.PatCredentialKey;

        var effectivePat = string.IsNullOrWhiteSpace(DevOpsPat)
            ? _credentialStore.Get(existingCredentialKey)
            : DevOpsPat.Trim();

        if (string.IsNullOrWhiteSpace(DevOpsOrganization) || string.IsNullOrWhiteSpace(effectivePat))
        {
            SetStatus("Enter both the organization and PAT before testing Azure DevOps access.");
            return;
        }

        try
        {
            var testCredentialKey = existingCredentialKey;
            if (!string.IsNullOrWhiteSpace(DevOpsPat))
            {
                testCredentialKey = $"{DevOpsCredentialKey}:test";
                _credentialStore.Save(testCredentialKey, DevOpsPat.Trim());
            }

            try
            {
                var client = _devOpsClientFactory.Create(new DevOpsConfig
                {
                    Organization = DevOpsOrganization.Trim(),
                    PatCredentialKey = testCredentialKey,
                });

                var connected = await client.TestConnectionAsync();
                SetStatus(connected
                    ? "Azure DevOps connection succeeded."
                    : "Azure DevOps connection failed. Check the PAT scope and organization URL.");
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(DevOpsPat))
                {
                    _credentialStore.Delete(testCredentialKey);
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    [RelayCommand]
    private void EditStorageAccount(StorageAccountDisplayItem? item)
    {
        if (item is null)
        {
            return;
        }

        EditingStorageAccountId = item.Config.Id;
        StorageDisplayName = item.Config.DisplayName;
        StorageAccountName = item.Config.AccountName;
        StorageConnectionStringRef = item.Config.ConnectionStringRef ?? string.Empty;
        StorageUseAad = item.Config.UseAad;
        StorageAllowMutations = item.Config.AllowMutations;
        SectionStatusMessage = null;
    }

    [RelayCommand]
    private void CancelStorageEdit() => ResetStorageEditor();

    [RelayCommand]
    private async Task RemoveStorageAccountAsync(StorageAccountDisplayItem? item)
    {
        if (item is null)
        {
            return;
        }

        _appState.Config.StorageAccounts.Remove(item.Config);
        if (string.Equals(EditingStorageAccountId, item.Config.Id, StringComparison.Ordinal))
        {
            ResetStorageEditor();
        }

        await PersistConfigAsync("Storage account removed.");
    }

    [RelayCommand]
    private async Task SaveStorageAsync()
    {
        if (string.IsNullOrWhiteSpace(StorageDisplayName))
        {
            SetStatus("Storage display name is required.");
            return;
        }

        if (StorageUseAad && string.IsNullOrWhiteSpace(StorageAccountName))
        {
            SetStatus("Storage account name is required when Azure AD is enabled.");
            return;
        }

        if (!StorageUseAad && string.IsNullOrWhiteSpace(StorageConnectionStringRef))
        {
            SetStatus("A connection string reference is required when Azure AD is disabled.");
            return;
        }

        var existing = _appState.Config.StorageAccounts.FirstOrDefault(candidate => string.Equals(candidate.Id, EditingStorageAccountId, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.DisplayName = StorageDisplayName.Trim();
            existing.AccountName = NormalizeOptional(StorageAccountName) ?? string.Empty;
            existing.ConnectionStringRef = NormalizeOptional(StorageConnectionStringRef);
            existing.UseAad = StorageUseAad;
            existing.AllowMutations = StorageAllowMutations;
        }
        else
        {
            _appState.Config.StorageAccounts.Add(BuildStorageConfig());
        }

        await PersistConfigAsync("Storage settings saved.");
        ResetStorageEditor();
        LoadStorageState();
    }

    [RelayCommand]
    private async Task TestStorageConnectionAsync()
    {
        try
        {
            var client = _storageClientFactory.Create(BuildStorageConfig());
            var connected = await client.TestConnectionAsync();
            SetStatus(connected ? "Storage connection succeeded." : "Storage connection failed.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveObservabilityAsync()
    {
        var config = _appState.Config.ObservabilityConfig ??= new ObservabilityConfig();
        config.MaxRowsPerQuery = Math.Clamp(ObservabilityMaxRowsPerQuery, 100, 5000);
        config.FailureRateAmberThreshold = Math.Clamp(ObservabilityFailureRateAmberThreshold, 0, 1);
        config.FailureRateRedThreshold = Math.Clamp(ObservabilityFailureRateRedThreshold, 0, 1);
        config.LatencyAmberThresholdMs = Math.Clamp(ObservabilityLatencyAmberThresholdMs, 0, 60000);
        config.LatencyRedThresholdMs = Math.Clamp(ObservabilityLatencyRedThresholdMs, 0, 60000);

        await PersistConfigAsync("Observability settings saved.");
    }

    private void LoadFromState()
    {
        SelectedTheme = _themeCoordinator.NormalizeThemeKey(_userSettings.Settings.Theme);
        IsProduction = _appState.Config.IsProduction;
        WarmupConnectionsOnStartup = _userSettings.Settings.WarmupConnectionsOnStartup;
        IsDemoModeEnabled = _appState.UseDemoData;

        LoadServiceBusState();
        LoadAksState();
        LoadRedisState();
        LoadDevOpsState();
        LoadStorageState();
        LoadObservabilityState();
        RefreshReadiness();
    }

    private void LoadServiceBusState()
    {
        ReplaceCollection(
            ServiceBusNamespaces,
            _appState.ServiceBusNamespaces
                .OrderBy(static candidate => candidate.Alias)
                .Select(candidate => new ServiceBusNamespaceStatusItem(candidate, !string.IsNullOrWhiteSpace(_credentialStore.Get(candidate.CredentialKey)))));

        ReplaceCollection(
            ServiceBusPinnedEntities,
            _appState.Config.ServiceBusEntityLinks
                .Select(link => new ServiceBusPinnedEntityItem(link, ResolveNamespaceLabel(link.NamespaceId, link.Alias))));

        OnPropertyChanged(nameof(ServiceBusNamespacesEmptyVisibility));
        OnPropertyChanged(nameof(ServiceBusPinnedEntitiesEmptyVisibility));
    }

    private void LoadAksState()
    {
        var config = _appState.Config.AksConfig;
        AksKubeconfigPath = config?.KubeconfigPath ?? string.Empty;
        AksKubeconfigContext = config?.KubeconfigContext ?? string.Empty;
        AksDefaultNamespace = string.IsNullOrWhiteSpace(config?.DefaultNamespace) ? "default" : config.DefaultNamespace;
    }

    private void LoadRedisState()
    {
        _appState.Config.RedisConfig?.EnsureMigrated();
        var config = _appState.Config.RedisConfig;

        RedisNamespaceSeparator = string.IsNullOrWhiteSpace(config?.NamespaceSeparator) ? "-" : config.NamespaceSeparator;
        SelectedActiveRedisCacheId = config?.ActiveCacheId ?? config?.Caches.FirstOrDefault()?.Id;

        ReplaceCollection(
            RedisCaches,
            (config?.Caches ?? [])
                .Select(static cache => new RedisCacheDisplayItem(cache)));

        OnPropertyChanged(nameof(RedisCachesEmptyVisibility));
    }

    private void LoadDevOpsState()
    {
        var config = _appState.Config.DevOpsConfig;
        DevOpsOrganization = config?.Organization ?? string.Empty;
        DevOpsPat = string.Empty;
        HasStoredDevOpsPat = !string.IsNullOrWhiteSpace(config?.PatCredentialKey)
            && !string.IsNullOrWhiteSpace(_credentialStore.Get(config.PatCredentialKey));
    }

    private void LoadStorageState()
    {
        ReplaceCollection(
            StorageAccounts,
            _appState.Config.StorageAccounts.Select(static account => new StorageAccountDisplayItem(account)));

        OnPropertyChanged(nameof(StorageAccountsEmptyVisibility));
    }

    private void LoadObservabilityState()
    {
        var config = _appState.Config.ObservabilityConfig ?? new ObservabilityConfig();
        ObservabilityMaxRowsPerQuery = config.MaxRowsPerQuery;
        ObservabilityFailureRateAmberThreshold = config.FailureRateAmberThreshold;
        ObservabilityFailureRateRedThreshold = config.FailureRateRedThreshold;
        ObservabilityLatencyAmberThresholdMs = config.LatencyAmberThresholdMs;
        ObservabilityLatencyRedThresholdMs = config.LatencyRedThresholdMs;
    }

    private void SetSelectedSection(string? section)
    {
        var normalized = SettingsSections.Normalize(section) ?? SettingsSections.Appearance;
        SelectedSectionOption = Sections.First(option => string.Equals(option.Key, normalized, StringComparison.Ordinal));
    }

    private void NotifySectionSelectionChanged()
    {
        OnPropertyChanged(nameof(CurrentSectionKey));
        OnPropertyChanged(nameof(CurrentSectionTitle));
        OnPropertyChanged(nameof(CurrentSectionSubtitle));
        OnPropertyChanged(nameof(AppearanceVisibility));
        OnPropertyChanged(nameof(ServiceBusVisibility));
        OnPropertyChanged(nameof(AksVisibility));
        OnPropertyChanged(nameof(RedisVisibility));
        OnPropertyChanged(nameof(DevOpsVisibility));
        OnPropertyChanged(nameof(StorageVisibility));
        OnPropertyChanged(nameof(ObservabilityVisibility));
        OnPropertyChanged(nameof(IncidentTimelineVisibility));
        OnPropertyChanged(nameof(RunLiveCheckVisibility));
    }

    private void RefreshReadiness()
    {
        var report = _configurationHealth.BuildReport(CreateReadinessContext(includeLatestProbe: true));
        _currentAreaHealth = report.Areas.FirstOrDefault(area => string.Equals(area.SettingsSection, CurrentSectionKey, StringComparison.Ordinal));

        ReplaceCollection(CurrentCredentialReferences, _currentAreaHealth?.CredentialReferences ?? []);
        ReplaceCollection(CurrentActionItems, _currentAreaHealth?.ActionItems ?? []);

        OnPropertyChanged(nameof(CurrentAreaStatusText));
        OnPropertyChanged(nameof(CurrentAreaSummary));
        OnPropertyChanged(nameof(CurrentAreaDetail));
        OnPropertyChanged(nameof(CurrentAreaDetailVisibility));
        OnPropertyChanged(nameof(CurrentAreaProbeSummary));
        OnPropertyChanged(nameof(CurrentAreaProbeSummaryVisibility));
        OnPropertyChanged(nameof(CurrentCredentialReferencesVisibility));
        OnPropertyChanged(nameof(CurrentActionItemsVisibility));
        OnPropertyChanged(nameof(RunLiveCheckVisibility));
    }

    private ConfigurationHealthContext CreateReadinessContext(bool includeLatestProbe)
    {
        var baseContext = new ConfigurationHealthContext(
            _appState.Config,
            _appState.ServiceBusNamespaces,
            _appState.UseDemoData,
            _appState.HasProfileLoadFailure,
            _appState.ProfilePersistenceBlockedMessage);

        return includeLatestProbe
            ? baseContext with { ProbeSnapshot = _configurationProbes.GetLatest(baseContext) }
            : baseContext;
    }

    private async Task PersistConfigAsync(string successMessage)
    {
        var persisted = await _appState.SaveConfigAsync();
        _configurationProbes.Invalidate();
        LoadFromState();
        SetStatus(persisted ? successMessage : _appState.ProfilePersistenceBlockedMessage ?? successMessage);
    }

    private string ResolveNamespaceLabel(Guid namespaceId, string? fallbackAlias)
    {
        var namespaceConfig = _appState.ServiceBusNamespaces.FirstOrDefault(candidate => candidate.Id == namespaceId);
        if (namespaceConfig is null)
        {
            return string.IsNullOrWhiteSpace(fallbackAlias) ? namespaceId.ToString("N")[..8] : fallbackAlias!;
        }

        return string.IsNullOrWhiteSpace(namespaceConfig.Alias)
            ? namespaceConfig.FullyQualifiedNamespace
            : namespaceConfig.Alias;
    }

    private RedisCacheEntry? BuildRedisProbeCache()
    {
        if (!string.IsNullOrWhiteSpace(RedisConnectionString))
        {
            return new RedisCacheEntry
            {
                DisplayName = string.IsNullOrWhiteSpace(RedisDisplayName) ? "Redis" : RedisDisplayName.Trim(),
                ConnectionString = RedisConnectionString.Trim(),
                Database = Math.Clamp(RedisDatabase, 0, 15),
            };
        }

        var config = _appState.Config.RedisConfig;
        config?.EnsureMigrated();
        return config?.Caches.FirstOrDefault(candidate => string.Equals(candidate.Id, SelectedActiveRedisCacheId, StringComparison.Ordinal))
            ?? config?.ActiveCache;
    }

    private StorageConfig BuildStorageConfig() => new()
    {
        Id = string.IsNullOrWhiteSpace(EditingStorageAccountId) ? Guid.NewGuid().ToString("N")[..8] : EditingStorageAccountId,
        DisplayName = StorageDisplayName.Trim(),
        AccountName = NormalizeOptional(StorageAccountName) ?? string.Empty,
        ConnectionStringRef = NormalizeOptional(StorageConnectionStringRef),
        UseAad = StorageUseAad,
        AllowMutations = StorageAllowMutations,
    };

    private void ResetRedisEditor()
    {
        EditingRedisCacheId = null;
        RedisDisplayName = string.Empty;
        RedisConnectionString = string.Empty;
        RedisDatabase = 0;
    }

    private void ResetStorageEditor()
    {
        EditingStorageAccountId = null;
        StorageDisplayName = string.Empty;
        StorageAccountName = string.Empty;
        StorageConnectionStringRef = string.Empty;
        StorageUseAad = false;
        StorageAllowMutations = false;
    }

    private void SetStatus(string message)
    {
        SectionStatusMessage = message;
    }

    private static string? NormalizeOptional(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatStatus(ConfigurationCheckStatus status) => status switch
    {
        ConfigurationCheckStatus.Ready => "Ready",
        ConfigurationCheckStatus.Configured => "Configured",
        ConfigurationCheckStatus.Warning => "Needs attention",
        ConfigurationCheckStatus.Error => "Error",
        ConfigurationCheckStatus.NotConfigured => "Needs setup",
        ConfigurationCheckStatus.Skipped => "Skipped",
        _ => status.ToString(),
    };

    private static Visibility ToVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
