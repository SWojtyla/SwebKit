# SwebKit - AI Agent Instructions

## 🎯 Project Identity

**SwebKit** is a **.NET MAUI Blazor Hybrid** desktop application that serves as a "Swiss army knife" debugging tool for **.NET developers working with Azure**. It combines multiple Azure-focused development workflows into a single, cohesive desktop experience.

---

## 📋 Agent Workflow Rules

### 🔴 CRITICAL: Docs-First Mandate

**ALL** agents working on this repository MUST follow the **docs-first workflow**:

1. **BEFORE any non-trivial work**, read:
   - `docs/architecture/index.md` (context router - tells you exactly what to read)
   - The architecture docs it references
   - Relevant files in `docs/pitfalls/`

2. **IF** the task belongs to an active feature, treat `docs/features/active/<feature-name>/` as the **source of truth**
   - Keep `status.md` current
   - Follow the documented plan
   - Record decisions in `decisions.md`

3. **DO NOT** write plans, feature docs, or decisions **outside** the repository
   - Everything belongs under `docs/`

### 🎯 Delegation Rules

**DO NOT** delegate a multi-wave feature or a multi-page shell refactor as one oversized subagent task.

**For Blazor/MAUI work** that spans multiple areas, split into slices:
- Shell context and navigation
- Shared page-header and state primitives
- Per-page adoption
- Tests and docs alignment

**For backend work** that spans multiple layers, split into:
- Contracts/interfaces
- Services/implementations
- Integrations
- Tests and docs

**IF** a specialist agent judges a delegated task too broad:
- Complete **ONE coherent slice**, OR
- Return `BLOCKED` with a recommended decomposition
- **Silent failure is NOT acceptable**

### 📊 Feature Execution Rules

- ✅ **Prefer** updating existing active feature docs over creating ad hoc markdown files
- ✅ **Keep** implementation aligned with the feature plan
- ✅ **Update** `status.md` when implementation meaningfully progresses
- ✅ **IF** implementation changes behavior for documented functionality, update the corresponding file under `docs/architecture/functionalities/` in the same change set

---

## 🏗️ Architecture Constraints

### 📐 System Design

SwebKit follows a **modular monolith** architecture with clear separation of concerns:

```
┌─────────────────────────────────────────────────────────────────┐
│                      SwebKit.App (UI Layer)                        │
│  - MAUI Blazor Hybrid host                                         │
│  - Page routing and UI composition                                 │
│  - App-level orchestration                                         │
└────────────────────────────────────────────┬────────────────────┘
                                         │
                                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                      SwebKit.Core (Domain Layer)                   │
│  - Domain models                                                    │
│  - Integration abstractions (I*Client interfaces)                 │
│  - Repositories and persistence                                     │
│  - Shared app services (AppState, EventBus, etc.)                  │
└────────────────────────────┬────────────────────────────────────┘
                                 │
    ┌────────────────────┬───────────────┬────────────────┐
    ▼                    ▼               ▼                ▼
┌──────────┐      ┌──────────────┐   ┌──────────┐   ┌──────────────┐
│  Azure    │      │ Kubernetes   │   │  Redis    │   │   DevOps     │
│  (SB+Blob)│      │  (AKS)       │   │           │   │  (REST API)   │
└──────────┘      └──────────────┘   └──────────┘   └──────────────┘
                                    │
                                    ▼
                         ┌─────────────────────┐
                         │  External Services   │
                         │  - Azure Service Bus │
                         │  - AKS API           │
                         │  - Redis             │
                         │  - Azure DevOps      │
                         │  - Azure Monitor     │
                         └─────────────────────┘
```

### 🔑 Key Principles

