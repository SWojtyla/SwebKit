# Projects and Environments

## What Is Supported

- Create, edit, select, and delete projects.
- Per-project environments (for example: Dev, Test, Acc, Prod).
- Global project and environment selection from the top bar.
- Persisted last-used project/environment across app restarts.
- Production awareness (`IsProduction`) used by other features to add safety friction.

## Core Runtime Flow

1. `AppStateService.InitializeAsync` loads profiles and UI state.
2. Last selected project/environment are restored when possible.
3. UI components subscribe to project/environment events.
4. Selecting project/environment publishes change events through `IAppEventBus`.

## Main Code Locations

- `src/SwebKit.App/Components/Pages/ProjectsPage.razor`
- `src/SwebKit.App/Components/Layout/TopBar.razor`
- `src/SwebKit.Core/Services/AppStateService.cs`
- `src/SwebKit.Core/Services/AppEventBus.cs`
- `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- `src/SwebKit.Core/Domain/Project.cs`
- `src/SwebKit.Core/Domain/ProjectEnvironment.cs`

## Important Notes

- Project and environment state is central; all feature pages depend on it.
- `UseDemoData` is also stored on `AppStateService` and changes how feature providers connect.
- Production checks are derived from selected environment tier and should not be bypassed in mutating flows.

## Validation Pointers

- `tests/SwebKit.Core.Tests/ProjectModelTests.cs`
- `tests/SwebKit.Core.Tests/AppStateServiceTests.cs`
- `tests/SwebKit.Core.Tests/AppEventBusTests.cs`
