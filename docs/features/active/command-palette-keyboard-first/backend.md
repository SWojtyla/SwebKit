# Backend Plan — Command Palette & Keyboard-First Navigation

## `CommandRegistry` extensions

### Current state

`CommandRegistry` holds a flat list of `AppCommand` objects. `AppCommand` has: `Id`, `Label`, `Category`, `Shortcut`, `Execute`.

### Required additions

```csharp
public record AppCommand
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Category { get; init; } = "";
    public string? Shortcut { get; init; }
    public Func<Task> Execute { get; init; } = () => Task.CompletedTask;

    // New:
    public Func<bool>? IsAvailable { get; init; }   // null = always available
    public string? AreaScope { get; init; }          // null = global; "aks", "redis" etc = area-scoped
}
```

`CommandRegistry`:
- `GetAvailable(string? currentArea)` — returns commands where `AreaScope` is null or matches `currentArea`, and `IsAvailable?.Invoke() ?? true`
- `CommandRegistry` is a singleton; area-scoped commands registered by feature pages on init, unregistered on dispose

### `ISelectionContext`

New singleton service that tracks the currently selected resource per area. Feature pages push their selection into it; command availability predicates read from it.

```csharp
public interface ISelectionContext
{
    void SetSelection(string area, object? selected);
    T? GetSelection<T>(string area) where T : class;
    event Action? SelectionChanged;
}
```

`SelectedDeployment`, `SelectedPod`, etc. are stored as untyped `object?` and cast by predicates:

```csharp
IsAvailable = () => selectionContext.GetSelection<DeploymentInfo>("aks") is not null
```

### `UiStateRepository` additions

Add `RecentCommandIds` (list of up to 5 command IDs, most recent first) to the persisted JSON. `CommandRegistry` exposes:
- `void RecordUsed(string commandId)` — prepends to recent list, trims to 5, persists
- `IReadOnlyList<string> RecentCommandIds { get; }`

## Affected files

- `src/SwebKit.Core/Services/AppCommand.cs` — add `IsAvailable`, `AreaScope`
- `src/SwebKit.App/Services/CommandRegistry.cs` — `GetAvailable`, `RecordUsed`, `RecentCommandIds`
- `src/SwebKit.Core/Services/ISelectionContext.cs` — new
- `src/SwebKit.App/Services/SelectionContext.cs` — new
- `src/SwebKit.Core/Configuration/UiStateRepository.cs` — add `RecentCommandIds`
- `src/SwebKit.App/MauiProgram.cs` — register `ISelectionContext`
- All feature pages — register commands on init, unregister on dispose, push selection to `ISelectionContext`

## Tasks

- [ ] Add `IsAvailable` and `AreaScope` to `AppCommand`
- [ ] Add `GetAvailable(area)` and `RecordUsed` to `CommandRegistry`
- [ ] Add `RecentCommandIds` persistence to `UiStateRepository`
- [ ] Define and implement `ISelectionContext`
- [ ] Register `ISelectionContext` in `MauiProgram.cs`
- [ ] Register area-specific commands in Service Bus, AKS, Redis, Storage, Releases pages
- [ ] Push selection state to `ISelectionContext` in all grids
- [ ] Unit tests for `GetAvailable` (filtering, availability predicates)
- [ ] Unit tests for `RecordUsed` (order, persistence)
