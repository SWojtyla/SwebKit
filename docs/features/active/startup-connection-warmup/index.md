# Feature Overview - startup-connection-warmup

---

title: "Feature Overview - startup-connection-warmup"
owner: "GitHub Copilot"
status: "Planned"
jira: "not linked"
created: "2026-04-17"
updated: "2026-04-17"

---

## Goal

Pre-warm connections to AKS, Redis, Service Bus, and Observability silently in the background after startup so that first page open is instant rather than blocked on connection bootstrapping.

## Value

Each integration page (AKS, Redis, Service Bus, Observability) currently performs its connection bootstrap on first `OnParametersSet`. For AKS this can take 2–5 s (Azure token + kubeconfig parse + context/namespace calls). Redis takes 1–3 s (StackExchange `ConnectionMultiplexer` init). Service Bus fans out per namespace (1–2 s each). The user sees a spinner on every first open.

If the bootstrappers fire in the background after profiles and credentials are loaded — which already happens in `InitializeInBackgroundAsync` — the client is ready by the time the user clicks the first nav item. From the user's perspective: the page opens with content already loading.

## Scope

- In scope:
  - New `IConnectionWarmupService` + `ConnectionWarmupService` singleton in `SwebKit.App/Services/`
  - Per-integration warm-client caches as singleton services (`IAksWarmupCache`, `IRedisWarmupCache`, `IServiceBusWarmupCache`, `IObservabilityWarmupCache`)
  - Warmup triggered from `MainLayout.InitializeInBackgroundAsync()` after `AppState.InitializeAsync()` returns
  - Tab-priority ordering: restored `UiState.OpenTabs` areas get warmed first; unconfigured areas skipped entirely
  - AKS: warm default kubeconfig context + default namespace from profile
  - Redis: warm each configured Redis cache entry in the active profile
  - Service Bus: warm each configured namespace in the active profile
  - Observability: warm App Insights ARM discovery for the selected subscription
  - All warmup failures (timeout, network, auth) swallowed silently; errors logged only to debug output
  - Per-integration timeout guard (default 10 s per feature area) via `CancellationToken`
  - Cache invalidation on profile change (`AppState.Initialized` re-fires or profile-reload signal)
  - User opt-out toggle in `UserSettings` ("Pre-warm connections on startup")
  - Pages check the cache before bootstrapping (idempotent: cache hit skips reconnect)
- Out of scope:
  - Any visible progress or status indicator during warmup — the feature is intentionally invisible
  - Azure DevOps warmup (PAT validation is fast enough, not worth the complexity)
  - Retrying failed warmup connections — pages handle reconnect on demand as today
  - Warming multiple profiles simultaneously
  - Cluster-topology discovery beyond the default context/namespace

> Waves
>
> - Wave 1: Cache infrastructure + AKS + Redis warmup (highest perceived latency)
> - Wave 2: Service Bus + Observability warmup, opt-out setting

## Dependencies

- Internal projects and likely touched paths:
  - `src/SwebKit.App/Components/Layout/MainLayout.razor` — trigger warmup after init
  - `src/SwebKit.App/Services/ConnectionWarmupService.cs` — new service (created)
  - `src/SwebKit.App/MauiProgram.cs` — DI registration
  - `src/SwebKit.Core/Abstractions/IAksClient.cs` — cache target type
  - `src/SwebKit.Core/Abstractions/IRedisClient.cs` — cache target type
  - `src/SwebKit.Core/Domain/AppConfig.cs` — read configured resources for warmup candidates
  - `src/SwebKit.Core/Domain/UserSettings.cs` — add opt-out toggle
  - `src/SwebKit.Core/Services/AppStateService.cs` — subscribe to profile-change signal for cache invalidation
  - `src/SwebKit.App/Components/Pages/AksPage.razor` — consume AKS warm-client cache
  - `src/SwebKit.App/Components/Pages/RedisPage.razor` — consume Redis warm-client cache
  - `src/SwebKit.App/Components/Pages/ServiceBusPage.razor` — consume Service Bus warm-client cache
  - Bootstrappers: `IAksClientBootstrapper`, `IServiceBusNamespaceBootstrapper`
  - `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` — no change; warmup reuses existing bootstrapper
  - `src/SwebKit.Redis/RedisClient.cs` — no change; warmup creates client via the same factory path
- External libraries:
  - StackExchange.Redis — `ConnectionMultiplexer.ConnectAsync` (already used)
  - Azure SDK — no new packages; bootstrappers already handle auth
- Pitfall files that apply:
  - `docs/pitfalls/dotnet-csharp.md` (CS-2: swallowing `OperationCanceledException` — must re-throw or catch specifically inside warmup try/catch)
  - `docs/pitfalls/blazor-maui.md` (BL-2: `StateHasChanged` dispatch; BL-3: guard state before `await` in `OnParametersSet`)

## Risks & mitigations

- Risk: Warmup consumes TCP connections and auth tokens at startup even for pages the user never visits — Mitigation: tab-priority ordering means only areas with restored open tabs are warmed eagerly; all others skipped unless the user re-enables unconfigured areas explicitly
- Risk: Warmup uses stale clients if the user changes credentials or profile without restarting — Mitigation: subscribe to `AppState` profile-reload signal and clear/replace the warm-client caches on that event
- Risk: Page bootstrap and warmup race; two clients created for the same resource — Mitigation: cache check in the bootstrapper path is idempotent; second creation is skipped if cache is already populated
- Risk: `OperationCanceledException` escapes the silence wrapper and surfaces as an unhandled error — Mitigation: CS-2 pitfall enforced in code review; warmup try/catch must catch `OperationCanceledException` separately and log only
- Risk: 10 s per-area timeout delays the next warmup area in sequence — Mitigation: warmup areas run concurrently (`Task.WhenAll` fan-out) so a slow area does not block a fast one

## Related documents

- Architecture map: `docs/architecture/architecture.md`
- Component design: `docs/architecture/design.md` (App Bootstrap Flow section)
- Code navigation: `docs/architecture/codebase-guide.md`
- Pitfalls: `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `backend.md`
