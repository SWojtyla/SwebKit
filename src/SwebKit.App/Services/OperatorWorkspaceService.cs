using Microsoft.AspNetCore.Components;
using SwebKit.App.Components.Layout;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.App.Services;

public interface IOperatorResourceSearchProvider
{
    IEnumerable<WorkspaceSnapshot> GetSnapshots();
}

public sealed class OperatorWorkspaceService
{
    private const int MaxRecentResources = 8;

    private readonly AppStateService _appState;
    private readonly UiStateRepository _uiState;
    private readonly NavigationManager _navigation;
    private readonly IReadOnlyList<IOperatorResourceSearchProvider> _providers;
    private readonly Dictionary<string, WorkspaceSnapshot> _currentSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<WorkspaceSnapshot, Task>> _restoreHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WorkspaceSnapshot> _pendingRestores = new(StringComparer.OrdinalIgnoreCase);

    public OperatorWorkspaceService(
        AppStateService appState,
        UiStateRepository uiState,
        NavigationManager navigation,
        IEnumerable<IOperatorResourceSearchProvider> providers)
    {
        _appState = appState;
        _uiState = uiState;
        _navigation = navigation;
        _providers = providers.ToList();
    }

    public event Action? Changed;

    public WorkspaceSnapshot? GetCurrentSnapshot(string area) =>
        _currentSnapshots.TryGetValue(area, out var snapshot) ? snapshot.Clone() : null;

    public void ClearCurrentSnapshot(string area)
    {
        if (_currentSnapshots.Remove(area))
        {
            NotifyChanged();
        }
    }

    public IReadOnlyList<FavoriteResource> GetFavoriteResources() =>
        _appState.Config.FavoriteResources
            .OrderByDescending(static favorite => favorite.PinnedAt)
            .Select(static favorite => favorite.Clone())
            .ToList();

    public IReadOnlyList<SavedWorkspace> GetSavedWorkspaces() =>
        _appState.Config.SavedWorkspaces
            .OrderByDescending(static workspace => workspace.SavedAt)
            .Select(static workspace => workspace.Clone())
            .ToList();

    public IReadOnlyList<RecentResourceEntry> GetRecentResources() =>
        _uiState.State.RecentResources
            .OrderByDescending(static recent => recent.AccessedAt)
            .Select(static recent => recent.Clone())
            .ToList();

    public bool IsFavorite(string resourceKey) =>
        _appState.Config.FavoriteResources.Any(favorite =>
            string.Equals(favorite.Snapshot.Resource.Key, resourceKey, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<WorkspaceSnapshot> SearchResources(string query)
    {
        var searchQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return [];
        }

        return GetSearchCandidates()
            .Select(snapshot => (Snapshot: snapshot, Score: SearchScoring.FuzzyScore(searchQuery, BuildSearchText(snapshot))))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Snapshot.Resource.DisplayPath ?? candidate.Snapshot.Resource.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Snapshot.Clone())
            .Take(12)
            .ToList();
    }

    public IReadOnlyList<SavedWorkspace> SearchSavedWorkspaces(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return GetSavedWorkspaces();
        }

