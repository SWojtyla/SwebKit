# SwebKit - Mistral Vibe Project Instructions

## 🎯 Project Identity

- **Name**: SwebKit
- **Type**: .NET MAUI Blazor Hybrid desktop application
- **Purpose**: Swiss army knife debugging tool for .NET developers working with Azure
- **Primary Language**: C# (.NET 10)
- **UI Framework**: MAUI + Blazor + Microsoft Fluent UI Blazor
- **Repository**: https://github.com/SWojtyla/SwebKit

## 📖 Before Starting ANY Work

**MANDATORY**: Always follow this order:

1. **Read `docs/architecture/index.md`** - The context router tells you exactly what to read
2. **Check `docs/pitfalls/`** - Learn from past mistakes
3. **Check active features** in `docs/features/active/` - See if work is already in progress
4. **Read the relevant architecture docs** based on index.md routing

**NEVER** start coding without reading the architecture docs first. They are constraints, not background reading.

## 🏗️ Architecture Quick Reference

### Core Structure
```
src/
├── SwebKit.App/          # MAUI Blazor Hybrid host, Razor UI, platform glue
│   ├── Components/
│   │   ├── Layout/       # Shell-level layout and navigation (MainLayout, LeftNav, TopBar)
│   │   ├── Pages/        # Routed top-level pages (Dashboard, ServiceBus, Aks, etc.)
│   │   ├── ServiceBus/   # Service Bus workspace components
│   │   ├── Aks/          # AKS grids, panels, diagnostics views
│   │   ├── IncidentTimeline/ # Incident timeline workbench
│   │   ├── Redis/        # Redis keyspace browsing
│   │   ├── Storage/      # Blob container/list/detail UI
│   │   ├── Pipelines/    # Pipelines tree/detail/activity views
│   │   ├── Releases/     # Release records and approvals
│   │   ├── Observability/# Overview/failures/performance/logs
│   │   ├── ApiClient/    # API Client page and request builder
│   │   └── Shared/       # Shared primitives and base components
│   ├── Services/         # App-layer orchestration (commands, tabs, notifications)
│   ├── Platforms/Windows/# Windows-specific (credential store, notifications)
│   └── wwwroot/js/       # JS interop for keyboard, YAML highlighting, UI helpers
│
├── SwebKit.Core/         # Framework-agnostic contracts, models, repositories
│   ├── Abstractions/     # Integration interfaces (IServiceBusClient, IAksClient, etc.)
│   ├── Domain/           # Persisted configuration models
│   ├── Models/           # Runtime DTOs and feature model types
│   ├── Configuration/    # JSON repository implementations
│   ├── Services/         # AppState, event bus, task queue, demo providers
│   └── Serialization/    # System.Text.Json contexts/options
│
├── SwebKit.Azure/        # Azure SDK-backed implementations
│   ├── ServiceBus/       # Service Bus client
│   └── Storage/          # Blob Storage client
│
├── SwebKit.Kubernetes/   # Kubernetes/AKS implementation
│   └── AksClient/        # AKS client and operations
│
├── SwebKit.Redis/        # Redis implementation
├── SwebKit.DevOps/       # Azure DevOps REST integration
├── SwebKit.Observability/# Application Insights discovery and KQL
└── SwebKit.WinUI/        # Legacy WinUI components (migration in progress)
```

### Entry Points
- **App Startup**: `src/SwebKit.App/MauiProgram.cs` - DI composition root
- **MAUI Lifecycle**: `src/SwebKit.App/App.xaml.cs` - Lifecycle and shutdown hooks
- **Blazor Shell**: `src/SwebKit.App/Components/Layout/MainLayout.razor` - Global layout
- **Route Wiring**: `src/SwebKit.App/Components/Routes.razor` - Page entry URLs

### External Integrations
- Azure Service Bus + Blob Storage
- AKS Kubernetes API
- Redis (StackExchange.Redis)
- Azure DevOps REST API
- Azure Monitor Logs API + Azure Resource Manager (App Insights discovery)
- Git CLI (for linked repositories)

### Persistence
- **Location**: `%APPDATA%/SwebKit/`
- **Files**: `profiles.json`, `ui-state.json`, `user-settings.json`, `releases.json`, `scheduled-messages.json`, `collections.json`, `environments.json`, `api-linked-roots.json`
- **Pattern**: Atomic writes (temp file → atomic replace → `.bak` recovery copy)

