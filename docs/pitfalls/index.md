# Pitfalls Index

Quick-reference files for recurring bugs in this codebase. Add an entry to the relevant file whenever a bug costs more than one debugging session.

| File                                   | Covers                                                                                                |
| -------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| [react-frontend.md](react-frontend.md) | React/Tauri frontend: CodeMirror theming, Tauri serialization boundary, `AllowedRoots`, panel layout, Playwright traps |
| [blazor-maui.md](blazor-maui.md)       | Blazor component lifecycle, rendering, JS interop, MAUI Hybrid threading, CSS isolation, line endings |
| [azure-sdk.md](azure-sdk.md)           | Azure SDK auth, connection strings, `AsyncPageable` resource management                               |
| [dotnet-csharp.md](dotnet-csharp.md)   | General .NET / C# traps (`required`, cancellation, nullability)                                       |
| [agent-workflow.md](agent-workflow.md) | AI agent workflow mistakes (status drift, missing pitfalls, architecture drift)                       |