        return _appState.Config.SavedWorkspaces
            .Select(workspace => (Workspace: workspace, Score: SearchScoring.FuzzyScore(query, BuildSearchText(workspace))))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Workspace.Name, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Workspace.Clone())
            .ToList();
    }

    public async Task PublishSnapshotAsync(WorkspaceSnapshot snapshot, bool recordRecent)
    {
        var normalized = NormalizeSnapshot(snapshot);
        if (string.IsNullOrWhiteSpace(normalized.Resource.Area) || string.IsNullOrWhiteSpace(normalized.Resource.Key))
        {
            return;
        }

        _currentSnapshots[normalized.Resource.Area] = normalized.Clone();

        if (recordRecent)
        {
            await RecordRecentAsync(normalized);
        }
        else
        {
            NotifyChanged();
        }
    }

    public async Task ToggleFavoriteAsync(WorkspaceSnapshot snapshot)
    {
        var normalized = NormalizeSnapshot(snapshot);
        var existing = _appState.Config.FavoriteResources.FirstOrDefault(favorite =>
            string.Equals(favorite.Snapshot.Resource.Key, normalized.Resource.Key, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            _appState.Config.FavoriteResources.Remove(existing);
            SyncLegacyServiceBusLink(normalized, shouldExist: false);
        }
        else
        {
            _appState.Config.FavoriteResources.Add(new FavoriteResource
            {
                Snapshot = normalized.Clone(),
                PinnedAt = DateTimeOffset.UtcNow,
            });
            SyncLegacyServiceBusLink(normalized, shouldExist: true);
        }

        await _appState.SaveConfigAsync();
        NotifyChanged();
    }

    public async Task SaveCurrentWorkspaceAsync(string area, string name)
    {
        if (!_currentSnapshots.TryGetValue(area, out var snapshot))
        {
            return;
        }

        var workspaceName = name.Trim();
        if (string.IsNullOrWhiteSpace(workspaceName))
        {
            workspaceName = snapshot.Resource.DisplayPath ?? snapshot.Resource.DisplayName;
        }

        var existing = _appState.Config.SavedWorkspaces.FirstOrDefault(workspace =>
            string.Equals(workspace.Name, workspaceName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(workspace.Snapshot.Resource.Area, snapshot.Resource.Area, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            _appState.Config.SavedWorkspaces.Add(new SavedWorkspace
            {
                Name = workspaceName,
                Snapshot = snapshot.Clone(),
                SavedAt = DateTimeOffset.UtcNow,
                SchemaVersion = 1,
            });
        }
        else
        {
            existing.Snapshot = snapshot.Clone();
            existing.SavedAt = DateTimeOffset.UtcNow;
            existing.SchemaVersion = 1;
        }

        await _appState.SaveConfigAsync();
        NotifyChanged();
    }

    public async Task DeleteWorkspaceAsync(Guid workspaceId)
    {
        var removed = _appState.Config.SavedWorkspaces.RemoveAll(workspace => workspace.Id == workspaceId);
        if (removed == 0)
        {
            return;
        }

        await _appState.SaveConfigAsync();
        NotifyChanged();
    }

    public Task OpenFavoriteAsync(string resourceKey)
    {
        var favorite = _appState.Config.FavoriteResources.FirstOrDefault(candidate =>
            string.Equals(candidate.Snapshot.Resource.Key, resourceKey, StringComparison.OrdinalIgnoreCase));
        return favorite is null ? Task.CompletedTask : OpenSnapshotAsync(favorite.Snapshot, recordRecent: true);
    }

    public Task OpenWorkspaceAsync(Guid workspaceId)
    {
        var workspace = _appState.Config.SavedWorkspaces.FirstOrDefault(candidate => candidate.Id == workspaceId);
        return workspace is null ? Task.CompletedTask : OpenSnapshotAsync(workspace.Snapshot, recordRecent: true);
    }

    public async Task OpenSnapshotAsync(WorkspaceSnapshot snapshot, bool recordRecent)
    {
        var normalized = NormalizeSnapshot(snapshot);
        if (string.IsNullOrWhiteSpace(normalized.Resource.Area))
        {
            return;
        }

        _currentSnapshots[normalized.Resource.Area] = normalized.Clone();

        if (recordRecent)
        {
            await RecordRecentAsync(normalized);
        }
        else
        {
            NotifyChanged();
        }

        if (CanRestoreImmediately(normalized.Resource.Area, out var handler))
        {
            await handler(normalized.Clone());
            return;
        }

        _pendingRestores[normalized.Resource.Area] = normalized.Clone();
        _navigation.NavigateTo(ShellNavigation.ForArea(normalized.Resource.Area).Href);
    }

    public void RegisterRestoreHandler(string area, Func<WorkspaceSnapshot, Task> handler)
    {
        _restoreHandlers[area] = handler;
    }

    public void UnregisterRestoreHandler(string area)
    {
        _restoreHandlers.Remove(area);
    }

    public Task ApplyPendingRestoreAsync(string area)
    {
        if (!_pendingRestores.TryGetValue(area, out var snapshot))
        {
            return Task.CompletedTask;
        }

        if (!_restoreHandlers.TryGetValue(area, out var handler))
        {
            return Task.CompletedTask;
        }

        _pendingRestores.Remove(area);
        return handler(snapshot.Clone());
    }

    private IEnumerable<WorkspaceSnapshot> GetSearchCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in _currentSnapshots.Values)
        {
            if (seen.Add(snapshot.Resource.Key))
            {
                yield return snapshot;
            }
        }

        foreach (var favorite in _appState.Config.FavoriteResources)
        {
            if (seen.Add(favorite.Snapshot.Resource.Key))
            {
                yield return favorite.Snapshot;
            }
        }

        foreach (var provider in _providers)
        {
            foreach (var snapshot in provider.GetSnapshots())
            {
                var normalized = NormalizeSnapshot(snapshot);
                if (string.IsNullOrWhiteSpace(normalized.Resource.Key) || !seen.Add(normalized.Resource.Key))
                {
                    continue;
                }

                yield return normalized;
            }
        }
    }

    private async Task RecordRecentAsync(WorkspaceSnapshot snapshot)
    {
        var recentResources = _uiState.State.RecentResources;
        recentResources.RemoveAll(existing =>
            string.Equals(existing.Snapshot.Resource.Key, snapshot.Resource.Key, StringComparison.OrdinalIgnoreCase));
        recentResources.Insert(0, new RecentResourceEntry
        {
            Snapshot = snapshot.Clone(),
            AccessedAt = DateTimeOffset.UtcNow,
        });

        if (recentResources.Count > MaxRecentResources)
        {
            recentResources.RemoveRange(MaxRecentResources, recentResources.Count - MaxRecentResources);
        }

        await _uiState.SaveAsync();
        NotifyChanged();
    }

    private bool CanRestoreImmediately(string area, out Func<WorkspaceSnapshot, Task> handler)
    {
        if (!_restoreHandlers.TryGetValue(area, out var resolvedHandler))
        {
            handler = static _ => Task.CompletedTask;
            return false;
        }

        handler = resolvedHandler;
        var currentArea = ShellNavigation.ResolveUri(_navigation.ToBaseRelativePath(_navigation.Uri)).Area;
        return string.Equals(currentArea, area, StringComparison.OrdinalIgnoreCase);
    }

    private void SyncLegacyServiceBusLink(WorkspaceSnapshot snapshot, bool shouldExist)
    {
        if (!string.Equals(snapshot.Resource.Area, "service-bus", StringComparison.OrdinalIgnoreCase)
            || !snapshot.RestoreState.TryGetValue("namespaceId", out var namespaceText)
            || !Guid.TryParse(namespaceText, out var namespaceId)
            || !snapshot.RestoreState.TryGetValue("entityPath", out var entityPath)
            || string.IsNullOrWhiteSpace(entityPath))
        {
            return;
        }

        var existing = _appState.Config.ServiceBusEntityLinks.FirstOrDefault(link =>
            link.NamespaceId == namespaceId
            && string.Equals(link.EntityPath, entityPath, StringComparison.OrdinalIgnoreCase));

        if (shouldExist)
        {
            if (existing is null)
            {
                _appState.Config.ServiceBusEntityLinks.Add(new SbEntityLink
                {
                    NamespaceId = namespaceId,
                    EntityPath = entityPath,
                    Alias = snapshot.Resource.DisplayName,
                });
            }

            return;
        }

        if (existing is not null)
        {
            _appState.Config.ServiceBusEntityLinks.Remove(existing);
        }
    }

    private static WorkspaceSnapshot NormalizeSnapshot(WorkspaceSnapshot snapshot)
    {
        snapshot.Resource ??= new OperatorResourceReference();
        snapshot.Resource.Key ??= string.Empty;
        snapshot.Resource.Area ??= string.Empty;
        snapshot.Resource.Kind ??= string.Empty;
        snapshot.Resource.DisplayName ??= string.Empty;
        snapshot.Resource.Metadata ??= [];
        snapshot.RestoreState ??= [];
        snapshot.CapturedAt = snapshot.CapturedAt == default ? DateTimeOffset.UtcNow : snapshot.CapturedAt;
        return snapshot;
    }

    private static string BuildSearchText(WorkspaceSnapshot snapshot)
    {
        var parts = new List<string>
        {
            snapshot.Resource.DisplayName,
            snapshot.Resource.DisplayPath ?? string.Empty,
            snapshot.Resource.Summary ?? string.Empty,
            snapshot.Resource.Kind,
            snapshot.Resource.Area,
        };

        parts.AddRange(snapshot.Resource.Metadata.Values);
        parts.AddRange(snapshot.RestoreState.Values);
        return string.Join(' ', parts.Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string BuildSearchText(SavedWorkspace workspace) => string.Join(' ', new[]
    {
        workspace.Name,
        workspace.Snapshot.Resource.DisplayName,
        workspace.Snapshot.Resource.DisplayPath ?? string.Empty,
        workspace.Snapshot.Resource.Summary ?? string.Empty,
        workspace.Snapshot.Resource.Area,
    }.Where(static value => !string.IsNullOrWhiteSpace(value)));

    private void NotifyChanged() => Changed?.Invoke();
}