# MiddleTier Service Pattern (Get + Save)

This skill describes how to implement a `ServiceBase` service supporting `IGetService` and/or `ISaveService`, with multiple partials, data windows, and parameters. Use `WorkflowConfigService` as the canonical reference implementation.

---

## File Structure

For a service named `{Name}`, the following files are required:

```
Parameters/
  {Name}GetParameters.cs          # IGetService parameters
  {Name}SaveParameters.cs         # ISaveService parameters (if save needed)

Views/
  Config/CRUD/
    {Name}DataWindow.cs           # One DataWindowRow class per partial (CRUD)
    {Name}DataWindow.sql          # SQL SELECT backing the DataWindowRow ← MUST match class name exactly
  Config/Search/
    {Name}_Search.cs              # DataWindowRow for the service-level list (SetDataViewAsync)
    {Name}_Search.sql             # SQL for the search/list view

Partials/
  Config/
    {Name}Complete.cs             # One PartialBase subclass per partial
    {Name}Step.cs
    ...

Service/
  {Name}Service.cs                # ServiceBase + IGetService [+ ISaveService]

PartialNames.cs                   # Central string constants for all partial names
```

**SQL file rule**: The `.sql` filename must exactly match the C# `DataWindowRow` class name. The resource manager key must also match. This is how `[QueryStore(typeof(WorkflowQueries))]` locates the SQL at runtime.

**Canonical SQL location for WorkflowConfig actions**:
`src\Brio.MiddleTier.v2.Workflow\Views\Config\CRUD\WorkflowActionConfigDataWindow.sql`

---

## 1. SQL File

Write a `SELECT` that joins all required tables. Columns that belong to the updatable table must be prefixed with the table alias. Computed columns (e.g. `CASE WHEN @lang = ...`) are read-only.

```sql
-- src\Brio.MiddleTier.v2.Workflow\Views\Config\CRUD\WorkflowActionConfigDataWindow.sql
SELECT dcaf.p_dict_wkfl_action_config,
    dca.p_dict_concrt_action,
    dcaf.f_active,
    dcaf.f_auto_execute,
    (CASE WHEN @lang = '1' THEN dca.description_fr ELSE dca.description_nl END) AS description
FROM dict_concrt_action dca
INNER JOIN dict_wkfl_action_config dcaf ON dcaf.p_dict_concrt_action = dca.p_dict_concrt_action
WHERE dca.p_dict_concrt_dt_ent IN (...)
```

Register it in `WorkflowQueries.resx` with the key matching the C# class name (e.g. `WorkflowActionConfigDataWindow`). The Designer file is auto-generated.

---

## 2. DataWindowRow Class

```csharp
[QueryStore(typeof(WorkflowQueries))]          // links to the .sql resource; key = class name
[UpdateTable(1, "dict_wkfl_action_config")]    // marks the table targeted for INSERT/UPDATE/DELETE
public class WorkflowActionConfigDataWindow : DataWindowRow
{
    // PK of the updatable table: Key=true, Update=true (for WHERE clause on UPDATE)
    [Display(Order = 1, AutoGenerateField = true)]
    [PropertyMetadata(IsModifiable = true, Type = "N:9",
        Update = true, UpdateWhereClause = true, Key = true,
        DbName = "dict_wkfl_action_config.p_dict_wkfl_action_config",
        Table = "dict_wkfl_action_config")]
    public int p_dict_wkfl_action_config { get; set; }

    // Editable column on the updatable table
    [Display(Order = 8, AutoGenerateField = true)]
    [PropertyMetadata(IsModifiable = true, Type = "S:1",
        Update = true, UpdateWhereClause = true,
        DbName = "dict_wkfl_action_config.f_active",
        Table = "dict_wkfl_action_config")]
    public string? f_active { get; set; }

    // Read-only column from a joined table
    [Display(Order = 2, AutoGenerateField = true)]
    [PropertyMetadata(IsModifiable = false, Type = "N:9",
        UpdateWhereClause = true,
        DbName = "dict_concrt_action.p_dict_concrt_action")]
    public int p_dict_concrt_action { get; set; }

    // Read-only computed column
    [Display(Order = 15, AutoGenerateField = true)]
    [PropertyMetadata(IsModifiable = false, Type = "S:255",
        UpdateWhereClause = false, DbName = "description")]
    public string? description { get; set; }
}
```

