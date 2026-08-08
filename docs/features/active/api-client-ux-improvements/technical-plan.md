# API Client UX Improvements — Technical Plan

## Overall approach

All changes are scoped to the React/Tauri API client. Where a backend piece is needed (collection import endpoint, JSONPath evaluation helper) it is a thin wrapper around existing Core services. The plan is ordered by control/data flow: shared components → variable editors → request editor → import flow → validation.

---

## 1. Shared variable editing component

A single variable table must back both environment variables and collection variables so the UI is identical.

### New component

```tsx
// web/src/components/api-client/VariableList.tsx
export interface VariableListItem {
  id: string;           // stable client key for React list
  key: string;
  isEnabled: boolean;
  mode: VariableMode;   // "plain" | "generated" | "credential" | "keyvault"
  value?: string | null;
  credentialKey?: string | null;
  keyVaultName?: string | null;
  generator?: VariableGeneratorDefinition | null;
}

type VariableMode = "plain" | "generated" | "credential" | "keyvault";

interface VariableListProps {
  variables: VariableListItem[];
  keyVaults: KeyVaultEntry[];
  onChange: (variables: VariableListItem[]) => void;
  /**
   * If true, the row supports the "keyvault" source and shows the vault picker.
   * Collection variables only support value/generated, so this is false there.
   */
  supportsKeyVault?: boolean;
  /**
   * If true, the row supports the "credential" source (Windows Credential Store).
   * Environments support it; collection variables do not.
   */
  supportsCredentialStore?: boolean;
  emptyMessage?: string;
  testIdPrefix: string;
}

export function VariableList({ ... }: VariableListProps): JSX.Element;
```

### New component

```tsx
// web/src/components/api-client/GeneratorConfig.tsx
interface GeneratorConfigProps {
  generator: VariableGeneratorDefinition;
  onChange: (generator: VariableGeneratorDefinition) => void;
  testIdPrefix: string;
}

export function GeneratorConfig({ generator, onChange, testIdPrefix }: GeneratorConfigProps): JSX.Element;
```

`GeneratorConfig` renders:

- A `<select>` for `kind` (`Guid`, `DateTime`, `Integer`, `Decimal`, `Boolean`, `List`, `Faker`, `Template`).
- Per-kind inputs exactly as `CollectionVariableEditor` currently does.
- `data-testid` on every control.

### Conversions

Two small helpers keep `EnvironmentManager` and `CollectionVariableEditor` unchanged at their public interfaces:

```ts
// web/src/lib/variable-utils.ts
export function environmentVariableToListItem(v: EnvironmentVariable): VariableListItem;
export function listItemToEnvironmentVariable(v: VariableListItem): EnvironmentVariable;
export function collectionVariableToListItem(v: CollectionVariable): VariableListItem;
export function listItemToCollectionVariable(v: VariableListItem): CollectionVariable;
```

`VariableSubstitutionService.ResolveEnvironmentVariableSync` already handles `SecretSource.Generated` + `Generator` (see `src/SwebKit.Core/Services/VariableSubstitutionService.cs:116-119`), so no backend change is needed for generated environment variables.

---

## 2. Environment Manager resizable + no overflow

### File: `web/src/components/api-client/EnvironmentManager.tsx`

#### 2.1 Dialog size persistence

Replace the fixed `w-[800px] h-[600px]` shell with a resizable dialog whose size is loaded/saved via `web/src/lib/stores/panel-preferences.ts` `loadViewPreference` / `saveViewPreference`.

```tsx
const DEFAULT_SIZE = { width: 800, height: 600 };

export function EnvironmentManager({ ... }: EnvironmentManagerProps) {
  const [size, setSize] = useState(loadViewPreference("env-manager-size", DEFAULT_SIZE));

  // Persist through ResizeObserver on the dialog element.
  const dialogRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!dialogRef.current) return;
    const obs = new ResizeObserver((entries) => {
      const { width, height } = entries[0].contentRect;
      saveViewPreference("env-manager-size", { width: Math.round(width), height: Math.round(height) });
    });
    obs.observe(dialogRef.current);
    return () => obs.disconnect();
  }, []);
  ...
}
```

The dialog keeps a bottom-right drag handle (`<div className="... cursor-se-resize">`) for discoverability; `resize: both` is not used so the custom handle owns persistence. Minimum width/height: `640x420`.

