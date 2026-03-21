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

    /// <summary>Raised when <see cref="UseDemoData"/> changes so layout components can re-render.</summary>
    public event Action? DemoModeChanged;

    public async Task InitializeAsync()
    {
        await _profiles.LoadAsync();
        await _uiState.LoadAsync();
        UseDemoData = _uiState.State.UseDemoData;
    }

    public async Task SetDemoModeAsync(bool enabled)
    {
        UseDemoData = enabled;
        _uiState.State.UseDemoData = enabled;
        await _uiState.SaveAsync();
        DemoModeChanged?.Invoke();
    }

    public Task SaveConfigAsync() => _profiles.SaveAsync();

    public async Task AddServiceBusNamespaceAsync(ServiceBusNamespace ns)
    {
        _profiles.AddServiceBusNamespace(ns);
        await _profiles.SaveAsync();
    }

    public async Task RemoveServiceBusNamespaceAsync(Guid id)
    {
        _profiles.RemoveServiceBusNamespace(id);
        await _profiles.SaveAsync();
    }

    public async Task SaveMessageTemplateAsync(SbMessageTemplate template)
    {
        _profiles.SaveMessageTemplate(template);
        await _profiles.SaveAsync();
    }

    public async Task DeleteMessageTemplateAsync(Guid id)
    {
        _profiles.DeleteMessageTemplate(id);
        await _profiles.SaveAsync();
    }
}
