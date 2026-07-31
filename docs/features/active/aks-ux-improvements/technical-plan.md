# AKS React UX improvements — technical plan

## Data flow

```
YamlViewer (edit mode)
  ├─ calls validateAksResourceYaml(ns, yaml)  →  POST /api/aks/{ns}/yaml/validate
  │     returns { error?: string } or empty (valid)
  ├─ calls applyAksResourceYaml(ns, kind, name, yaml)
  │     → POST /api/aks/{ns}/yaml/{kind}/{name}
  │     → sidecar: IAksClient.ApplyResourceYamlAsync(ns, kind, name, yaml)
  └─ onSuccess invalidates ["aks-yaml", ns, kind, name] and ["aks-"] queries

NamespaceSelector
  ├─ keeps pending selection state
  ├─ sorts filtered list: selected items first, then unselected, both alphabetically
  └─ Apply calls onChange(pending)

AksWorkspaceContext
  ├─ exposes contextSwitching = setContextMutation.isPending
  ├─ exposes isAksFetching = any active resource query with a key starting with "aks-"
  └─ updates AksPage header with spinner when context/namespace/resources are loading
```

## Sidecar changes

### `src-sidecar/Endpoints/AksEndpoints.cs`

Add two minimal-API endpoints next to the existing YAML GET endpoint:

```csharp
record YamlApplyRequest(string Yaml);
record YamlValidateRequest(string Yaml);

// POST /api/aks/{ns}/yaml/{kind}/{name}
app.MapPost("/api/aks/{ns}/yaml/{kind}/{name}", async (
    string ns, string kind, string name,
    YamlApplyRequest req,
    ProfileRepository profile, DemoModeService demo,
    CancellationToken ct) =>
{
    var client = GetClient(profile, demo);
    await client.ApplyResourceYamlAsync(ns, kind, name, req.Yaml, ct);
    return Results.Ok();
});

// POST /api/aks/{ns}/yaml/validate
app.MapPost("/api/aks/{ns}/yaml/validate", async (
    string ns,
    YamlValidateRequest req,
    ProfileRepository profile, DemoModeService demo,
    CancellationToken ct) =>
{
    var client = GetClient(profile, demo);
    var error = await client.ValidateResourceYamlAsync(ns, req.Yaml, ct);
    return error is null
        ? Results.Ok(new { valid = true })
        : Results.BadRequest(new { error });
});
```

`DemoAksClient.ApplyResourceYamlAsync` already stores the override; validation falls back to the interface default (`null`), so the demo path works unchanged.

## Frontend changes

### `web/src/lib/api.ts`

```ts
export async function applyAksResourceYaml(ns: string, kind: string, name: string, yaml: string): Promise<void> {
  return apiSend(`/api/aks/${encodeURIComponent(ns)}/yaml/${encodeURIComponent(kind)}/${encodeURIComponent(name)}`, "POST", { yaml });
}

export async function validateAksResourceYaml(ns: string, yaml: string): Promise<{ error?: string }> {
  return apiSend(`/api/aks/${encodeURIComponent(ns)}/yaml/validate`, "POST", { yaml });
}
```

### `web/src/lib/hooks.ts`

```ts
export function useAksApplyYaml() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { ns: string; kind: string; name: string; yaml: string }) =>
      apiSend<void>(
        `/api/aks/${encodeURIComponent(vars.ns)}/yaml/${encodeURIComponent(vars.kind)}/${encodeURIComponent(vars.name)}`,
        "POST",
        { yaml: vars.yaml },
      ),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["aks-yaml", vars.ns, vars.kind, vars.name] });
      qc.invalidateQueries({ queryKey: ["aks-"] });
    },
  });
}

export function useAksValidateYaml() {
  return useMutation({
    mutationFn: (vars: { ns: string; yaml: string }) =>
      apiSend<{ error?: string }>(`/api/aks/${encodeURIComponent(vars.ns)}/yaml/validate`, "POST", { yaml: vars.yaml }),
  });
}
```

