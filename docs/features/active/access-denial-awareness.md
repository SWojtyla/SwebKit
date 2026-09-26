# Access-Denial Awareness (Phase 1)

State: Review

## Goal

In locked-down environments (typical PRD) the signed-in Entra identity has rights on only
some resources. Today a 403 reaches the model as a raw exception and a hidden SQL catalog
looks identical to an empty database. Phase 1 makes access failures first-class:

- agent tools return a structured `access_denied` result with the least-privilege fix;
- the model is told to aggregate denials into an "Access gaps" report instead of retrying;
- the SQL schema endpoint distinguishes *metadata hidden by policy* from *empty database*
  via the caller's own effective permissions — and the UI says so.

## Scope

- `AccessAdvisor` (`SwebKit.Core/Security`) — pure, duck-typed classifier: Azure SDK 401/403,
  Service Bus AMQP `Unauthorized`, k8s `HttpOperationException` 403, `HttpRequestException`
  401/403, `SqlException` 229/230/297, Redis NOAUTH/NOPERM, plus a conservative message
  fallback. Duck-typed because Core takes no Azure/SQL/Redis package references.
- `AccessAdvisor` also maps `FeatureArea` → least-privilege remedy (e.g. Service Bus →
  `Azure Service Bus Data Receiver`, Storage → `Storage Blob Data Reader`, SQL →
  `VIEW DEFINITION`/`db_datareader`).
- `AgentToolRegistry.ExecuteAsync` routes exceptions through the classifier and serializes
  `{"status":"access_denied", capability, featureArea, requiredAccess, guidance, detail}`.
- `AgentSystemPromptBuilder` instructs the model: access_denied = permission gap, don't
  retry, close the report with an "Access gaps" section naming `requiredAccess` per item.
- `ISqlClient.GetMyPermissionsAsync` → `sys.fn_my_permissions(NULL, 'DATABASE')` (works with
  zero catalog rights); `SqlSchemaModel.ApplyPermissionAnalysis` sets `MetadataHidden`
  when the tree is empty but SELECT/EXECUTE-family rights exist without VIEW DEFINITION.
- SchemaTree renders a "Schema hidden by permissions" state (`sql-schema-hidden`) listing
  held permissions and the grant to request.
- Third demo connection `demo-sql-prd` (`DemoSqlClient` variant 2) reproduces the
  locked-down behavior for demos and e2e.

## Non-goals (later phases)

- Capability probing/caching across features → per-environment access report (phase 2).
- Declared-object SQL trees + `SELECT TOP 0` column introspection (phase 3).
- Copyable access-request artifacts with principal/scope resolution (phase 3).
- PIM eligible-assignment awareness (phase 4).

## Implementation tasks

- [x] `AccessAdvisor` + `AccessDenial` record
- [x] `AgentToolRegistry` structured denial results (and proper JSON escaping on the
      generic error path — newline messages previously produced invalid JSON)
- [x] System-prompt access-gap guidance (interactive + background investigation)
- [x] `GetMyPermissionsAsync` on `ISqlClient`/`SqlDatabaseClient`/`DemoSqlClient`
- [x] `SqlSchemaModel.EffectivePermissions`/`MetadataHidden` + endpoint analysis
- [x] `demo-sql-prd` restricted demo connection + reserved-id guard
- [x] SchemaTree hidden-schema empty state + `sql.ts` fields
- [x] Tests + e2e

## Test plan

- `AccessAdvisorTests` (16): every exception shape, inner-exception unwrapping, remedy
  mapping, generic fallback — fakes carry exact SDK type names for the duck-typing.
- `AgentToolRegistryTests` (+3): structured denial JSON shape, non-denial exclusion,
  newline-message JSON validity.
- `SqlEndpointsTests` (+5): full-access not hidden, restricted demo → `MetadataHidden`,
  reserved id blocked outside demo, probe failure falls back gracefully.
- `web/e2e/sql.spec.ts` (+1): `demo-sql-prd` shows `sql-schema-hidden`, never
  `sql-schema-empty`; connection count updated to 3.

## Validation results

- Core tests 16/16 AccessAdvisor ✓ · Agents 10/10 registry ✓ · Sidecar 19/19 SQL endpoints ✓
- Playwright `sql.spec.ts` 9/9 ✓ · tsc clean · ESLint clean on changed files

## Decisions

- **Duck-typed classifier over typed SDK checks** — `SwebKit.Core` has no Azure/SQL/Redis
  refs; matching `Type.Name` + well-known members keeps one classifier shared by agent
  tools, endpoints, and future callers. Failure-path only, never hot-loop.
- **`sys.fn_my_permissions` over heuristics** — self-reported permissions need no catalog
  rights, so "empty tree" vs "hidden tree" is *certain*, not inferred.
- **Probe failure fails open** — a failed permission probe leaves `MetadataHidden` false;
  the UI falls back to the generic empty state rather than claiming a denial.
