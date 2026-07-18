using SwebKit.App.Services;
using SwebKit.Core.Services;

namespace SwebKit.App.Components.ApiClient;

/// <summary>
/// Keyboard shortcuts and command palette registration concern for <see cref="ApiClientPage"/>.
/// </summary>
/// <remarks>
/// Slice 8 of the decomposition tracked in
/// docs/features/active/api-client-page-decomposition/. Pure file-boundary move: no behavior
/// change. These members still mutate the page-owned <c>_state</c> field and call other
/// partial-class members (<c>AddRequestAsync</c>, <c>SaveRequestAsync</c>,
/// <c>OpenNewCollectionDialog</c>, <c>OnTabCloseRequestedAsync</c>, <c>OnTabSelectedAsync</c>)
/// directly, by design (DEC-PD-1 in this feature's decisions.md). <c>GetNextOpenTabId</c> is
/// included here (not separately listed in the extraction plan) because it is exclusively used
/// by <c>OnApiClientShortcut</c>.
/// </remarks>
public partial class ApiClientPage
{
    private void OnApiClientShortcut(ApiClientShortcutEvent e)
    {
        _ = InvokeAsync(async () =>
        {
            switch (e.Action)
            {
                case "NewRequest":
                    if (_state.ActiveCollection is not null)
                        await AddRequestAsync();
                    break;
                case "NewCollection":
                    OpenNewCollectionDialog();
                    break;
                case "EnvManager":
                    _state.WorksheetMode = _state.WorksheetMode == WorksheetEnvs ? null : WorksheetEnvs;
                    break;
                case "SaveRequest":
                    if (_state.SelectedRequest is not null)
                        await SaveRequestAsync();
                    break;
                // Phase 3 follow-up: request-tab close/cycle shortcuts (Ctrl+Shift+W,
                // Ctrl+PageUp/Down — see keyboardShortcuts.js for why Ctrl+W/Ctrl+Tab aren't
                // reused). No-op when the tabs setting is off (DEC-UX-1: zero behaviour change
                // on the OFF path) or when there is no active tab.
                case "TabClose":
                    if (UserSettings.Settings.ApiClientRequestTabs && _state.ActiveTabRequestId is { } closeId)
                        await OnTabCloseRequestedAsync(closeId);
                    break;
                case "TabNext":
                    if (UserSettings.Settings.ApiClientRequestTabs && GetNextOpenTabId(1) is { } nextId)
                        await OnTabSelectedAsync(nextId);
                    break;
                case "TabPrev":
                    if (UserSettings.Settings.ApiClientRequestTabs && GetNextOpenTabId(-1) is { } prevId)
                        await OnTabSelectedAsync(prevId);
                    break;
            }
            await InvokeAsync(StateHasChanged); // BL-2
        });
    }

    // Mirrors MainLayout.GetNextTabId, scoped to the API Client's own open-request tabs
    // (State.OpenTabs) instead of the app-level page tabs.
    private string? GetNextOpenTabId(int direction)
    {
        var tabs = _state.OpenTabs;
        if (tabs.Count == 0) return null;
        var currentId = _state.ActiveTabRequestId;
        var idx = currentId is null ? -1 : tabs.FindIndex(t => t.RequestId == currentId);
        if (idx < 0) return tabs[0].RequestId;
        var next = (idx + direction + tabs.Count) % tabs.Count;
        return tabs[next].RequestId;
    }

    private void RegisterApiClientCommands()
    {
        Commands.Register(new AppCommand
        {
            Id = "api-new-request",
            Label = "API Client: New Request",
            Category = "API Client",
            Icon = "＋",
            Shortcut = "Ctrl+N",
            AreaScope = "api-client",
            Execute = async () => { if (_state.ActiveCollection is not null) await AddRequestAsync(); },
        });
        Commands.Register(new AppCommand
        {
            Id = "api-new-collection",
            Label = "API Client: New Collection",
            Category = "API Client",
            Icon = "📦",
            Shortcut = "Ctrl+Shift+N",
            AreaScope = "api-client",
            Execute = () => { OpenNewCollectionDialog(); return Task.CompletedTask; },
        });
        Commands.Register(new AppCommand
        {
            Id = "api-env-manager",
            Label = "API Client: Manage Environments",
            Category = "API Client",
            Icon = "🌍",
            Shortcut = "Ctrl+E",
            AreaScope = "api-client",
            Execute = () =>
            {
                _state.WorksheetMode = _state.WorksheetMode == WorksheetEnvs ? null : WorksheetEnvs;
                return InvokeAsync(StateHasChanged);
            },
        });
        Commands.Register(new AppCommand
        {
            Id = "api-quick-nav",
            Label = "API Client: Quick Nav (Request Picker)",
            Category = "API Client",
            Icon = "⌕",
            Shortcut = "Ctrl+P",
            AreaScope = "api-client",
            Execute = () => Task.CompletedTask, // handled by global Ctrl+P → CommandPalette
        });
        Commands.Register(new AppCommand
        {
            Id = "api-save-request",
            Label = "API Client: Save Request",
            Category = "API Client",
            Icon = "💾",
            Shortcut = "Ctrl+S",
            AreaScope = "api-client",
            Execute = async () => { if (_state.SelectedRequest is not null) await SaveRequestAsync(); },
        });
    }
}
