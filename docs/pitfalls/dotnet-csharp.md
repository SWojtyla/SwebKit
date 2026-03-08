# Pitfalls — General .NET / C#

---

## CS-1 — `required` properties are not null-safe at runtime

**Symptom:** `NullReferenceException` on a property marked `required string`.

**Cause:** `required` enforces initialisation in object initialisers at compile time but does not prevent null at runtime (e.g., when deserialized from JSON with a missing field, or when a test sets a value via reflection).

**Fix:** Treat `required` as a compile-time hint only. Validate at deserialization boundaries (`ProfileRepository.LoadAsync`) and provide sensible defaults.

---

## CS-2 — `catch (Exception)` catches `OperationCanceledException`

**Symptom:** Cancellation is swallowed silently; the operation appears to complete instead of being cancelled.

**Cause:** `OperationCanceledException` derives from `Exception`, so a bare `catch (Exception)` will catch it. If you want cancellation to propagate cleanly, you must re-throw it explicitly.

**Fix:**

```csharp
catch (OperationCanceledException) { throw; }
catch (Exception ex) { /* handle non-cancellation errors */ }
```

---

_See also: [blazor-maui.md](blazor-maui.md) · [azure-sdk.md](azure-sdk.md)_
