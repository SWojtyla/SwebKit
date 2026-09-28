# Access Awareness Phases 2–4 — access report, request artifacts, declared-object SQL, request webhook

State: In Progress

Phase 1 (classification + agent `access_denied` results + SQL `MetadataHidden`) is shipped —
see `access-denial-awareness.md`. This plan covers the rest of the arc.

## Hard findings that shape the design

- **Most connection entries carry no ARM identity** — only `ObservabilityConfig.
  SelectedResourceId` is a real ARM id. SQL/SB/Storage/Redis entries store names/FQDNs.
  `SqlDiscoveredServer` already *discovers* `SubscriptionId`/`ResourceGroup`/`ResourceId`
  but `SqlSettings` drops them on save. Scope resolution needs new optional fields +
  seed-from-discovery.
- **Nothing parses JWT claims** — need `IAzurePrincipalContext` (decode ARM token payload:
  `oid`/`upn`/`tid`; zero extra scopes, no Graph call).
- **Several agent tools swallow exceptions into `{"error"}`** (`ListSqlTablesTool`,
  `DescribeSqlTableTool`, `ListStorageBlobsTool`, …) — bypassing the classifier. Fix by
  rethrowing or pre-classifying before the generic error.
- **SB RBAC granularity**: entity listing needs the Manage claim, peek needs Data Receiver —
  `servicebus.manage` and `servicebus.peek` are separate capabilities, not one status.
- **Redis `AbortOnConnectFail=false`** → probes need an external timeout.
- **`demo-sql-prd` is invisible to agent tools** — `SqlToolContext` only knows the first two
  demo ids. Fix for demo parity.
- **PIM/Teams Power App**: user requests access via a Power App (likely a Power Automate
  HTTP-triggered flow); calls will be captured later — design the webhook generic enough to
  absorb the real shape (URL via credential store + templated body).

## Scope

### Phase 2 — per-environment access report

- `AccessReportModels` + `IAccessReportService` (Core): `AccessProbeResult{FeatureArea,
  ConnectionKey, Capability, Status: Ok|Denied|Unknown, CheckedAt, Denial?, ErrorSummary?}`,
  `AccessReportEntry{Label, ScopeResourceId?, Capabilities}`.
- `AccessReportService` (sidecar singleton): 5-min TTL cache keyed `area|conn|cap`,
  per-probe coalescing, `Task.WhenAll` + 8s per-probe timeout, `RecordObservedDenial` +
  `TryGetKnownDenial` for un-probeable capabilities (e.g. `servicebus.send`), invalidation
  hooked into `SaveProfileAsync`'s existing stale-id computation + `InvalidateAll` on
  import/mode change.
- Probe adapters (`src-sidecar/Services/AccessProbes/`, one per area, all through existing
  pools — never raw SDK clients): SB `GetNamespaceInfoAsync` (manage) + first-entity peek
  (data); Storage `ListContainersAsync`; Redis connect + `ScanKeysAsync(1)` time-boxed; AKS
  `GetNamespacesAsync`; Observability `RunQueryAsync("print 'probe'")`; SQL
  `GetMyPermissionsAsync` + `GetSchemaAsync`→`MetadataHidden` (sqlGrant remedy, not ARM).
- Non-Entra auth modes → row marked `authMode:"connectionString"`, no `az` artifacts.
- Endpoints: `GET /api/access/report[?refresh]`, single-entry refresh.
- Settings → new "Access" tab: grouped table, per-capability status dot, denied rows carry
  `requiredAccess`/`guidance` + "Request access" button, refresh control.
- Agent: `IAccessAwareTool.GetConnectionKey(args)` opt-in interface; registry consults
  `TryGetKnownDenial` → emits the same `access_denied` JSON + `"cached":true` (never
  pre-empt on `Unknown`); observed denials feed `RecordObservedDenial` (fills
  `servicebus.send`, `sql.query`); fix the swallowing tools.
- Demo: probe demo clients normally + synthesize the `demo-sql-prd` `sql.metadata` denied
  row so e2e sees a red row.

### Phase 3a — copyable request artifacts

- `IAzurePrincipalContext` → `ResolvedPrincipal{ObjectId,Upn,TenantId,DisplayName}` from
  the ARM token's JWT payload (cached for session; degrade gracefully for SPs — no `upn`).
- Optional ARM fields on `SqlConnectionEntry`/`ServiceBusNamespace`/`StorageConfig`/
  `RedisCacheEntry`/`AksConfig` (SubscriptionId/ResourceGroup/ResourceId); seed through
  when picking a discovered server; plain optional "Azure resource ID" fields in settings;
  **never guess scope from a hostname**.
- `AccessRemedy{Kind: armRole|sqlGrant|redisAcl|kubeRbac|other, RoleName, GrantStatement,
  Instructions}` on `AccessDenial` (additive); `AssignmentKind:"permanent"` reserved —
  PIM hook stays open.
