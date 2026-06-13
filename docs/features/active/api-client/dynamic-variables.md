# Dynamic Variables — API Client Phase 10

---

## Scope

Add generated variables to the API client so users can define values with building blocks instead of hard-coded strings. Examples: random age between 10 and 20, random first name, random last name, GUID, timestamp, random item from a list, or date offset.

This extends the existing collection/environment variable system. It does not introduce scripting or arbitrary code execution.

---

## Goals

- Let users create generated variables from safe, explicit building blocks.
- Support common random data without requiring users to write scripts.
- Keep generated variable definitions readable in local and linked collection files.
- Make preview/test generation visible before a request is sent.
- Keep secrets separate; generated variables are non-secret values only.

---

## Recommended Generator Library

Use `Bogus` for realistic fake data categories:

- first name
- last name
- full name
- email
- phone
- company
- city/country/address when needed later

Use SwebKit-owned generators for deterministic/simple primitives:

- integer range
- decimal range
- boolean
- GUID
- timestamp/date offset
- random list item
- string template composition

Rationale: Bogus is a well-known .NET faker library and saves us from maintaining name/address datasets. It should not own the whole feature; constraints and variable resolution remain SwebKit-owned.

---

## Variable Definition Shape

Add an optional generated-value definition beside the existing variable value fields.

Example JSON shape for linked files:

```json
{
  "variables": {
    "baseUrl": "https://dev-api.example.com"
  },
  "generatedVariables": {
    "age": {
      "type": "integer",
      "min": 10,
      "max": 20
    },
    "firstName": {
      "type": "faker",
      "category": "person.firstName"
    },
    "userEmail": {
      "type": "template",
      "template": "{{firstName}}.{{lastName}}@example.com"
    }
  }
}
```

In app-local `EnvironmentVariable` / `CollectionVariable`, this can be represented by a nullable generator definition object rather than overloading `Value`.

---

## Generator Types

| Type       | Fields                         | Example                           |
| ---------- | ------------------------------ | --------------------------------- |
| `integer`  | `min`, `max`                   | age 10-20                         |
| `decimal`  | `min`, `max`, `scale`          | price 1.00-99.99                  |
| `boolean`  | optional probability           | feature flag                      |
| `guid`     | format                         | request correlation ID            |
| `dateTime` | `from`, `to`, `offset`, format | due date +7 days                  |
| `list`     | `values[]`                     | status from known values          |
| `faker`    | `category`, optional locale    | first/last name                   |
| `template` | `template`                     | compose generated + static values |

---

## Resolution Semantics

- Generated variables resolve when building the request scope.
- Values are generated per request send by default.
- Add a per-variable mode later if needed:
  - `perSend` (default)
  - `perSession`
  - `stickyUntilRefresh`
- Variable dependency order matters for templates. Resolve non-template generators first, then templates.
- If a generated variable fails, leave the token unresolved and surface a preview warning.

---

## UI Integration

### Environment/Collection Variable Editors

Add a Type selector:

```text
Plain | Secret Store | Key Vault | Generated
```

When `Generated` is selected, show a building-block editor:

```text
Kind: [Integer range ▼]
Min: 10
Max: 20
[Preview] 14
```

For faker:

```text
Kind: [Fake data ▼]
Category: [Person / First name ▼]
Locale: [default ▼]
[Preview] Emma
```

### Request Preview

The existing inline variable preview should show generated samples:

```text
{{age}} -> 17
{{firstName}} -> Nora
```

Add a small refresh button in the preview strip to regenerate sample values without sending.

---

## Architecture Touchpoints

- `src/SwebKit.Core/Domain/ApiClientModels.cs`
  - Add generator definition model.
  - Add generator fields to collection/environment variables.
- `src/SwebKit.Core/Services/VariableSubstitutionService.cs`
  - Resolve generated variables into the request scope.
- `src/SwebKit.Core/Services/VariablePreviewService.cs`
  - Preview generated values and warnings.
- `src/SwebKit.App/Components/ApiClient/EnvironmentEditor.razor`
  - Add generated variable editor UI.
- `src/SwebKit.App/Components/ApiClient/CollectionVariableEditor.razor`
  - Add generated variable editor UI.
- Linked collection format
  - Add `generatedVariables` section to `.swebenv.json` / collection manifests.

---

## Implementation Tasks

- [x] Add `VariableGeneratorDefinition` domain model and enum.
- [x] Add generator support to collection and environment variables.
- [x] Add `IVariableGeneratorService` with deterministic primitive generators.
- [x] Add `Bogus` package and faker generator adapter.
- [x] Extend `VariableSubstitutionService` to resolve generated variables.
- [x] Existing `VariablePreviewService` previews generated values through resolved scope.
- [x] Add generated-variable editors to environment and collection variable screens.
- [x] Extend linked `.swebenv.json` and collection manifest read/write with `generatedVariables`.
- [x] Add tests for range constraints, faker fields, template composition, scope resolution, and serialization.

---

## Validation Notes

- Unit: integer generator respects inclusive min/max.
- Unit: invalid constraints produce warnings, not exceptions.
- Unit: faker first/last name generates non-empty values.
- Unit: template variables compose generated values.
- Unit: generated variables serialize in linked root files without generated sample values.
- UI: generated variable editor changes shape when the kind changes.
- Manual: request send regenerates values per send; preview refresh regenerates without sending.
