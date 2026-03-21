# SwebKit — Design & Implementation Plan

**Context:** SwebKit is a .NET MAUI Blazor Hybrid desktop utility for .NET developers who
regularly operate with Azure Service Bus, Application Insights / OpenTelemetry, AKS, and
related infrastructure. The codebase lives at `d:/Custom Stuff/SwebKit` and contains
production-focused projects, implementations, and automated tests described below.

---

## Table of Contents

1. [Overall Architecture & Domain Model](#1-overall-architecture--domain-model)
2. [Solution Structure & Tech Stack](#2-solution-structure--tech-stack)
3. [Information Architecture & Navigation](#3-information-architecture--navigation)
4. [Layout & Pane System](#4-layout--pane-system)
5. [Service Bus Feature Design](#5-service-bus-feature-design)
6. [Observability Feature Design](#6-observability-feature-design)
7. [AKS Feature Design](#7-aks-feature-design)
8. [Cross-Cutting UX Decisions](#8-cross-cutting-ux-decisions)
9. [Implementation Roadmap](#9-implementation-roadmap)
10. [Risks & Trade-offs](#10-risks--trade-offs)

---

## 1. Overall Architecture & Domain Model

### 1.1 Core Concept

Each feature area (Service Bus, AKS, Redis, Storage, Releases) is independent. There is
no global project or environment selection — each feature reads its own config from the
single global `AppConfig` stored in `profiles.json`.

### 1.2 Domain Objects (summary)

AppConfig (`SwebKit.Core.Domain.AppConfig`, stored as `profiles.json`)

- AksConfig?: AksConfig
- RedisConfig?: RedisConfig
- StorageAccounts: List<StorageConfig>
- DevOpsConfig?: DevOpsConfig
- ServiceBusEntityLinks: List<SbEntityLink>
- FavoriteEntities: List<FavoriteEntity>
- LastUsedFilters: Dictionary<string, FilterState>

The domain model lives in `SwebKit.Core` and is deliberately small: feature-specific
implementations are provided by the `SwebKit.*` projects under `src/`.

### 1.3 Core Services & Runtime

- `AppStateService` (singleton): exposes `AppConfig` and `ServiceBusNamespaces`, delegates
  persistence to `ProfileRepository` and `UiStateRepository`. Initialization
  (`InitializeAsync`) loads profiles and UI state.
- DI registrations live in `SwebKit.App.MauiProgram` and include `AppStateService`,
  `ProfileRepository`, `UiStateRepository`, `ScheduledMessageRepository`, `ICredentialStore`,
  `IAppEventBus`, and UI helpers like `TabService` and `CommandRegistry`.

### 1.4 Secrets & Credential Store

- Secrets are not embedded in profile files. Logical credential references (string keys)
  are stored in a platform credential store. Windows implements `ICredentialStore` via
  `WindowsCredentialStore` using `PasswordVault` (save/get/delete/list). The implementation
  prefixes resources with `SwebKit:` and gracefully returns `null` when a secret is absent.

## 2. Solution Structure & Tech Stack

- Platform: .NET MAUI Blazor Hybrid (Windows primary)
- UI: Razor components inside `BlazorWebView`, Fluent UI Blazor components
- Charts: Blazor-ApexCharts (metrics)
- Editor/Terminal: Monaco (BlazorMonaco) and xterm.js via JSInterop
- Serialization: `System.Text.Json` (source-gen where useful)
- Core projects (root `src/`):
  - `SwebKit.App` — MAUI Blazor app containing all Razor components and platform code
  - `SwebKit.Core` — domain models, abstractions, repositories
  - `SwebKit.Azure` — Azure implementations (Service Bus, Observability helpers)
  - `SwebKit.Kubernetes` — Kubernetes client helpers and AKS features

## 3. Information Architecture & Navigation

- Top bar: Project + Environment selector. Environment changes broadcast via `AppStateService`.
- Left pane: navigation tree of features and namespaces (Service Bus entities, AKS clusters).
- Center: tabbed panes for lists, charts, editors, and logs.
- Right: `DetailsPane` (collapsible) used across features for properties and message bodies.

## 4. Layout & Pane System

- Tabs are managed by `TabService`. Each pane is a Razor component conforming to a
  small lifecycle (Open/Close/Refresh). `DataTable.razor` and `DetailsPane.razor` provide
  consistent selection, loading, and error states across features.

## 5. Service Bus Feature Design

- Core abstraction: `IServiceBusClient` implemented by `AzureServiceBusClient` in
  `SwebKit.Azure`. DI resolves per-environment clients using `ServiceBusConfig` and
  `ICredentialStore`.
- `AzureServiceBusClient` supports both connection-string mode and AAD (DefaultAzureCredential).
  - When a connection string contains a scoped entity path the client will surface only that queue/topic.
  - When using `ServiceBusConfig` + credential ref the implementation attempts to read the connection
    string from `ICredentialStore` and falls back to AAD when appropriate.
- Standard operations: list queues/topics/subscriptions, peek messages, peek DLQ, send, send batch,
  schedule/cancel scheduled messages, resubmit DLQ messages (reads DLQ via peek-lock, forwards,
  and completes), and basic connection test via administration client.

## 6. Observability Feature Design

- Abstraction: `IObservabilityProvider` with implementations using `Azure.Monitor.Query` and
  OTLP where required. Supports Log Queries, Traces, and Metrics.
- UI: Query editor (Monaco) with saved queries per-environment, results table, and trace waterfall
  view using a React/JS-based renderer via JSInterop when needed.

## 7. AKS Feature Design

- `IAksClient` provides cluster/namespace listing, pod and deployment introspection, log streaming,
  port-forwarding, and a remote shell helper.
- Implementation uses `KubernetesClient` and includes short-lived operations performed through
  a background task queue to avoid blocking the UI thread.

## 8. Cross-Cutting UX Decisions

- Keyboard shortcuts and global command palette (`Ctrl+P`).
- Production safety: a future production indicator may toggle UI warnings and confirmation dialogs for destructive operations.
- Shared components: `FilterBar.razor`, `DataTable.razor`, and `DetailsPane.razor` to keep behavior uniform.

## 9. Implementation Roadmap

1. Stabilize core `AppStateService` and profile storage. (done)
2. Harden Service Bus flows: resubmit, DLQ management, batch send.
3. Observability query UX and trace viewer integration.
4. AKS live tooling (logs, port-forward, shell).
5. E2E tests and Playwright smoke tests for major flows.

## 10. Risks & Trade-offs

- Choosing Blazor Hybrid gives web-component flexibility but increases app size and adds JSInterop complexity.
- DefaultAzureCredential simplifies auth in Azure-hosted dev environments but can surprise users on
  machines without the expected credential sources; we fall back to connection strings when present.
- Production safety UX adds friction to fast recovery workflows; opt-in shortcuts for power users may be added later.

---

If you'd like, I can now: (1) restore any specific deleted sections to match the previous version exactly, (2) expand any feature section with code references and file links, or (3) run tests that touch the services to validate behavior. Which should I do next?