**Rules**:
- Only columns from the `[UpdateTable]` table get `Update = true` and `Table = "..."`.
- The PK additionally gets `Key = true`.
- Columns from joined tables get `IsModifiable = false` with no `Update`/`Table`.
- Computed expressions get `UpdateWhereClause = false`.
- `Display(Order = N)` controls serialization order; start at 1 and increment.

---

## 3. Parameters

### Get Parameters

```csharp
public class WorkflowConfigGetParameters
    : Parameters, IXmlParameters, IPartialParameters, IRequestIdParameters
{
    [CustomRange(0, int.MaxValue, true)]
    public int WorkflowConfigId { get; set; } = -1;

    public string? XML { get; set; }
    public List<string>? SUB { get; set; }
    public string? RequestId { get; set; }
}
```

### Save Parameters

Mirror the Get parameters. Properties with matching names are automatically copied by `GenericMapper.MapFromTo` during the post-save re-GET.

```csharp
public class WorkflowConfigSaveParameters
    : Parameters, IXmlParameters, IPartialParameters, IRequestIdParameters
{
    [CustomRange(0, int.MaxValue, true)]
    public int WorkflowConfigId { get; set; } = -1;  // copied to _getParameters after save

    public string? XML { get; set; }
    public List<string>? SUB { get; set; }
    public string? RequestId { get; set; }
}
```

---

## 4. Partial

One `PartialBase` subclass per data window. The constructor switches on `Method` to cast the concrete parameters:

```csharp
public class WorkflowConfigAction : PartialBase
{
    protected WorkflowConfigGetParameters? GetParameters { get; set; }

    public WorkflowConfigAction(IDatabase database, IParameters brioParameters, IParameters concreteParameters)
        : base(database, brioParameters)
    {
        switch (brioParameters.Method)
        {
            case Method.Get:
                GetParameters = (WorkflowConfigGetParameters)concreteParameters;
                break;
            // Method.Save: no cast needed — SetDataViewAsync guards on Method.Get
        }
    }

    protected override Task<IDataWindow> SetDataViewAsync()
    {
        var dataWindow = DataWindow.Create<WorkflowActionConfigDataWindow>();

        // Only add query parameters for GET; during SAVE the structure is used without querying
        if (BrioParameters.Method == Method.Get && GetParameters != null)
        {
            dataWindow.Query.AddParameter("@p_dict_concrt_wkfl", GetParameters.WorkflowConfigId);
            dataWindow.Query.AddParameter("@lang", (int)BrioParameters.Language);
        }

        dataWindow.ObjectName = "workflowConfigAction";
        dataWindow.ObjectNameExtraRowIdentifier = "workflowConfigActionRow";

        return Task.FromResult(dataWindow);
    }

    // Optional: per-row modifiability overrides (runs on both GET and SAVE)
    protected override Task SetModifiableRowAsync(IDataWindow collection)
    {
        foreach (var entry in collection)
        {
            // Lock read-only (non-updatable table) columns
            entry.ModifyColumnMetadata(nameof(WorkflowActionConfigDataWindow.p_dict_wkfl_action_config), isModifiable: false);
            entry.ModifyColumnMetadata(nameof(WorkflowActionConfigDataWindow.p_dict_concrt_action), isModifiable: false);
            entry.ModifyColumnMetadata(nameof(WorkflowActionConfigDataWindow.description), isModifiable: false);

            // Unlock editable (updatable table) columns
            entry.ModifyColumnMetadata(nameof(WorkflowActionConfigDataWindow.f_active), isModifiable: true);

            // Conditionally: f_auto_execute only editable when f_can_be_automatized == "1"
            if (entry.GetItem<string>(nameof(WorkflowActionConfigDataWindow.f_can_be_automatized)) == "1")
                entry.ModifyColumnMetadata(nameof(WorkflowActionConfigDataWindow.f_auto_execute), isModifiable: true);
            else
                entry.ModifyColumnMetadata(nameof(WorkflowActionConfigDataWindow.f_auto_execute), isModifiable: false);
        }

        return Task.CompletedTask;
    }
}
```