- `POST /api/access/request` → `AccessRequestArtifact{role, scope?, principal, resource,
  summaryText, azCommand?, grantStatement?, remedyKind, assignmentKind, webhookConfigured}`;
  `azCommand` emitted only when `armRole` + scope + `ObjectId` all known (else fallback
  text "run `az ad signed-in-user show`").
- `AccessRequestDialog`: summary block, azCommand code block, Copy (always) + Send
  (when webhook configured).

### Phase 3b — declared-object SQL browsing

- `SqlConnectionEntry.DeclaredObjects` (`"schema.name"`, `ValidateIdentifier` on save) +
  chip editor in `SqlSettings.ConnectionRow`.
- Merge declared objects into `SqlDatabaseClient.GetSchemaAsync` (client layer — agent
  tools benefit too) with `IsDeclared` flag; same for `DemoSqlClient` variant 2.
- `ISqlClient.GetObjectColumnsAsync(s, o, db)` → `SELECT TOP 0 * FROM [s].[o]` +
  `GetColumnSchema()` (result-set metadata needs only SELECT — no catalog rights);
  229/230 denial flows through `AccessAdvisor`.
- SchemaTree: declared objects render with badge; expand → lazy `useSqlObjectColumns` →
  merged into cached schema → autocomplete/query-builder work on declared objects.

### Phase 4 — access-request webhook (Teams Power App hook point)

- `AppConfig.AccessRequestConfig{EndpointUrl or EndpointUrlCredentialKey, Method,
  Headers, BodyTemplate}` — URL/headers via `SidecarCredentialStore` (Power Automate
  trigger URLs embed a SAS `sig` → secret by construction).
- `BodyTemplate` placeholders `{role} {scope} {principal} {principalId} {resource}
  {capability} {justification}` substituted as JSON values (unquoted placeholders).
- `POST /api/access/request/send` → `AccessRequestSender` (named `IHttpClientFactory`,
  15s timeout, sanitized errors); "Send request" button + webhook config UI in the
  Access tab with a test-send. When the real Power App calls are captured, they drop in
  as URL+headers+template — no code change.
- `AssignmentKind` enum left open for a later `"pim"` variant.

## Non-goals

- PIM eligible-assignment activation flow (hook reserved via `AssignmentKind`).
- Probing `servicebus.send` non-destructively (impossible — observed-denial feed only).
- Session-entity probing nuances; generic KQL rule creation.

## Implementation tasks

- [x] Core models + `IAccessReportService` contract (commit 41754ac1)
- [x] `AccessReportService` + per-area probe adapters + endpoints + save-invalidation
      (commit 41754ac1 — `RecordObservedDenial`/`TryGetKnownDenial` in place)
- [x] `IAccessAwareTool` + registry known-denial short-circuit + observed-denial feed +
      fix exception-swallowing tools + `demo-sql-prd` in `SqlToolContext` (commit 8e127eff)
- [x] Access Settings tab + report UI (commit 952d8b1a)
- [x] `IAzurePrincipalContext` + ARM fields + `ScopeResolver` + artifact endpoint +
      dialog (commit 952d8b1a — PascalCase remedy-kind wire contract pinned by test)
- [x] `DeclaredObjects` + schema merge + columns endpoint + SchemaTree/settings UI
      (commit 4fe8fc89 — `exec:`-prefixed procs, per-object denial payloads,
      declared objects excluded from schema compare, `demo-sql-prd` seeds a
      granted view + runnable proc + object-denied view)
- [ ] `AccessRequestConfig` + credential-store plumbing + sender + webhook UI

## Test plan

- Core: remedy-kind mapping, JWT decode (user/SP/malformed), model serialization.
- Sidecar: TTL/coalescing/invalidation, probe adapters (ok/denied/timeout), artifact
  endpoint shape permutations, webhook send 200/500/timeout.
- Agents: cache short-circuit emits identical JSON, observed-denial recording, formerly
  swallowing tools emit `access_denied`.
- Frontend: Access tab statuses, dialog copy/send-disabled states, declared-object
  expand.
- e2e (`access.spec.ts` + `sql.spec.ts` extension): demo report red row, request dialog,
  declared-object browse on `demo-sql-prd`.

## Sequencing

2-core → 2-agent → 2-UI → 3a → 3b → 4.

## Risks

- `SELECT TOP 0 *` needs SELECT on the object (fine — surfaces a clean 229 denial);
  column-level deny grants can fail `*` shapes — documented.
- SB peek probe needs an entity path (`SbEntityLinks` fallback: first-page
  `GetQueuesAsync`) — manage vs peek stay separate rows to avoid false denies.
- SP/machine tokens may lack `upn` — artifact degrades to `appid`/`oid` wording.
- `AgentToolRegistry` ctor change ripples to tests — make `IAccessReportService`
  optional.
