# Service Bus Integration Standardization

## Scope

Standardize and improve the Service Bus integration in SwebKit across:
- `SwebKit.Azure.ServiceBus` infrastructure layer
- `SwebKit.App` UI components (`ServiceBusPage`, `ServiceBusGrid`, `ServiceBusNamespacePanel`)
- Service Bus lifecycle services (`ServiceBusNamespaceBootstrapper`, `ServiceBusWarmupCache`)

## Goals

1. **Reduce complexity** in `AzureServiceBusClient` by splitting responsibilities into focused collaborators
2. **Improve maintainability** by aligning Service Bus components with existing base class patterns (`SwebKitComponentBase`)
3. **Optimize rendering and state management** in `ServiceBusPage` and `ServiceBusGrid`
4. **Standardize error handling, caching, and cancellation** across the Service Bus stack
5. **Improve consistency** with other Azure integrations (Storage, AKS, Redis)

## Motivation

The current Service Bus integration has grown organically:
- `AzureServiceBusClient` is 653 lines and mixes admin, messaging, stats, and dead-letter workflows
- `ServiceBusPage` is 927 lines and does not use the standardized `SwebKitComponentBase`
- `ServiceBusGrid` has hand-rolled sort/filter caching and many uncoordinated `InvokeAsync(StateHasChanged)` calls
- `ServiceBusWarmupCache` holds `IServiceBusClient` instances without ownership or disposal tracking

These patterns cause:
- Difficulty adding new features or fixing bugs
- Inconsistent UI refresh behavior
- Potential resource leaks
- Code duplication across similar Azure client patterns

## Dependencies

- `SwebKitComponentBase` standardized rendering APIs
- `RenderCoalescingOptions` configuration system (introduced in Blazor Performance Standardization)

## Risks

| Risk | Mitigation |
|------|------------|
| Breaking existing Service Bus workflows | Comprehensive test plan and feature flags if needed |
| Client lifetime regressions | Explicit ownership tracking and `IAsyncDisposable` tests |
| UI render regressions | Use existing `RequestRender()` coalescing |
| Message processing regressions | Preserve `DeadLetterSequenceProcessor` logic with better encapsulation |

## Waves

1. **Wave A: Infrastructure Refactor** - Split `AzureServiceBusClient` into focused services
2. **Wave B: UI Base Class Adoption** - Migrate `ServiceBusPage` and `ServiceBusGrid` to `SwebKitComponentBase`
3. **Wave C: State & Cache Optimization** - Standardize caching, cancellation, and rendering
4. **Wave D: Lifecycle & Disposal** - Fix client ownership and disposal tracking
5. **Wave E: Validation & Testing** - Build verification and integration tests
