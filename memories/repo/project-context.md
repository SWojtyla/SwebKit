# SwebKit Project Context - Session Memory

## 📅 Last Updated: 2026-06-18

---

## 🎯 Current Project State

### Git Information
- **Current Branch**: `vibe/archive-style-features-d23fe2`
- **Main Branch**: `main`
- **Repository**: https://github.com/SWojtyla/SwebKit
- **Recent Commits** (last 5):
  - `901614d` - Merge branch 'vibe/archive-style-features-d23fe2'
  - `2b1d935` - fix
  - `51c331e` - fix
  - `43f483c` - Rework Service Bus authentication to use Azure credentials
  - `82d0943` - Fix build issues in Core project

### Recent Activity
- **Primary Focus**: Service Bus authentication improvements
- **Build Status**: Recent fixes applied to Core project
- **Active Work**: Archive-style features implementation

---

## 👥 Team & Contacts

- **Primary Maintainer**: SebastienWojtyla
- **Repository Owner**: SWojtyla

---

## 🏗️ Architecture Recap

### Core Components
1. **SwebKit.App** - MAUI Blazor Hybrid UI host
2. **SwebKit.Core** - Domain models, interfaces, shared services
3. **SwebKit.Azure** - Azure Service Bus + Storage implementations
4. **SwebKit.Kubernetes** - AKS diagnostics and operations
5. **SwebKit.Redis** - Redis client implementation
6. **SwebKit.DevOps** - Azure DevOps REST integration
7. **SwebKit.Observability** - Application Insights + KQL queries

### Key Patterns
- **Abstraction First**: All external services behind `I*Client` interfaces
- **Demo Mode**: Every feature supports `AppStateService.UseDemoData`
- **Atomic Persistence**: Temp file → atomic replace → `.bak` recovery
- **Route-First Restore**: Workspace-based navigation with semantic snapshots
- **Fan-Out Connectivity**: Independent connections (resilient to partial failures)

---

## 🎨 Technology Stack Summary

| Area | Technology | Version/Notes |
|------|------------|--------------|
| **Runtime** | .NET | 10 SDK |
| **UI Framework** | MAUI Blazor Hybrid | Cross-platform ready |
| **Components** | Fluent UI Blazor | Microsoft official |
| **Azure SDK** | Service Bus, Monitor, Identity, ARM | Latest stable |
| **Kubernetes** | KubernetesClient | C# client |
| **Redis** | StackExchange.Redis | Latest |
| **Tests (UI)** | bUnit | Component testing |
| **Tests (Backend)** | xUnit | Unit testing |
| **Tests (E2E)** | Playwright | End-to-end |
| **Styling** | CSS + Blazor CSS isolation | Layered approach |

---

## 📁 Important File Locations

### Entry Points
- `src/SwebKit.App/MauiProgram.cs` - App startup and DI
- `src/SwebKit.App/App.xaml.cs` - MAUI lifecycle
- `src/SwebKit.App/MainPage.xaml` - MAUI host page
- `src/SwebKit.App/Components/Layout/MainLayout.razor` - Blazor shell
- `src/SwebKit.App/Components/Routes.razor` - Route configuration

### Core Services
- `src/SwebKit.Core/Services/AppStateService.cs` - Global app state
- `src/SwebKit.Core/Services/AppEventBus.cs` - Event bus
- `src/SwebKit.Core/Services/TaskQueueService.cs` - Background tasks
- `src/SwebKit.App/Services/CommandRegistry.cs` - Commands and shortcuts
- `src/SwebKit.App/Services/NotificationService.cs` - Notifications

### Configuration & Persistence
- `src/SwebKit.Core/Domain/AppConfig.cs` - App configuration model
- `src/SwebKit.Core/Configuration/ProfileRepository.cs` - Profile persistence
- `src/SwebKit.Core/Configuration/UiStateRepository.cs` - UI state persistence
- `src/SwebKit.Core/Configuration/UserSettingsRepository.cs` - User settings
- `src/SwebKit.App/Platforms/Windows/WindowsCredentialStore.cs` - Credential storage

### Integrations
- `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs` - Service Bus
- `src/SwebKit.Azure/Storage/AzureStorageClient.cs` - Blob Storage
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` - AKS
- `src/SwebKit.Redis/RedisClient.cs` - Redis
- `src/SwebKit.DevOps/DevOpsClient.cs` - Azure DevOps
- `src/SwebKit.Observability/AzureAppInsightsProvider.cs` - App Insights

---

## 🚀 Common Commands Reference

### Build & Development
```bash
# Full build
dotnet build