## 🎨 Common Patterns & Conventions

### File Naming
| Pattern | Meaning | Location |
|---------|---------|----------|
| `*Page.razor` | Routed top-level page | `src/SwebKit.App/Components/Pages/` |
| `*ConfigForm.razor` | Settings sub-form | `src/SwebKit.App/Components/Pages/` |
| `*Panel.razor` / `*Grid.razor` | Feature workspace component | Feature-specific folders |
| `*Repository.cs` | JSON-backed persistence | `src/SwebKit.Core/Configuration/` |
| `I*Client.cs` | Integration interface | `src/SwebKit.Core/Abstractions/` |
| `Azure*Client.cs` | Azure SDK implementation | `src/SwebKit.Azure/` |
| `Kubernetes*Client.cs` | K8s implementation | `src/SwebKit.Kubernetes/` |
| `Demo*Client.cs` | Synthetic demo implementation | `src/SwebKit.Core/Services/` |

### Implementation Patterns

#### Adding a New Page
1. Create Razor component in `src/SwebKit.App/Components/Pages/`
2. Add navigation entry in `src/SwebKit.App/Components/Layout/LeftNav.razor`
3. Add route in `src/SwebKit.App/Components/Routes.razor` (if not auto-discovered)
4. Add feature-specific styles in component-local `.razor.css`
5. Register any services in `MauiProgram.cs`

#### Adding a New Integration
1. Add interface in `src/SwebKit.Core/Abstractions/I*Client.cs`
2. Add implementation in `src/SwebKit.<Domain>/`
3. Create demo implementation in `src/SwebKit.Core/Services/Demo*Client.cs`
4. Register both in `MauiProgram.cs` (real + demo)
5. Add configuration to `src/SwebKit.Core/Domain/AppConfig.cs`
6. Update `AppStateService.UseDemoData` checks

#### Adding Persistence
1. Create model in `src/SwebKit.Core/Domain/`
2. Create repository in `src/SwebKit.Core/Configuration/*Repository.cs`
3. Use `AppDataPaths` for file locations
4. Implement atomic write pattern with `.bak` recovery
5. Register in DI (usually in `MauiProgram.cs`)

## ⚡ Cross-Cutting Concerns

| Concern | Where It Lives | Key Files |
|---------|---------------|------------|
| **Dependency Injection** | `src/SwebKit.App/MauiProgram.cs` | Composition root |
| **Shared App State** | `src/SwebKit.Core/Services/AppStateService.cs` | Global state |
| **Event Bus** | `src/SwebKit.Core/Services/AppEventBus.cs` | Cross-component events |
| **Profile Persistence** | `src/SwebKit.Core/Configuration/ProfileRepository.cs` | `profiles.json` |
| **UI State Persistence** | `src/SwebKit.Core/Configuration/UiStateRepository.cs` | `ui-state.json` |
| **User Settings** | `src/SwebKit.Core/Configuration/UserSettingsRepository.cs` | `user-settings.json` |
| **Credential Storage** | `src/SwebKit.App/Platforms/Windows/WindowsCredentialStore.cs` | Windows Credential Store |
| **Background Tasks** | `src/SwebKit.Core/Services/TaskQueueService.cs` | Background queueing |
| **Port-Forward Sessions** | `src/SwebKit.Core/Services/PortForwardSessionService.cs` | AKS port-forwarding |
| **Commands & Shortcuts** | `src/SwebKit.App/Services/CommandRegistry.cs` + `wwwroot/js/keyboardShortcuts.js` | Global commands |
| **Notifications** | `src/SwebKit.App/Services/NotificationService.cs` | Toast notifications |
| **Demo Mode** | `src/SwebKit.Core/Services/Demo*` | Synthetic data providers |

## 🚦 Feature Execution Workflow

### For Jira-Driven Features (Autonomous)
Use the **`swebify`** skill:
```
"Use swebify for JIRA-123"
```
This handles: ticket fetch → planning → implementation → testing → PR → review → archive

### For Manual Features
1. **Plan**: Use **`swebiplan`** skill to create feature folder with docs
2. **Implement**: Follow the plan, update `status.md` as you go
3. **Review**: Use **`pre-ship-review`** skill for quality gate
4. **Commit**: Use **`azure-devops`** skill to commit and push
5. **Fix PR**: Use **`swebifix`** skill to address review feedback
6. **Archive**: Use **`feature-archive`** skill to close out

