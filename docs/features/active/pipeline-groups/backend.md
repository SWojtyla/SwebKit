# Backend — Pipeline Groups

## Goal

Add the `PipelineGroup` domain model to `DevOpsConfig` and ensure it serialises/deserialises correctly through `ProfileRepository`.

## Changes

### `src/SwebKit.Core/Domain/DevOpsConfig.cs`

Add two records and a new property on `DevOpsConfig`:

```csharp
public record PipelineGroupEntry(string ProjectName, int PipelineId, string PipelineName);

public record PipelineGroup(string Id, string Name, List<PipelineGroupEntry> Pipelines);

public class DevOpsConfig
{
    // ... existing properties ...
    public List<PipelineGroup> PipelineGroups { get; set; } = [];
}
```

`PipelineGroup.Id` is generated as `Guid.NewGuid().ToString("N")` on creation.

### `src/SwebKit.Core/Serialization/`

If `SwebKitJsonOptions` uses source-generated contexts, add `PipelineGroup` and `PipelineGroupEntry` to the JSON context to support serialisation. Otherwise the default STJ reflection serialiser handles them automatically.

### Validation note

No additional `Validate()` changes needed — groups are optional and empty by default.

## Test coverage expectations

- `DevOpsConfig` serialises and deserialises `PipelineGroups` round-trip correctly (JSON)
- A `PipelineGroup` with zero entries is valid
- `PipelineGroupEntry` stores the project name, pipeline ID, and display name as given