**`SetModifiableRowAsync` rules**:
- Called on both GET (to inform the client) and SAVE (to validate incoming changes).
- Use `entry.GetItem<T>("columnName")` to read the controlling field value.
- Use `entry.ModifyColumnMetadata("columnName", isModifiable: true/false)` to override the static `[PropertyMetadata]` attribute per row.
- Use `entry.IsModifiable = false` to lock the entire row.
- Always use `nameof(DataWindowRowClass.PropertyName)` — never hardcoded strings.

---

## 5. PartialNames

Register every partial name as a `const string` in `PartialNames.cs`:

```csharp
internal static class PartialNames
{
    public const string WorkFlowConfigComplete  = "workFlowConfigComplete";
    public const string WorkFlowConfigStep      = "workFlowConfigStep";
    public const string WorkFlowConfigDataEntity = "workFlowConfigDataEntity";
    public const string WorkFlowConfigAction    = "workFlowConfigAction";
    public const string WorkflowConfigList      = "workflowConfigList";      // SetDataViewAsync ObjectName
    public const string WorkflowConfigListRow   = "workflowConfigListRow";   // SetDataViewAsync ObjectNameExtraRowIdentifier
}
```

---

## 6. Service

```csharp
[MiddletierService("N_WORKFLOW_CONFIG")]
public sealed class WorkflowConfigService : ServiceBase, IGetService, ISaveService
{
    private WorkflowConfigGetParameters?  _getParameters;
    private WorkflowConfigSaveParameters? _saveParameters;

    // ISaveService properties
    public string SaveResultError    { get; private set; } = string.Empty;
    public int    SaveResultErrorCode { get; private set; }

    public WorkflowConfigService(IParameters parameters, IGenericSerializer serializer, IDatabase database)
        : base(serializer, parameters, database)
    {
        Partials = [
            PartialNames.WorkFlowConfigComplete,
            PartialNames.WorkFlowConfigStep,
            PartialNames.WorkFlowConfigDataEntity,
            PartialNames.WorkFlowConfigAction,
        ];
        MasterTag = "workflowConfigMaster";
    }

    // --- GET ---

    public async Task<Dictionary<string, IDataWindow>> ExecuteGetAsync(
        Dictionary<string, PartialConfiguration> requestedPartials)
    {
        var components = new Dictionary<string, IDataWindow>();
        if (_getParameters == null) return components;

        if (requestedPartials.ContainsKey(PartialNames.WorkFlowConfigAction))
        {
            var action = new WorkflowConfigAction(Database, BrioParameters, _getParameters);
            var result = await action.GetAsync(requestedPartials, PartialNames.WorkFlowConfigAction);
            if (result != null) components.Add(PartialNames.WorkFlowConfigAction, result);
        }
        // ... repeat for other partials

        return components;
    }

    public Dictionary<string, Required> GetRequiredPartials() => new();

    // --- SAVE ---

    public async Task<SaveResult> ExecuteSaveAsync(
        Dictionary<string, IDataWindow> components,
        List<string> requestedPartials)
    {
        int? result = 0;

        // Only WorkFlowConfigAction is savable; other partials are read-only config data
        if (requestedPartials.Contains(PartialNames.WorkFlowConfigAction))
        {
            var action = new WorkflowConfigAction(Database, BrioParameters, _saveParameters!);
            (result, SaveResultError) = await action.SaveAsync(
                components, requestedPartials, PartialNames.WorkFlowConfigAction);
        }

        if (result != 1)
        {
            SaveResultErrorCode = result ?? -1;
            return SaveResult.Error;
        }

        // Re-populate get parameters so the framework re-GETs after save
        _getParameters = new WorkflowConfigGetParameters();
        GenericMapper.MapFromTo(_saveParameters, _getParameters);  // copies matching property names
        _getParameters.SUB = _saveParameters!.SUB;
        _getParameters.XML = _saveParameters.XML;
        PartialParameters = _getParameters;

        return SaveResult.ExecuteGet;  // triggers automatic GET after save
    }

    // --- ROUTING ---

    protected override IParameters GetConcreteParameters()
    {
        if (BrioParameters.Method == Method.Get)
            return _getParameters ??= GetConcreteParameters<WorkflowConfigGetParameters>();

        if (BrioParameters.Method == Method.Save)
            return _saveParameters ??= GetConcreteParameters<WorkflowConfigSaveParameters>();

        return null!;
    }

    // OnBeforeRetrieveAsync: append @lang and ORDER BY for the service-level list
    protected override Task OnBeforeRetrieveAsync(QueryBuilder builder, Type type)
    {
        builder.AddParameter("@lang", (int)BrioParameters.Language);
        builder.Append("ORDER BY dcw.p_dict_concrt_wkfl ASC");
        return Task.CompletedTask;
    }

    // SetDataViewAsync: used for the service-level list (Search/retrieve all)
    protected override Task<IDataWindow> SetDataViewAsync()
    {
        var dataWindow = DataWindow.Create<WorkflowConfig_Search>();
        dataWindow.ObjectName = PartialNames.WorkflowConfigList;
        dataWindow.ObjectNameExtraRowIdentifier = PartialNames.WorkflowConfigListRow;
        dataWindow.ObjectNameExtraDatakeys = "p_dict_concrt_wkfl";
        return Task.FromResult(dataWindow);
    }
}
```