#### 2.2 Inner split with `ResizablePanels`

Inside the dialog, replace the fixed `w-56` left pane with `ResizablePanels`:

```tsx
<ResizablePanels
  initialWidths={[220, "1fr"]}
  minWidths={[180, 320]}
  storageKey="env-manager-panels"
  panelLabels={["environments", "editor"]}
  className="flex-1 overflow-hidden"
>
  <EnvironmentListPanel ... />
  <EnvironmentEditor ... />
</ResizablePanels>
```

#### 2.3 Row layout fix for Key Vault

`EnvironmentEditor` variable rows move from a single `flex items-center` line to a stacked block:

```tsx
<div className="rounded border p-2" data-testid={`env-var-row-${i}`}>
  <div className="flex items-center gap-2">
    <input type="checkbox" ... />
    <input placeholder="Key" className="w-32 ..." />
    <select className="w-32 ..." /> {/* source */}
    <button className="ml-auto"><Trash2 ... /></button>
  </div>
  <div className="mt-2 flex flex-wrap items-center gap-2">
    <VariableValueField ... />
  </div>
</div>
```

`VariableValueField` (replaces `ValueField`) delegates to `VariableList`/`GeneratorConfig` and renders Key Vault controls on a second line, removing the horizontal overflow.

---

## 3. Collection Variable Editor reuse

### File: `web/src/components/api-client/CollectionVariableEditor.tsx`

Replace its inline variable row with the new `VariableList`:

```tsx
export function CollectionVariableEditor({ collection, onSave, onClose }: CollectionVariableEditorProps) {
  const [variables, setVariables] = useState<VariableListItem[]>(() =>
    (collection.variables ?? []).map(collectionVariableToListItem)
  );

  const handleSave = () => {
    onSave(variables.filter((v) => v.key.trim()).map(listItemToCollectionVariable));
    onClose();
  };

  return (
    <Dialog ...>
      <VariableList
        variables={variables}
        onChange={setVariables}
        keyVaults={[]}
        supportsKeyVault={false}
        supportsCredentialStore={false}
        emptyMessage="No collection variables. These are available to all requests in this collection."
        testIdPrefix="col-var"
      />
    </Dialog>
  );
}
```

The dialog width can stay `w-[500px]`, but the shared `VariableList` row will wrap rather than truncate, so a small width increase to `w-[560px]` is acceptable.

---

## 4. Collection import flow

### 4.1 Sidecar endpoint

#### Registration: `src-sidecar/Program.cs`

Add the existing Core import services that the sidecar does not currently register:

```csharp
builder.Services.AddSingleton<SwebKit.Core.Services.SwebKitCollectionImporter>();
builder.Services.AddSingleton<SwebKit.Core.Services.PostmanCollectionImporter>();
builder.Services.AddSingleton<SwebKit.Core.Services.BrunoFolderImporter>();
builder.Services.AddSingleton<SwebKit.Core.Services.SwebKitEnvironmentImporter>();
builder.Services.AddSingleton<SwebKit.Core.Services.CollectionImportService>();
```

#### New endpoint: `src-sidecar/Endpoints/ConfigEndpoints.cs`

```csharp
public sealed record ImportCollectionRequest(
    string? FileBytesBase64,   // base64 payload of .sweb.json or .postman_collection.json
    string? BrunoFolderPath);  // absolute folder path selected by the Tauri folder picker

public static void MapConfigEndpoints(this WebApplication app)
{
    ...
    app.MapPost("/api/config/collections/import", ImportCollectionsAsync);
}

internal static async Task<IResult> ImportCollectionsAsync(
    CollectionImportService importService,
    ImportCollectionRequest req,
    CancellationToken ct)
{
    if (!string.IsNullOrWhiteSpace(req.BrunoFolderPath))
    {
        var result = await importService.ImportBrunoFolderAsync(req.BrunoFolderPath, ct);
        return Results.Ok(result);
    }

    if (!string.IsNullOrWhiteSpace(req.FileBytesBase64))
    {
        var bytes = Convert.FromBase64String(req.FileBytesBase64);
        var result = await importService.ImportCollectionAsync(bytes, ct);
        return Results.Ok(result);
    }

    return Results.BadRequest(new { error = "Either fileBytesBase64 or brunoFolderPath is required." });
}
```