# Run the application
dotnet run --project src/SwebKit.App

# Run all tests
dotnet test

# Run specific test project
dotnet test tests/SwebKit.Core.Tests

dotnet test tests/SwebKit.Azure.Tests

# Clean and rebuild
dotnet clean
dotnet build
```

### Git Operations
```bash
# Check status
git status

# See recent commits
git log --oneline -10

# Create new feature branch
git checkout -b feature/<name>

# Stage changes
git add .

# Commit
git commit -m "<message>"

# Push (requires confirmation)
git push

# Pull latest
git pull
```

---

## 💡 Recent Patterns & Learnings

### Service Bus Authentication (Recent Change)
- **Change**: Reworked to use Azure credentials (commit `43f483c`)
- **Pattern**: Using Azure Identity SDK for authentication
- **Files Touched**: `SwebKit.Azure/ServiceBus/`
- **Status**: Build fixes applied in subsequent commits

### Build Issues (Recent Fix)
- **Issue**: Core project build failures (commit `82d0943`)
- **Fix**: Build configuration adjustments
- **Status**: Resolved

---

## 🔍 Debugging Tips

### Common Issues & Solutions

#### Service Bus Connection Failures
1. Check credential configuration in `AppConfig`
2. Verify `WindowsCredentialStore` has the required credentials
3. Check demo mode setting (`AppStateService.UseDemoData`)
4. Review `docs/pitfalls/azure-sdk.md`

#### UI Not Updating
1. Check `AppStateService` initialization
2. Verify event bus subscriptions (`AppEventBus`)
3. Check Blazor component lifecycle (OnParametersSet, OnAfterRenderAsync)
4. Review `docs/pitfalls/blazor-maui.md`

#### Persistence Issues
1. Check `%APPDATA%/SwebKit` directory permissions
2. Verify `.bak` files exist for recovery
3. Check repository implementations use atomic writes
4. Review JSON serialization in `Serialization/` folder

---

## 📊 Project Metrics (as of last update)

- **Total Projects**: 7 source projects + 6 test projects
- **Lines of Code**: ~50,000+ (estimate)
- **Documentation Files**: 50+ in `docs/`
- **Pitfall Entries**: 4 main pitfall files
- **Active Features**: Check `docs/features/active/`
- **Archived Features**: Check `docs/features/archive/`

---

## 🎯 Quick Navigation Guide

### Finding Files
```bash
# Find all Service Bus related files
findstr /s /i "ServiceBus" src/

# Find all page components
dir /s /b src\SwebKit.App\Components\Pages\*.razor

# Find all repository classes
dir /s /b src\SwebKit.Core\Configuration\*Repository.cs

# Find all client interfaces
dir /s /b src\SwebKit.Core\Abstractions\I*Client.cs
```

### Key Directories
```
src/SwebKit.App/Components/Pages/          # All routed pages
src/SwebKit.App/Components/Layout/        # Shell components
src/SwebKit.App/Services/                  # App services
src/SwebKit.Core/Abstractions/             # Integration interfaces
src/SwebKit.Core/Configuration/            # Persistence
src/SwebKit.Core/Services/                 # Shared services
docs/architecture/                          # Architecture docs
docs/pitfalls/                            # Known issues
```

---

## 🔗 Related Resources

- **Official Documentation**: `docs/README.md`
- **Feature Catalog**: `docs/features/README.md`
- **Architecture Overview**: `docs/architecture/architecture.md`
- **Design Document**: `docs/architecture/design.md`
- **Codebase Guide**: `docs/architecture/codebase-guide.md`
- **Agent Instructions**: `.vibe/instructions.md`
- **GitHub Instructions**: `.github/copilot-instructions.md`

---

## 📝 Session Notes

*Add notes during a session that should persist for future reference*

---

**Template**: Copy this section to add new entries

```markdown
### [Date] - [Topic]
**Context**: [What was being worked on]
**Learning**: [What was learned]
**Decision**: [Decision made]
**Files**: [Files involved]
**Tags**: [#architecture #bug #feature]
```

---

**Maintained by**: Mistral Vibe  
**File Location**: `memories/repo/project-context.md`  
**Purpose**: Session-persistent project context for faster onboarding