---

## Save Flow Summary

```
Client → POST (Method.Save)
  └─ GetConcreteParameters() → WorkflowConfigSaveParameters
  └─ ExecuteSaveAsync()
       └─ WorkflowConfigAction.SaveAsync()           (PartialBase)
            └─ SetDataViewAsync()                    → DataWindow structure (no query)
            └─ SetModifiableRowAsync()               → per-row validation
            └─ PrepareSaveAsync() + CommitChanges()  → INSERT/UPDATE/DELETE on dict_wkfl_action_config
       └─ result == 1 → GenericMapper.MapFromTo(_saveParameters → _getParameters)
       └─ PartialParameters = _getParameters
       └─ return SaveResult.ExecuteGet
  └─ Framework auto-executes GET with _getParameters
```

---

## Key Interfaces & Base Classes

| Type | Purpose |
|------|---------|
| `ServiceBase` | Base for all services; handles routing, serialization, parameter wiring |
| `IGetService` | `ExecuteGetAsync` + `GetRequiredPartials` |
| `ISaveService` | `ExecuteSaveAsync` + `SaveResultError` + `SaveResultErrorCode` |
| `PartialBase` | Base for partials; provides `GetAsync`, `SaveAsync`, `SetDataViewAsync`, `SetModifiableRowAsync` |
| `SaveResult` | `Return = 0`, `ExecuteGet = 1` (triggers re-GET), `Error = -1` |
| `GenericMapper.MapFromTo` | Copies matching property names between two objects (used save → get params) |
| `[MiddletierService("N_...")]` | Registers the service with its routing key |
| `[QueryStore(typeof(WorkflowQueries))]` | Links DataWindowRow to its SQL resource; key = class name |
| `[UpdateTable(order, "tableName")]` | Marks the target DB table for commit operations |
| `[PropertyMetadata(...)]` | Per-column static modifiability, type, update, and DB mapping |