The `CollectionImportService` already persists imported collections/environments and resolves name collisions (see `src/SwebKit.Core/Services/CollectionImportService.cs:27-57`).

### 4.2 Frontend API wrappers

#### File: `web/src/lib/api.ts`

```ts
export interface CollectionImportResult {
  collections: ApiCollection[];
  environments: ApiEnvironment[];
  requestCount: number;
  captureRuleCount: number;
  authConfigsRequiringReEntry: number;
  variablesExtractedAsEnvironment: number;
  warnings: string[];
}

export async function importCollection(
  fileBytesBase64?: string,
  brunoFolderPath?: string,
): Promise<CollectionImportResult> {
  return apiSend<CollectionImportResult>("/api/config/collections/import", "POST", {
    fileBytesBase64,
    brunoFolderPath,
  });
}
```

#### File: `web/src/lib/hooks/useApiClient.ts`

```ts
export function useImportCollection() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: ({ fileBytesBase64, brunoFolderPath }: { fileBytesBase64?: string; brunoFolderPath?: string }) =>
      importCollection(fileBytesBase64, brunoFolderPath),
    onSuccess: (result) => {
      qc.invalidateQueries({ queryKey: ["collections"] });
      qc.invalidateQueries({ queryKey: ["environments"] });
      notify("success", "Import complete", `${result.collections.length} collection(s) imported.`);
    },
    onError: (err) => {
      notify("error", "Import failed", err instanceof Error ? err.message : String(err));
    },
  });
}
```

### 4.3 Import dialog

#### New file: `web/src/components/api-client/CollectionImportDialog.tsx`

```tsx
interface CollectionImportDialogProps {
  onClose: () => void;
}

export function CollectionImportDialog({ onClose }: CollectionImportDialogProps): JSX.Element;
```

Behavior:

- Tab or section switch for **File import** (Postman/SwebKit) vs **Bruno folder import**.
- File import uses a hidden `<input type="file" accept=".json,.sweb.json,.postman_collection.json" />` with a `Choose file…` button; `FileReader` reads the file as binary and base64-encodes it. In Tauri, `pickFile` returns only a path, so the fallback above is replaced by reading the file through Tauri's `readFile` and base64-encoding.
- Bruno folder import uses `pickDirectory("Select Bruno collection folder")` from `tauri-bridge.ts`. In a plain browser it is unavailable; show a helper explaining that Bruno import requires the desktop app.
- Display selected file/folder name, import progress, and the returned `warnings` list.
- `Import` button disabled until a source is chosen.

### 4.4 Discovery

#### File: `web/src/components/api-client/CollectionTree.tsx`

Add an `Import` button in the header next to **Add collection**:

```tsx
<button
  data-testid="import-collection-button"
  title="Import collection (Postman, Bruno, SwebKit)"
  onClick={onImportCollection}
>
  <Upload className="h-4 w-4" />
</button>
```

Propagate `onImportCollection: () => void` through `CollectionTreeProps` to `ApiClientPageContent`.

#### File: `web/src/components/api-client/ApiClientPage.tsx`

```tsx
{ctx.showImportDialog && (
  <CollectionImportDialog onClose={() => ctx.setShowImportDialog(false)} />
)}
```

#### File: `web/src/components/api-client/ApiClientPageContext.tsx`

Add state:

```ts
showImportDialog: boolean;
setShowImportDialog: (v: boolean) => void;
handleImportCollection: (fileBytesBase64?: string, brunoFolderPath?: string) => Promise<void>;
```

`handleImportCollection` delegates to the new mutation and closes the dialog.

---

## 5. Pre/post request actions

### 5.1 Domain model

#### File: `web/src/lib/types.ts`

```ts
export type RequestActionKind = "CopyToClipboard" | "Delay" | "LogMessage";

export interface RequestAction {
  id: string;
  kind: RequestActionKind;
  /** What to copy for CopyToClipboard, or the message for LogMessage. */
  value?: string | null;
  /** For CopyToClipboard: a dynamic source. */
  source?: "requestUrl" | "requestBody" | "responseBody" | "responseHeader" | "requestHeader" | "responseStatus" | "customValue" | null;
  /** Header name when source is responseHeader/requestHeader; ignored otherwise. */
  selector?: string | null;
  /** For Delay. */
  delayMs?: number | null;
}
```

Add to `HttpRequestEntry`:

