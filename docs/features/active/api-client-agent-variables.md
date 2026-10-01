# API Client agent: collection variables + file form fields

State: Review

## Goal

The agent can already create full requests, but two holes force it to hand work
back to the user (observed: rebuilding a multi-sign Bruno flow — agent created
the requests, then listed "set the collection variables" and "mark the file
field as a file upload" as manual follow-ups):

1. **No variable mutation path.** `CollectionVariable` (incl. `Guid` generator)
   exists in the model, but `IApiClientAgentService` exposes no set-variable
   operation. The agent cannot seed `baseUrl`, package-id placeholders, or a
   regenerating GUID variable — everything `{{var}}` refs and `capture_rules`
   depend on.
2. **No file-typed form fields anywhere.** `RequestBody.FormData` is
   `List<KeyValuePair<string>>` and `HttpRequestExecutor` wraps every value in
   `StringContent`, so a multipart `file` part is not even representable — the
   agent's fallback uploads the PDF _path_ as text.

## Scope

- `IApiClientAgentService.SetCollectionVariableAsync(collectionIdOrName, key,
value?, generator?, enabled)` — id-or-name resolution and auto-collection
  creation reuse the `CreateRequestAsync` semantics; upsert by key.
- `AgentActionType.SetCollectionVariable` + `ApiClientActionExecutor` branch;
  `propose_collection_variable_change` tool (pending-confirm like every other
  mutation). Params: collection, key, value, generator (`guid`, `integer`…),
  enabled.
- `FormDataField` model (`Key`, `Value`, `IsEnabled`, `IsFile`) replacing the
  bare `KeyValuePair<string>` in `RequestBody.FormData`; when `IsFile` the value
  is a local path (post-`{{var}}` substitution) and the executor sends
  `ByteArrayContent` + filename so text and file parts can mix in one multipart.
- `BodyPanel`: per-row Text/File toggle + path input + Browse button wired to
  the native `pick_file` Tauri command (`pickFilePath` bridge; browser fallback
  uses a hidden file input).
- `propose_api_request_change`: `form_data` items accept `type: "text"|"file"`;
  description steers toward `propose_collection_variable_change` + capture_rules
  chaining instead of deferring to the user.
- Bruno/Postman export-import: map file fields where the format has one
  (Postman `type:"file"`/`src`); otherwise leave untouched.

## Non-goals

- Environment-variable mutation tool (env-level `baseUrl`) — collection
  variables cover the reported gap; env ops are a follow-up.
- Executing the upload — execution stays behind `prepare_api_request_execution`
    - confirm.
- File-existence validation at proposal time (path may carry `{{vars}}`).

## Tasks

- [x] `FormDataField` + `RequestBody.FormData` type change; executor file parts.
- [x] `IApiClientAgentService.SetCollectionVariableAsync` + impl.
- [x] `AgentActionType.SetCollectionVariable`, executor branch, tool + schema.
- [x] `BodyPanel` row toggle; apiClient TS type for `formData[].isFile`.
- [x] Tool-schema `form_data[].type`; description update.
- [x] Tests: Agents executor/tool tests, Core multipart-file executor test,
      service upsert tests; e2e for the file toggle + variable proposal.

## Test plan

- `dotnet test` Agents/Core/Sidecar suites.
- vitest + `npm run build`.
- Playwright: api-client spec for the form-data file toggle.

## Validation

- `dotnet test` Agents: 381 passed (new executor tests cover variable payload
  pass-through, generator-over-static, missing key, `type:"file"` → `IsFile`).
- `dotnet test` Core: 964 passed (new: 7 `SetCollectionVariableAsync` upsert /
  generator / auto-collection / guard tests, FormData copy regression, 5
  executor multipart tests incl. missing-file text fallback).
- `dotnet test` Sidecar: 842 passed, 2 flaky SB park/restore timing failures
  (pass in isolation; unrelated area).
- `vitest`: 798 passed · `tsc --noEmit` clean · eslint clean.
- e2e `api-client.spec.ts`: new form-data test passes — text/file toggle,
  browse button via `filechooser`, placeholder switch.
- Aikido: findings are all false positives for a REST client (user-chosen file
  paths and request URLs are the feature; interpolated summary strings are not
  SQL).