1. **Separation of Concerns**: UI (App) vs Domain (Core) vs Integrations (feature projects)
2. **Abstraction First**: All external integrations behind `I*Client` interfaces in `SwebKit.Core/Abstractions/`
3. **Demo Mode Support**: Every feature must work with `AppStateService.UseDemoData = true`
4. **Atomic Persistence**: All JSON file writes use temp → atomic replace → `.bak` recovery pattern
5. **Two-Phase Startup**: Immediate shell render, background state hydration
6. **Route-First Restore**: Pages publish semantic snapshots, workspace handles restore
7. **Fan-Out Connectivity**: Independent connections (one failure doesn't block others)

### 📁 Project Structure

```
SwebKit/
├── src/
│   ├── SwebKit.App/          # MAUI Blazor Hybrid host, Razor UI
│   │   ├── Components/
│   │   │   ├── Layout/       # MainLayout, LeftNav, TopBar
│   │   │   ├── Pages/        # Dashboard, ServiceBus, Aks, Redis, etc.
│   │   │   ├── <Feature>/    # Feature-specific components (ServiceBus, Aks, etc.)
│   │   │   └── Shared/       # Shared UI primitives
│   │   ├── Services/         # App-level services (commands, notifications, etc.)
│   │   ├── Platforms/Windows/# Windows-specific implementations
│   │   └── wwwroot/js/       # JS interop scripts
│   │
│   ├── SwebKit.Core/         # Domain, contracts, shared services
│   │   ├── Abstractions/     # I*Client interfaces
│   │   ├── Domain/           # Persisted models
│   │   ├── Models/           # Runtime DTOs
│   │   ├── Configuration/    # JSON repositories
│   │   ├── Services/         # AppState, EventBus, TaskQueue, Demo*
│   │   └── Serialization/    # JSON contexts
│   │
│   ├── SwebKit.Azure/        # Azure Service Bus + Storage
│   ├── SwebKit.Kubernetes/   # AKS operations
│   ├── SwebKit.Redis/        # Redis client
│   ├── SwebKit.DevOps/       # Azure DevOps REST
│   └── SwebKit.Observability/# App Insights + KQL
│
├── tests/
│   ├── SwebKit.App.Tests/    # bUnit component tests
│   ├── SwebKit.Core.Tests/   # xUnit unit tests
│   ├── SwebKit.Azure.Tests/  # Azure integration tests
│   ├── SwebKit.Kubernetes.Tests/
│   ├── SwebKit.DevOps.Tests/  
│   └── SwebKit.E2E.Tests/    # Playwright E2E
│
├── docs/
│   ├── architecture/         # System architecture docs
│   │   ├── architecture.md   # Component map (READ FIRST)
│   │   ├── design.md         # Component flows
│   │   ├── codebase-guide.md # Implementation navigation
│   │   ├── index.md          # Context router
│   │   ├── functionalities/   # Feature deep dives
│   │   └── decisions/        # ADRs
│   │
│   ├── features/            # Feature tracking
│   │   ├── active/<feature>/ # Current work
│   │   └── archive/<feature>/# Completed work
│   │
│   └── pitfalls/            # Known issues and solutions
│       ├── agent-workflow.md
│       ├── blazor-maui.md
│       ├── azure-sdk.md
│       └── dotnet-csharp.md
│
└── .github/
    └── copilot-instructions.md # Global workflow rules
```

---

## 🎨 Naming Conventions

| Pattern | Meaning | Example |
|---------|---------|---------|
| `SwebKit.App` | UI host project | `src/SwebKit.App/` |
| `SwebKit.Core` | Shared abstractions and domain | `src/SwebKit.Core/` |
| `SwebKit.<Integration>` | Concrete integration project | `SwebKit.Azure`, `SwebKit.Kubernetes` |
| `I*Client` | Integration interface | `IServiceBusClient`, `IAksClient` |
| `Azure*Client` | Azure SDK implementation | `AzureServiceBusClient` |
| `Demo*Client` | Synthetic demo implementation | `DemoServiceBusClient` |
| `*Page.razor` | Routed top-level page | `ServiceBusPage.razor` |
| `*Repository.cs` | JSON persistence | `ProfileRepository.cs` |
| `*Service.cs` | App service | `AppStateService.cs` |

---

## ⚡ Cross-Cutting Concerns

| Concern | Location | Key Files |
|---------|----------|------------|
| **Dependency Injection** | `src/SwebKit.App/MauiProgram.cs` | Composition root |
| **Shared App State** | `src/SwebKit.Core/Services/AppStateService.cs` | Global state management |
| **Event Bus** | `src/SwebKit.Core/Services/AppEventBus.cs` | Cross-component events |
| **Profile Persistence** | `src/SwebKit.Core/Configuration/ProfileRepository.cs` | `profiles.json` |
| **UI State Persistence** | `src/SwebKit.Core/Configuration/UiStateRepository.cs` | `ui-state.json` |
| **User Settings** | `src/SwebKit.Core/Configuration/UserSettingsRepository.cs` | `user-settings.json` |
| **Credential Storage** | `src/SwebKit.App/Platforms/Windows/WindowsCredentialStore.cs` | Windows Credential Store |
| **Background Tasks** | `src/SwebKit.Core/Services/TaskQueueService.cs` | Async task queue |
| **Port-Forwarding** | `src/SwebKit.Core/Services/PortForwardSessionService.cs` | AKS port-forward |
| **Commands/Shortcuts** | `src/SwebKit.App/Services/CommandRegistry.cs` + `wwwroot/js/keyboardShortcuts.js` | Global commands |
| **Notifications** | `src/SwebKit.App/Services/NotificationService.cs` | Toast notifications |
| **Demo Mode** | `src/SwebKit.Core/Services/Demo*` | Synthetic providers |

---

## 🚦 Feature Lifecycle

### 📁 Feature Folder Structure

```
docs/features/active/<feature-name>/
├── index.md              # Feature description, quick links, Jira ticket
├── status.md             # Current state (Proposed/Planned/In Progress/Review/Done)
├── plan.md               # Implementation plan (if complex)
├── decisions.md          # Technical decisions and tradeoffs
├── test-plan.md          # Test cases and validation
└── traceability/         # Requirements traceability (optional)
```

### 🎯 Status Values

Use **exactly one** of: `Proposed`, `Planned`, `In Progress`, `Review`, `Done`, `Archived`

### 📝 Status.md Template

```markdown
# Status: In Progress

## Current Focus
- Implementing Service Bus namespace connection

## Completed
- [x] Interface design (IServiceBusClient)
- [x] Demo implementation (DemoServiceBusClient)
- [ ] Azure SDK implementation
- [ ] UI integration

## Remaining
- Azure SDK implementation
- UI integration in ServiceBusPage
- Unit tests

## Blockers
- None

## Validation
- [ ] Build passes
- [ ] Unit tests pass
- [ ] Demo mode works
- [ ] Real mode works (if applicable)
```

### 🗂️ Archive Rules

**When a feature is complete:**

- **Jira ticket linked**: Delete the active feature folder (Jira is the durable record)
  - Add a concise closing comment to the ticket (5-8 lines max, outcomes only)
- **No Jira ticket**: 
  - Prepare a concise archive-ready summary
  - Preserve reusable decisions and lessons
  - Move the folder to `docs/features/archive/`

**DO NOT** keep large execution checklists in the active area.

---

## 🛡️ Guardrails & Constraints

### ✅ MUST DO (Non-Negotiable)

1. **Read architecture docs first** - `docs/architecture/index.md` routes you to what's relevant
2. **Use abstraction interfaces** - Never call implementations directly; always go through `SwebKit.Core/Abstractions/`
3. **Support demo mode** - All features must work when `AppStateService.UseDemoData = true`
4. **Use atomic persistence** - Temp file → atomic replace → `.bak` recovery copy pattern
5. **Update status.md** - Keep it current as work progresses
6. **Add demo implementations** - Every real client needs a `Demo*Client` counterpart
7. **Use AppEventBus** - For cross-component communication, not direct dependencies
8. **Check pitfalls** - Before touching Blazor/MAUI, Azure SDK, or .NET code, re-read relevant pitfall files

### ❌ NEVER DO

1. **Persist secrets in config files** - Use `WindowsCredentialStore` only
2. **Hardcode connection strings** - Use `AppConfig` with credential references
3. **Break existing abstractions** - Extend interfaces, don't bypass them
4. **Create duplicate services** - Check architecture docs first
5. **Leave archived features in active folder** - Move to archive or delete
6. **Ignore pitfalls** - Read relevant pitfall files before touching subsystems
7. **Start without reading architecture docs** - They exist to prevent context-blind code generation
8. **Treat archived docs as active requirements** - They are historical, not current

### ⚠️ Common Pitfalls

| Pitfall | File | When to Check |
|---------|------|---------------|
| Agent workflow issues | `docs/pitfalls/agent-workflow.md` | Before delegating work |
| Blazor/MAUI issues | `docs/pitfalls/blazor-maui.md` | Before UI changes |
| Azure SDK issues | `docs/pitfalls/azure-sdk.md` | Before Azure integration changes |
| .NET/C# patterns | `docs/pitfalls/dotnet-csharp.md` | Before backend changes |

**AFTER resolving any bug that cost more than one debugging cycle, ADD a new pitfall entry.**

---

## 🔧 Common Tasks & Starting Points

### Adding a New Page
1. Create Razor component in `src/SwebKit.App/Components/Pages/`
2. Add navigation entry in `src/SwebKit.App/Components/Layout/LeftNav.razor`
3. Add route in `src/SwebKit.App/Components/Routes.razor` (if not auto-discovered)
4. Add feature-specific styles in component-local `.razor.css`

### Adding a New Integration
1. Add interface in `src/SwebKit.Core/Abstractions/I*Client.cs`
2. Add implementation in `src/SwebKit.<Domain>/`
3. Create demo implementation in `src/SwebKit.Core/Services/Demo*Client.cs`
4. Register both (real + demo) in `MauiProgram.cs`
5. Add configuration to `src/SwebKit.Core/Domain/AppConfig.cs`

### Adding Persistence
1. Create model in `src/SwebKit.Core/Domain/`
2. Create repository in `src/SwebKit.Core/Configuration/*Repository.cs`
3. Use `AppDataPaths` for file locations
4. Implement atomic write pattern with `.bak` recovery
5. Register in DI (usually in `MauiProgram.cs`)

### Modifying Existing Features
1. Read the feature's docs in `docs/features/active/<feature>/`
2. Check `status.md` for current state
3. Update `status.md` before starting work
4. Keep implementation aligned with the documented plan
5. Update docs if behavior changes

---

## 🎯 Quality Gates

### Before Considering Work Complete

- [ ] Implementation matches the feature plan
- [ ] Relevant tests pass (run `dotnet test` for touched areas)
- [ ] Tests or test coverage expectations are met
- [ ] Related docs are updated
- [ ] Assumptions, gaps, or follow-up items are clearly noted
- [ ] Demo mode works (if applicable)
- [ ] Real mode works (if applicable)
- [ ] No secrets in config files
- [ ] Atomic persistence pattern is used
- [ ] `status.md` is updated

### Pre-Ship Review

Use the **`pre-ship-review`** skill to run a structured quality gate:
- **DoD conditions** - Definition of Done check
- **Architecture compliance** - Alignment with documented patterns
- **Security patterns** - Credential handling, secret management
- **Docs alignment** - Code matches documentation
- **Commit hygiene** - Clean, atomic commits

---

## 🚀 Vibe-Specific Commands

| Command | Purpose |
|---------|---------|
| `"Read .vibe/instructions.md and docs/architecture/index.md"` | Full project context preload |
| `"Use project-context skill"` | Load project constraints and architecture |
| `"Use swebify for JIRA-123"` | End-to-end Jira-driven feature delivery |
| `"Use swebiplan for <feature>"` | Create feature plan and scaffold docs |
| `"Use pre-ship-review"` | Quality gate before push |
| `"Use swebifix"` | Fix PR comments and resolve review feedback |
| `"Use feature-archive"` | Archive completed feature |
| `"Check docs/pitfalls/ for <topic>"` | Review known issues |
| `"What's the current branch?"` | Git status check |

---

## 📚 Essential Reading List

### 🎯 Read First (In This Order)
1. `.vibe/instructions.md` (this file) - Project-specific AI instructions
2. `.github/copilot-instructions.md` - Global workflow rules
3. `docs/architecture/index.md` - Context router (tells you what to read next)

### 🏗️ Architecture
1. `docs/architecture/architecture.md` - System-wide component map
2. `docs/architecture/design.md` - Component-level flows and sequences
3. `docs/architecture/codebase-guide.md` - Implementation navigation guide

### 🚨 Pitfalls
1. `docs/pitfalls/agent-workflow.md` - Agent-specific issues
2. `docs/pitfalls/blazor-maui.md` - UI/Blazor/MAUI issues
3. `docs/pitfalls/azure-sdk.md` - Azure integration issues
4. `docs/pitfalls/dotnet-csharp.md` - .NET/C# patterns

### 📁 Active Features
- Check `docs/features/active/` for current work in progress

---

## 💬 Communication Expectations

When responding, agents should:
- ✅ Be explicit about what was changed
- ✅ Mention which feature docs were updated
- ✅ Mention blockers or uncertainties
- ✅ NOT claim completion if validation is incomplete
- ✅ Suggest the next smallest useful step when work cannot be fully completed
- ✅ Mention which skills were used or could be useful
- ✅ Reference relevant sections from these instructions when explaining decisions

---

## 🔗 External References

- **Repository**: https://github.com/SWojtyla/SwebKit
- **Jira**: Check for linked tickets in feature folders
- **Azure Docs**: https://learn.microsoft.com/en-us/azure/
- **MAUI Docs**: https://learn.microsoft.com/en-us/dotnet/maui/
- **Blazor Docs**: https://learn.microsoft.com/en-us/aspnet/core/blazor/
- **Fluent UI Blazor**: https://github.com/microsoft/fluentui-blazor

---

**Maintained by**: SwebKit Team + Mistral Vibe  
**Last Updated**: 2026-06-18  
**Version**: 1.0
