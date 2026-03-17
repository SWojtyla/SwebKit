---
description: Load the relevant pitfalls file before making non-trivial changes to source or test code
applyTo: 'src/**,tests/**'
---

# Pitfalls — Load Before Making Changes

Before editing or creating any non-trivial code in `src/` or `tests/`, read the pitfall file(s) that match the context below.

| You are working with…                                                     | Read this file                                                         |
| ------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| Blazor components, `.razor` files, MAUI Hybrid, JS interop, CSS isolation | [docs/pitfalls/blazor-maui.md](../../docs/pitfalls/blazor-maui.md)     |
| Azure SDK, Service Bus, auth, connection strings, `AsyncPageable`         | [docs/pitfalls/azure-sdk.md](../../docs/pitfalls/azure-sdk.md)         |
| General C# / .NET: `required`, cancellation, nullability, LINQ            | [docs/pitfalls/dotnet-csharp.md](../../docs/pitfalls/dotnet-csharp.md) |

If unsure, read all three — they are short.

## Rules

- Read the relevant file(s) **before** writing or reviewing code, not after.
- If you hit a bug that isn't covered and it would have cost more than one debugging session, add a new entry to the matching pitfalls file.
- Use the pitfalls index for a quick overview: [docs/pitfalls/index.md](../../docs/pitfalls/index.md)