### `web/src/components/aks/YamlViewer.tsx`

- Add `useState<string | null>("validationError")` and `useState<boolean>("isApplying")`.
- In edit mode, replace the disabled `Apply (coming soon)` button with:
  - **Validate** — calls `validateAksResourceYaml`; displays returned error inline.
  - **Apply** — enabled when validation passes; confirms with `window.confirm` and calls `applyAksResourceYaml`.
- On apply success: `notify("success", "YAML applied")`, set `editMode(false)`, invalidate `["aks-yaml", ...]`.
- Show a loading spinner on the Apply button while `isPending`.

### `web/src/components/aks/NamespaceSelector.tsx`

- Sort the rendered list so pending-selected namespaces appear at the top:
  ```ts
  const sortedFiltered = useMemo(() => {
    const selectedSet = new Set(pending);
    return [...filtered].sort((a, b) => {
      const as = selectedSet.has(a);
      const bs = selectedSet.has(b);
      if (as && !bs) return -1;
      if (!as && bs) return 1;
      return a.localeCompare(b);
    });
  }, [filtered, pending]);
  ```
- Apply a subtle `bg-accent/40` or check icon to selected rows so the two groups are visually distinct.
- Keep the existing `select all / none / search` behavior.

### `web/src/components/aks/ContextSelector.tsx`

- Sort the list so the current context is pinned to the top (same selected-first pattern).
- The `isLoading` prop is already declared and disables the trigger; `AksPage` will pass it.

### `web/src/components/aks/shared/AksWorkspaceContext.tsx`

- Add to context value:
  ```ts
  contextLoading: boolean;
  isAksFetching: boolean;
  ```
- Derive:
  ```ts
  const contextLoading = setContextMutation.isPending;
  const isAksFetching = useIsFetching({
    predicate: (query) => {
      const key = query.queryKey[0];
      if (typeof key !== "string") return false;
      if (key === "aks-namespaces" || key === "aks-contexts" || key === "aks-test") return false;
      return key.startsWith("aks-");
    },
  }) > 0;
  ```

### `web/src/components/aks/AksPage.tsx`

- Import `Loader2`.
- Pass `isLoading={ws.contextLoading}` to `<ContextSelector>`.
- Show a loading indicator in the header when context is switching or AKS resource queries are fetching:
  ```tsx
  {(ws.contextLoading || ws.isAksFetching) && (
    <div className="flex items-center gap-1.5 text-xs text-primary" data-testid="aks-loading-indicator">
      <Loader2 className="h-3.5 w-3.5 animate-spin" />
      {ws.contextLoading ? "Switching context…" : "Loading resources…"}
    </div>
  )}
  ```
- Remove or keep the existing `“Loading...”` text next to the namespace selector; the spinner replaces it.

## Files to modify

- `src-sidecar/Endpoints/AksEndpoints.cs`
- `web/src/lib/api.ts`
- `web/src/lib/hooks.ts`
- `web/src/components/aks/YamlViewer.tsx`
- `web/src/components/aks/NamespaceSelector.tsx`
- `web/src/components/aks/ContextSelector.tsx`
- `web/src/components/aks/shared/AksWorkspaceContext.tsx`
- `web/src/components/aks/AksPage.tsx`

## Verification commands

```bash
cd /home/ubuntu/repos/SwebKit/web && npm run build
cd /home/ubuntu/repos/SwebKit/src-sidecar && ~/.dotnet/dotnet build SwebKit.Sidecar.csproj
cd /home/ubuntu/repos/SwebKit && ~/.dotnet/dotnet test tests/SwebKit.Sidecar.Tests
cd /home/ubuntu/repos/SwebKit/web && PATH="$HOME/.dotnet:$PATH" npx playwright test e2e/aks*.spec.ts
```
