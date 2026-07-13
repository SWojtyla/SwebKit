using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Services;

public class AppStateService
{
    private readonly ProfileRepository _profiles;
    private readonly UiStateRepository _uiState;
    private readonly IAppEventBus _events;
    private readonly TaskCompletionSource _initializedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AppStateService(ProfileRepository profiles, UiStateRepository uiState, IAppEventBus events)
    {
        _profiles = profiles;
        _uiState = uiState;
        _events = events;
    }

    public AppConfig Config => _profiles.Config;

    public IReadOnlyList<ServiceBusNamespace> ServiceBusNamespaces => _profiles.ServiceBusNamespaces;
    public IReadOnlyList<SbMessageTemplate> MessageTemplates => _profiles.MessageTemplates;

    public bool UseDemoData { get; private set; }

    public bool IsInitialized { get; private set; }
    public ProfileLoadResult ProfileLoadResult { get; private set; } = ProfileLoadResult.NotStarted;
    public bool HasProfileLoadFailure => ProfileLoadResult.IsFailure;
    public bool HasProfileLoadRecovery => ProfileLoadResult.IsRecovery;
    public bool IsProfilePersistenceBlocked => _profiles.IsPersistenceBlocked;
    public string? ProfilePersistenceBlockedMessage =>
        IsProfilePersistenceBlocked ? _profiles.CreatePersistenceBlockedException().Message : null;

    /// <summary>Raised when full initialization completes so UI components can re-render.</summary>
    public event Action? Initialized;

    /// <summary>Raised when <see cref="UseDemoData"/> changes so layout components can re-render.</summary>
    public event Action? DemoModeChanged;

    /// <summary>
    /// Raised after any profile save so subscribers can react to configuration changes
    /// (e.g., the monitoring connection pool can evict stale connections).
    /// </summary>
    public event Action? ConfigChanged;

    /// <summary>Returns a task that completes when <see cref="InitializeAsync"/> has finished.</summary>
    public Task WhenInitializedAsync() => _initializedTcs.Task;

    /// <summary>Sets up essential defaults with no disk I/O. Placeholder for future two-phase init.</summary>
    public Task InitializeEssentialsAsync() => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        if (IsInitialized)
            return;

        ProfileLoadResult = await _profiles.LoadAsync().ConfigureAwait(false);
        await _uiState.LoadAsync().ConfigureAwait(false);
        UseDemoData = _uiState.State.UseDemoData;

        IsInitialized = true;
        _initializedTcs.TrySetResult();
        Initialized?.Invoke();
    }

    public async Task SetDemoModeAsync(bool enabled)
    {
        UseDemoData = enabled;
        _uiState.State.UseDemoData = enabled;
        await _uiState.SaveAsync().ConfigureAwait(false);
        DemoModeChanged?.Invoke();
    }

    public Task<bool> SaveConfigAsync() => TryPersistProfilesAsync();

    public async Task AddServiceBusNamespaceAsync(ServiceBusNamespace ns)
    {
        _profiles.AddServiceBusNamespace(ns);
        await TryPersistProfilesAsync().ConfigureAwait(false);
    }

    public async Task RemoveServiceBusNamespaceAsync(Guid id)
    {
        _profiles.RemoveServiceBusNamespace(id);
        await TryPersistProfilesAsync().ConfigureAwait(false);
    }

    public async Task SaveMessageTemplateAsync(SbMessageTemplate template)
    {
        _profiles.SaveMessageTemplate(template);
        await TryPersistProfilesAsync().ConfigureAwait(false);
    }

    public async Task DeleteMessageTemplateAsync(Guid id)
    {
        _profiles.DeleteMessageTemplate(id);
        await TryPersistProfilesAsync().ConfigureAwait(false);
    }

    public void RefreshFromImportedState()
    {
        UseDemoData = _uiState.State.UseDemoData;
        ConfigChanged?.Invoke();
        DemoModeChanged?.Invoke();
    }

    private async Task<bool> TryPersistProfilesAsync()
    {
        var saved = await _profiles.TrySaveAsync().ConfigureAwait(false);
        if (saved)
            ConfigChanged?.Invoke();
        return saved;
    }
}
