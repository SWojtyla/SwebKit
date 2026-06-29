# SwebKit Development Guidelines

## 🚨 CRITICAL RULES

### Always Validate Before Committing
- **BUILD FIRST**: Always run `dotnet build` to ensure the project compiles without errors
- **TEST ALWAYS**: Run `dotnet test` to validate no regressions
- **SMOKE TEST**: Manually test the affected functionality in the UI

### Workflow
1. Make changes
2. Run build: `dotnet build`
3. Fix any compilation errors
4. Run tests: `dotnet test`
5. Fix any test failures
6. Manual verification
7. Only then commit

## 📝 Project Specifics

### Technologies
- .NET MAUI Blazor
- C# 10+
- Fluent UI Components
- Kubernetes client
- Azure services

### Key Components
- `AksDetailPanels.razor` - Main AKS detail panel system
- `AksPage.razor` - Main AKS page
- Uses Fluent UI Blazor components

## ⚡ Quality Gates

- [ ] Code compiles without errors
- [ ] All existing tests pass
- [ ] No breaking changes to public APIs
- [ ] UI remains responsive
- [ ] No memory leaks in panel management

## 🔧 Common Issues to Check

- Razor syntax errors (missing braces, @ symbols)
- CSS class conflicts
- Event handler bindings
- State management in Blazor components
- Null reference exceptions