```ts
export interface HttpRequestEntry {
  ...
  preRequestActions: RequestAction[];
  postRequestActions: RequestAction[];
}
```

#### File: `src/SwebKit.Core/Domain/ApiClientModels.cs`

```csharp
public sealed class RequestAction
{
    public string Id { get; set; } = string.Empty;
    public RequestActionKind Kind { get; set; }
    public string? Value { get; set; }
    public RequestActionSource? Source { get; set; }
    public string? Selector { get; set; }
    public int? DelayMs { get; set; }
}

public enum RequestActionKind { CopyToClipboard, Delay, LogMessage }

public enum RequestActionSource
{
    RequestUrl, RequestBody, ResponseBody, ResponseHeader, RequestHeader,
    ResponseStatus, CustomValue,
}
```

Add to `HttpRequestEntry`:

```csharp
public List<RequestAction> PreRequestActions { get; set; } = [];
public List<RequestAction> PostRequestActions { get; set; } = [];
```

These are optional during deserialization; existing persisted requests keep empty lists.

### 5.2 Action runner

#### New file: `web/src/lib/request-action-runner.ts`

```ts
export interface ActionRuntimeContext {
  request: HttpRequestEntry;
  response?: ApiClientExecutionResponse | null;
}

export async function runRequestActions(
  actions: RequestAction[],
  ctx: ActionRuntimeContext,
  onNotify: (type: "success" | "info", title: string, message: string) => void,
): Promise<void>;
```

Implemented behavior:

- `CopyToClipboard` evaluates `source` + `selector` from `ctx`, then calls `writeClipboard(text)` from `tauri-bridge.ts`. On success it calls `onNotify("success", "Copied", ...)`.
- `Delay` awaits `delayMs`.

### 5.3 Hook into request execution

#### File: `web/src/components/api-client/ApiClientPageContext.tsx`

Inside `handleSend`:

```ts
const handleSend = async () => {
  const saved = await handleSave();
  if (!saved) return;

  await runRequestActions(request.preRequestActions ?? [], { request }, notify);

  setTabStates((prev) => ({ ...prev, [activeTabId]: { ...prev[activeTabId], sending: true, response: null } }));

  try {
    const result = await executeRequest.mutateAsync({ request, collectionId, environmentId });
    setTabStates((prev) => ({ ...prev, [activeTabId]: { ...prev[activeTabId], response: result, sending: false, history: ... } }));

    await runRequestActions(request.postRequestActions ?? [], { request, response: result }, notify);
  } catch (err) {
    ...
  }
};
```

### 5.4 Editor UI

#### File: `web/src/components/api-client/RequestEditor.tsx`

Add an **Actions** tab between existing tabs:

```tsx
{activeTab === "actions" && (
  <RequestActionsPanel
    preActions={request.preRequestActions ?? []}
    postActions={request.postRequestActions ?? []}
    onChange={(pre, post) => onChange({ ...request, preRequestActions: pre, postRequestActions: post })}
  />
)}
```

#### New file: `web/src/components/api-client/RequestActionsPanel.tsx`

```tsx
interface RequestActionsPanelProps {
  preActions: RequestAction[];
  postActions: RequestAction[];
  onChange: (pre: RequestAction[], post: RequestAction[]) => void;
}

export function RequestActionsPanel({ preActions, postActions, onChange }: RequestActionsPanelProps): JSX.Element;
```

Each action row renders:

- A `<select>` for `kind`.
- For `CopyToClipboard`: a source `<select>` and an optional `selector` input (header name). A `value` input appears only when `source === "customValue"`.
- For `Delay`: a numeric `delayMs` input.
- For `LogMessage`: a `value` textarea/input.
- A remove button and reorder via drag is not required for the first pass.

---

## 6. JSONPath selector for capture rules

### 6.1 Sidecar evaluation helper

#### File: `src-sidecar/Endpoints/ApiClientEndpoints.cs`

