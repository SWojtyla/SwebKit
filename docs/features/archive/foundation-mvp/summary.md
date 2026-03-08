# Archive Summary - Foundation and MVP

---

title: "Archive Summary - Foundation and MVP"
owner: ""
completed_date: "2026-03-08"
pr: ""
commit: "706cd15"

---

## Goal

Deliver a stable baseline application shell, core domain model, and real service connectivity so later features build on a working foundation.

## Delivered

- Solution scaffold with 5 source projects (`SwebKit.App`, `Core`, `Azure`, `Kubernetes`, `OpenTelemetry`) and 4 test projects, all building cleanly.
- Core domain model: `Project`, `ProjectEnvironment`, `ServiceBusNamespace`, `ServiceBusConfig`, `ObservabilityConfig`, `AksConfig`, enums.
- Core abstractions: `IServiceBusClient`, `IObservabilityProvider`, `IAksClient`, `ICredentialStore`, `IAppEventBus`, `ITaskQueue`.
- Singleton services: `AppStateService` (project/env context), `AppEventBus` (pub/sub), `TaskQueueService`, `ProfileRepository` (JSON persistence), `UiStateRepository`.
- Client implementations: `AzureServiceBusClient`, `AppInsightsObservabilityProvider`, `KubernetesAksClient`, `WindowsCredentialStore`.
- Demo data providers: `DemoObservabilityProvider`, `DemoAksClient` for development without live services.
- DI wiring in `MauiProgram.cs` with Fluent UI, Blazor WebView, credential store, event bus, repositories, tab/command services.
- MAUI Blazor shell: `MainLayout` with `LeftNav`, `TopBar` (project/env switching, production badge, command palette trigger), `StatusBar`.
- All 5 pages: `ServiceBusPage`, `ObservabilityPage`, `AksPage`, `ProjectsPage`, `SettingsPage`.
- Keyboard shortcuts via JSInterop (`Ctrl+P`, `Alt+1-4`, `F5`, `Ctrl+W`).
- Command palette with search, execute, keyboard navigation.
- `_Imports.razor` covering all component subdirectories (no RZ10012 warnings).

## Key decisions

- Service clients (`IServiceBusClient`, `IObservabilityProvider`, `IAksClient`) are created per-connection on pages, not registered as DI singletons — config varies by environment.
- `ProfileRepository` uses JSON file at `%LocalAppData%\SwebKit\profiles.json` for persistence.
- `WindowsCredentialStore` uses Windows `PasswordVault` API with `SwebKit:` key prefix.
- Navigation uses Blazor Router + `NavigationManager`, not MAUI Shell routing.
- Cross-component state via `CascadingValue<AppStateService>` + `IAppEventBus` events.

## Validation performed

- 99 tests pass across 4 test projects:
  - `SwebKit.Core.Tests`: 45 tests (domain models, AppStateService, AppEventBus, ProfileRepository, RemapRules, FilterState)
  - `SwebKit.App.Tests`: 49 tests (components, CommandRegistry, MessageComposer, EntityTree, ScheduledMessages)
  - `SwebKit.Azure.Tests`: 4 tests (client guard validation)
  - `SwebKit.Kubernetes.Tests`: 1 test (client validation)

## Lessons learned

- Keeping service clients as per-connection instances (not DI singletons) simplifies multi-environment scenarios and avoids stale-connection bugs.
- Demo data providers are valuable — enable full UI development without Azure/AKS credentials.

## Follow-up

- `SwebKit.OpenTelemetry` project exists but has no source implementation (`OtlpObservabilityProvider`). To be implemented when OTLP endpoint support is prioritized.
- Metrics in `AppInsightsObservabilityProvider` is minimal (empty result). To be fleshed out in a dedicated observability feature.

## Archive metadata

- Source: `docs/features/active/foundation-mvp/`
- Related features built on this foundation: `service-bus`, `service-bus-enhancements`, observability, AKS