### Feature Folder Structure
```
docs/features/active/<feature-name>/
├── index.md          # Feature description, quick links, Jira ticket
├── status.md         # Current state, focus, completed, remaining, blockers
├── plan.md           # Implementation plan (if complex)
├── decisions.md       # Technical decisions and tradeoffs
├── test-plan.md       # Test cases and validation approach
└── traceability/      # Requirements traceability (if needed)
```

## 📝 Documentation Discipline

### Docs-First Rule
**ALL** non-trivial work must be documented:
- Architecture changes → Update `docs/architecture/*.md`
- New patterns → Update `docs/architecture/codebase-guide.md`
- New flows → Update `docs/architecture/design.md`
- Feature work → Use feature folder structure
- Bugs/learnings → Add to `docs/pitfalls/*.md`

### Architecture Docs Priority
1. `docs/architecture/index.md` - **READ FIRST** (context router)
2. `docs/architecture/architecture.md` - System-wide component map
3. `docs/architecture/design.md` - Component-level flows
4. `docs/architecture/codebase-guide.md` - Implementation navigation
5. `docs/architecture/functionalities/*.md` - Feature deep dives

### Pitfalls Discipline
Before making non-trivial changes, **ALWAYS** check:
- `docs/pitfalls/agent-workflow.md` - Agent-specific pitfalls
- `docs/pitfalls/blazor-maui.md` - UI/Blazor/MAUI issues
- `docs/pitfalls/azure-sdk.md` - Azure integration issues
- `docs/pitfalls/dotnet-csharp.md` - .NET/C# patterns

After resolving a non-trivial bug, **ADD** a new pitfall entry.

## 🔍 Common Tasks & Where to Start

| Task | Start Here | Key Files |
|------|------------|------------|
| **App startup / DI changes** | `src/SwebKit.App/MauiProgram.cs` | Composition root |
| **New routed page** | `src/SwebKit.App/Components/Pages/` + `LeftNav.razor` | Page + navigation |
| **Dashboard changes** | `src/SwebKit.App/Components/Pages/DashboardPage.razor` | Dashboard tiles |
| **Service Bus operations** | `src/SwebKit.Core/Abstractions/IServiceBusClient.cs` + `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs` | Contract + implementation |
| **AKS diagnostics** | `src/SwebKit.Core/Abstractions/IAksClient.cs` + `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` | Contract + implementation |
| **Redis operations** | `src/SwebKit.Core/Abstractions/IRedisClient.cs` + `src/SwebKit.Redis/RedisClient.cs` | Contract + implementation |
| **Storage operations** | `src/SwebKit.Core/Abstractions/IStorageClient.cs` + `src/SwebKit.Azure/Storage/AzureStorageClient.cs` | Contract + implementation |
| **DevOps integration** | `src/SwebKit.Core/Abstractions/IDevOpsClient.cs` + `src/SwebKit.DevOps/DevOpsClient.cs` | Contract + implementation |
| **Observability queries** | `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs` + `src/SwebKit.Observability/AzureAppInsightsProvider.cs` | Contract + implementation |
| **Incident timeline** | `src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor` + `src/SwebKit.Core/Abstractions/IIncidentTimelineService.cs` | UI + backend |
| **Resource search** | `src/SwebKit.App/Services/OperatorWorkspaceService.cs` + `src/SwebKit.App/Services/OperatorResourceSearchProviders.cs` | Search providers |
| **Settings/config** | `src/SwebKit.Core/Domain/AppConfig.cs` + `src/SwebKit.Core/Configuration/ProfileRepository.cs` | Config + persistence |
| **API Client** | `src/SwebKit.App/Components/ApiClient/ApiClientPage.razor` + `src/SwebKit.Core/Domain/ApiClientModels.cs` | UI + domain |

## 🛡️ Critical Constraints (NEVER Violate)