```csharp
public sealed record EvaluateJsonPathRequest(string Sample, string JsonPath);
public sealed record EvaluateJsonPathResponse(string? Value, string? Error);

internal static IResult EvaluateJsonPath(EvaluateJsonPathRequest req)
{
    if (string.IsNullOrWhiteSpace(req.Sample) || string.IsNullOrWhiteSpace(req.JsonPath))
        return Results.BadRequest(new { error = "Sample and JSONPath are required." });

    if (!JsonNode.TryParse(req.Sample, out var node))
        return Results.Ok(new EvaluateJsonPathResponse(null, "Invalid JSON sample."));

    if (!Json.Path.JsonPath.TryParse(req.JsonPath, out var path))
        return Results.Ok(new EvaluateJsonPathResponse(null, "Invalid JSONPath."));

    var result = path.Evaluate(node);
    var match = result.Matches?.FirstOrDefault();
    if (match?.Value is null)
        return Results.Ok(new EvaluateJsonPathResponse(null, null)); // no match, not an error

    var value = match.Value switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v => v.ToJsonString(),
        _ => match.Value.ToJsonString(),
    };

    return Results.Ok(new EvaluateJsonPathResponse(value, null));
}
```

Wire it:

```csharp
app.MapPost("/api/api-client/evaluate-jsonpath", EvaluateJsonPath);
```

### 6.2 Frontend wrappers

#### File: `web/src/lib/api.ts`

```ts
export interface EvaluateJsonPathResult {
  value: string | null;
  error: string | null;
}

export async function evaluateJsonPath(sample: string, jsonPath: string): Promise<EvaluateJsonPathResult> {
  return apiSend<EvaluateJsonPathResult>("/api/api-client/evaluate-jsonpath", "POST", { sample, jsonPath });
}
```

### 6.3 Picker component

#### New file: `web/src/components/api-client/JsonPathPicker.tsx`

```tsx
interface JsonPathPickerProps {
  sample: string | null;           // JSON sample (response body or selected example)
  initialPath?: string | null;
  onSelect: (jsonPath: string) => void;
  onCancel: () => void;
}

export function JsonPathPicker({ sample, initialPath, onSelect, onCancel }: JsonPathPickerProps): JSX.Element;
```

Behavior:

- Renders a tree view of the parsed JSON sample. Each scalar and collection node is clickable; clicking generates a JSONPath (dot/bracket mix) and immediately previews it by calling `evaluateJsonPath`.
- If no sample is provided, offer a `<textarea>` to paste one.
- Live preview area shows `value` or `error`.
- **Use** button closes the picker and calls `onSelect(path)`.

### 6.4 Integration in capture rules

#### File: `web/src/components/api-client/RequestEditor.tsx`

Next to the JSONPath input in capture rules:

```tsx
<input ... data-testid={`capture-rule-path-${i}`} />
<button
  onClick={() => setJsonPathPickerRuleIndex(i)}
  title="Pick JSONPath from sample"
  data-testid={`capture-rule-picker-${i}`}
>
  <Crosshair className="h-3 w-3" />
</button>
```

When the picker closes with `onSelect`, update `request.captureRules[i].jsonPath`.

Sample selection inside the picker uses `request.responseExamples` first, then the most recent `response.responseBody`, then a paste area. If `responseExamples` is empty and no response exists, the picker starts in paste mode.

---

## 7. Persistence / localStorage keys

- `view-pref:env-manager-size` — outer dialog width/height.
- `panel-widths:env-manager-panels` — inner environment list vs editor split (via `ResizablePanels`).
- `panel-widths:api-client-panels` unchanged.

---

## 8. Accessibility & test hooks

- Every new button/select/input gets `data-testid`.
- Dialogs trap focus and close on `Escape` (reuse `Dialog` component where possible; `EnvironmentManager` will be migrated to `Dialog` shell for consistency).
- `aria-label` on the JSONPath picker tree and the resizable drag handle.
- Empty/loading/error states in the import dialog and JSONPath picker.

---

## 9. Validation steps

After implementation:

1. `(cd web && npm run build)`
2. `(cd src-sidecar && dotnet build)`
3. `(cd tests/SwebKit.Sidecar.Tests && dotnet test)`
4. `(cd web && npx playwright test)`

Add or extend `web/e2e/api-client.spec.ts` for:

- Resized Environment Manager size is restored after reload.
- Key Vault source no longer triggers a horizontal scrollbar (assertion on `overflow` or absence of `scrollWidth > clientWidth`).
- Generated environment variable `kind` can be changed and persisted.
- Collection import from a Postman/SwebKit JSON fixture succeeds.
- A pre-request `CopyToClipboard` action copies the request URL.
- JSONPath picker populates the capture rule path.