### ✅ MUST DO
1. **ALWAYS use abstraction interfaces** from `SwebKit.Core/Abstractions/` - Never call implementations directly
2. **ALWAYS check demo mode** via `AppStateService.UseDemoData` - All features must support demo mode
3. **ALWAYS use atomic persistence** - Temp file → atomic replace → `.bak` recovery copy
4. **ALWAYS update `status.md`** when feature work progresses - Keep it current
5. **ALWAYS add demo implementations** - Every real client needs a `Demo*Client` counterpart
6. **ALWAYS use AppEventBus** for cross-component communication - Don't create direct dependencies

### ❌ NEVER DO
1. **NEVER persist secrets** in JSON config files - Use WindowsCredentialStore only
2. **NEVER hardcode connection strings** - Use AppConfig with credential references
3. **NEVER break existing abstractions** - Extend interfaces, don't bypass them
4. **NEVER create duplicate services** - Check architecture docs first
5. **NEVER leave archived features in active folder** - Move to archive or delete
6. **NEVER ignore pitfalls** - Read relevant pitfall files before touching subsystems
7. **NEVER start without reading architecture docs** - They exist to prevent mistakes

## 🧪 Testing Strategy

| Test Type | Framework | Location | When to Use |
|-----------|-----------|----------|--------------|
| **Component Tests** | bUnit | `tests/SwebKit.App.Tests/` | UI behavior and rendering |
| **Unit Tests** | xUnit | `tests/SwebKit.Core.Tests/` | Domain logic and services |
| **Integration Tests** | xUnit | `tests/SwebKit.Azure.Tests/`, etc. | Integration implementations |
| **E2E Tests** | Playwright | `tests/SwebKit.E2E.Tests/` | End-to-end user flows |

### Test Expectations
- **Before push**: Run `dotnet test` for touched areas
- **For new features**: Add tests in the same PR
- **For bug fixes**: Add regression test
- **For refactoring**: Verify existing tests still pass

## 🎯 Quick Commands Reference

### Build & Run
```bash
# Build entire solution
dotnet build

# Run the app
dotnet run --project src/SwebKit.App

# Run tests
dotnet test

# Run specific test project
dotnet test tests/SwebKit.Core.Tests
```

### Git Workflow
```bash
# Check status
git status

# Create feature branch
git checkout -b feature/<feature-name>

# Commit
git add .
git commit -m "message"

# Push (Vibe will ask for confirmation)
git push
```

## 💡 Vibe-Specific Tips

### For Faster Work
- **Start sessions with**: "Read `.vibe/instructions.md` and `docs/architecture/index.md`"
- **Before coding**: "Check `docs/pitfalls/` for this type of change"
- **For features**: "Use `swebify` skill for JIRA-123" or "Use `swebiplan` to plan this"
- **For review**: "Use `pre-ship-review` skill before push"
- **For bugs**: "Check `docs/pitfalls/` and add new entry if resolved"

### Memory & Context
- Vibe remembers **within a session** but not across sessions
- This file (`.vibe/instructions.md`) is loaded **automatically** at session start
- Use `todo` tool to track multi-step tasks within a session
- Use subagent delegation (`task` tool) for complex exploratory work

## 📚 Key Documentation Files

| File | Purpose | When to Read |
|------|---------|--------------|
| `docs/architecture/index.md` | Context router - tells you what to read | **ALWAYS first** |
| `docs/architecture/architecture.md` | System-wide component map | Before any architecture changes |
| `docs/architecture/design.md` | Component flows and sequences | Before touching core flows |
| `docs/architecture/codebase-guide.md` | Implementation navigation | Before touching code |
| `docs/pitfalls/agent-workflow.md` | Agent-specific pitfalls | Before delegating work |
| `docs/pitfalls/blazor-maui.md` | UI/Blazor issues | Before UI changes |
| `docs/pitfalls/azure-sdk.md` | Azure integration issues | Before Azure changes |
| `docs/pitfalls/dotnet-csharp.md` | .NET patterns | Before backend changes |

## 🔗 External References

- **Jira**: Check for linked tickets in feature folders
- **Azure Docs**: https://learn.microsoft.com/en-us/azure/
- **MAUI Docs**: https://learn.microsoft.com/en-us/dotnet/maui/
- **Blazor Docs**: https://learn.microsoft.com/en-us/aspnet/core/blazor/
- **Fluent UI Blazor**: https://github.com/microsoft/fluentui-blazor

---

**Last Updated**: 2026-06-18
**Maintainer**: Mistral Vibe + SwebKit Team
**Version**: 1.0
