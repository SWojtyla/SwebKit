# AI Cockpit Usability — Technical Plan

Module order is dependency order: 1 → 2 → 5 are backend/agent, 3–4 and 6 are frontend.
Modules 3, 4, 6 are independent of 1–2 and can land in any order relative to them.

## Module 1 — Service Bus tools go through the connection pool

**Files:** `src/SwebKit.Agents/Tools/GetQueueStatsTool.cs`, `GetQueueMessagesTool.cs`,
`AnalyzeQueueHealthTool.cs`; new `src/SwebKit.Agents/Tools/ServiceBusToolContext.cs`;
`src-sidecar/Program.cs` (no change needed — tools are resolved via `IAgentTool` DI);
`src/SwebKit.App/Hosting/SwebKitServiceCollectionExtensions.Agents.cs` (only if the
MAUI host needs the adapter below); tests in `tests/SwebKit.Agents.Tests/ServiceBusToolsTests.cs`.

### The problem

All three tools do:

```csharp
var ns = namespaces[0];                                   // ignores selection entirely
var connectionString = _credentialStore.Get(ns.CredentialKey);  // MAUI-era convention
client = _sbFactory.Create(connectionString);             // no AuthMode, no TransportType, no pooling
```

The working path every `ServiceBusEndpoints` handler uses is
`IServiceBusConnectionPool.GetOrCreate(ns)`, which branches on `ns.AuthMode`
(`factory.Create(ns.CredentialKey, ns.TransportType)` vs
`factory.CreateWithEntra(ns.FullyQualifiedNamespace, ns.TransportType)`) and caches the
client per namespace id (`src-sidecar/Services/SidecarServiceBusConnectionPool.cs:48-51`).

### The fix

New `ServiceBusToolContext` (static helper, mirrors `RedisToolContext`):

```csharp
Resolution ResolveNamespace(
    AppStateService appState,           // namespaces + UseDemoData
    string? requested,                  // alias, FQDN, or id — from the tool argument
    AgentChatContext? ambient)          // session selection (Module 2)
```

Resolution order (per the user's chosen semantics — selection, else error):

1. Explicit `namespace` argument — match `Alias` (case-insensitive), then
   `FullyQualifiedNamespace`, then `Id`. An unmatched explicit value is an error
   ("namespace 'x' not found — configured: a, b"), never a fallback.
2. Ambient selection: `Selection["nsId"]` (Guid match on `Id`) — that's what
   `ServiceBusPage.tsx` already passes — and `Selection["namespace"]` /
   `Selection["nsAlias"]` if present, for future callers.
3. Exactly one configured namespace → use it (unambiguous, not a silent guess).
4. Otherwise → error JSON listing every configured alias, so the model can either
   retry with an explicit argument or tell the user to pick one. **Never**
   `namespaces[0]` — that is the bug being fixed.

Client acquisition in each tool becomes:

```csharp
IServiceBusClient client = _appState.UseDemoData
    ? DemoServiceBusClient.OrdersDev()
    : _pool.GetOrCreate(ns);
```

i.e. drop `ICredentialStore`/`IServiceBusClientFactory` from the tools entirely; take
`IServiceBusConnectionPool` (defined in `SwebKit.Core.Abstractions`, implemented by
`SidecarServiceBusConnectionPool`). Also drop the per-call `DisposeAsync` — the pool
owns the client's lifetime. This automatically fixes Entra namespaces, `AmqpWebSockets`,
and the connect-per-call leak in one move.

Tool schema changes: `get_queue_stats` and `get_queue_messages` gain an optional
`namespace` parameter ("alias, FQDN, or id of a configured namespace; omit to use the
one selected in the UI"). `analyze_queue_health` keeps `namespace_alias` working but
the description is corrected to mention the same resolution. Descriptions should tell
the model that omitting the parameter uses the selected namespace — that's what makes
"I opened it with this context" work.

### MAUI host compatibility

`IServiceBusConnectionPool` is only implemented in the sidecar today; the same three
tools are registered in `SwebKitServiceCollectionExtensions.Agents.cs`. Two options:

- **(preferred) Adapter**: `AppServiceBusConnectionPool : IServiceBusConnectionPool`
  in `SwebKit.App` delegating to `MonitoringConnectionPool.GetServiceBusClient(ns.Alias)`
  (which already does credential-store + AuthMode resolution MAUI-side). `GetOrCreate`
  throws `InvalidOperationException` on null so the tool's error path stays uniform;
  `Evict`/`InvalidateAll` map onto `EvictServiceBusClient`.
- Alternative: keep `IServiceBusClientFactory` in the tools and replicate the
  AuthMode/transport branching — rejected: it re-opens exactly the drift this module
  exists to close.

## Module 2 — Ambient selection reaches tool execution

**Files:** `src-sidecar/Services/AgentToolCallOrchestrator.cs`,
`src-sidecar/Services/SidecarAgentChatService.cs`,
`src-sidecar/Services/Acp/SwebKitToolsMcpBridge.cs`, wherever
`SwebKitToolsMcpBridge.BuildUrl` is invoked (ACP session setup — verify call site in
`AcpAgentModelClient`/`AcpAgentHost`); new
`src/SwebKit.Agents/AgentExecutionContext.cs` (or `src-sidecar` if ACP-only scope is
chosen — see below).

Today `Selection` only reaches `AgentSystemPromptBuilder` — the model knows what's on
screen but tools can't. Add a process-wide ambient accessor:

```csharp
public static class AgentExecutionContext
{
    private static readonly AsyncLocal<AgentChatContext?> _current = new();
    public static AgentChatContext? Current { get => _current.Value; set => _current.Value = value; }
}
```

- `AgentToolCallOrchestrator.BuildStepTrackingToolExecutor` takes the turn's
  `AgentChatContext?` (it's already in scope in `BeginTurnAsync`) and sets
  `AgentExecutionContext.Current` around `_toolRegistry.ExecuteAsync`. AsyncLocal
  scopes it to the tool call's async flow — no leakage across turns or sessions.
- **ACP path**: `SwebKitToolsMcpBridge.CallToolAsync` runs on a different async flow
  (a stateless MCP HTTP request). The per-session bridge URL already carries the
  `?tools=` allowlist; extend it with `&sel=key:value` pairs built at session-creation
  time (where the turn's context is available), and set the same accessor around
  `ExecuteAsync`. Reject/strip any `sel` keys a tool could mistake for its own args —
  selection keys are namespaced (e.g. `sel.nsId`) so collision is impossible.
- Keep it a **default only**: an explicit tool argument always wins over ambient
  selection. Document this in `IAgentTool`/orchestrator doc comments — a model must
  never silently lose an explicit choice to page state.
- Other areas benefit for free: AKS tools can default `namespace`/`pod_name` from
  `Selection["namespace"]`/`Selection["pod"]` (what `AksPage.tsx` passes), Redis from
  `cache`/`key`, Storage from `account`/`container`. Only Service Bus is wired in this
  feature; per-area defaults for the rest are one-line additions listed as follow-ups.

Placement note: `AgentChatContext` lives in `src-sidecar`. If the accessor is needed by
`SwebKit.Agents` tools directly (it is — `ServiceBusToolContext.ResolveNamespace` takes
it), either move a minimal `AgentChatContext`-shaped type down or have the accessor
expose `IReadOnlyDictionary<string,string>? Selection` only. Prefer the latter: tools
need the selection dictionary, not the sidecar type.

## Module 3 — Log search with context lines

**Files:** `web/src/lib/log-window.ts` (+ `log-window.test.ts`),
`web/src/components/aks/shared/useLogWindow.ts`, `LogToolbar.tsx`, `LogOutput.tsx`,
`LogLineText.tsx` if a match-highlight prop is needed; `PodLogView.tsx` /
`MultiPodLogView.tsx` only if a prop threads through — goal is zero per-view work.

Current: `filterLogEntries` returns only matching lines. Add a second mode:

- `searchLogEntries(entries, term, contextLines)` → returns a list where every match
  is flagged (`kind: "match"`), each match is wrapped by up to `contextLines` of
  surrounding entries flagged `kind: "context"`, and non-contiguous groups are
  separated by a sentinel the renderer turns into a `── 12 lines ──` gutter row
  (`kind: "gap"`). Pure function → vitest.
- `useLogWindow` gains `mode: "filter" | "context"` + `contextLines` (default 3) and
  runs whichever pure function matches the mode. The frozen-anchor and heldBack logic
  is unchanged — it already operates on `filtered.length`; context mode just changes
  what `filtered` contains.
- `LogToolbar`: a Filter/Context segmented toggle next to the input (same control
  pattern as the timestamp select), a context-size select (2/5/10) shown only in
  context mode, and a match counter ("n matches").
- `LogOutput`: context lines render dimmed (`opacity-60`), the matched substring gets
  a `<mark>`-style highlight inside `LogLineText` (extend its tokenizer input with
  `highlightTerm` rather than bolting on a second highlighter), gap rows render as a
  centred separator, and `match` lines are navigation targets.
- Prev/next match: two small buttons (ChevronUp/ChevronDown reuse is confusing with
  Older/Newer — use distinct match-nav icons) that page the window so the next match
  enters view. Implement by finding match indexes in `filtered` and setting
  `pageFromNewest` accordingly — no new scroll machinery.

Both views get all of this for free through the shared components. Demo-mode e2e can
drive it: stubbed SSE bodies are already a Playwright fixture (see `aks-log-parity`
test-plan).

## Module 4 — Global activity indicator

**Files:** `AppLayout.tsx` (header), new `web/src/components/shared/ActivityIndicator.tsx`;
no backend.

A small, always-present header element bound to TanStack Query:

```tsx
const fetching = useIsFetching();
const mutating = useIsMutating();
```

- Shows a spinner + "Working…" (with `mutating > 0` taking precedence, e.g.
  "Saving…" while any mutation is pending). Invisible when idle — it must not become
  chrome noise.
- `data-testid="global-activity-indicator"`, `role="status"`, `aria-live="polite"`.
- Long-running streams (SSE log tails, port-forwards) don't flow through React Query —
  that's fine and out of scope; this indicator answers "did my click do anything",
  not "is a stream open".
- Note the boundary honestly in status.md: this complements, not replaces, the
  per-control pending states `ux-interaction-consistency` standardized.

## Module 5 — AKS YAML read + propose-apply tools

**Files:** new `src/SwebKit.Agents/Tools/GetAksResourceYamlTool.cs`,
`src/SwebKit.Agents/Tools/Aks/ProposeApplyAksYamlTool.cs`,
`src/SwebKit.Agents/Tools/Aks/AksActionExecutor.cs`;
`src/SwebKit.Agents/IAgentActionCoordinator.cs` (`AgentActionType.ApplyAksYaml`);
`src-sidecar/Program.cs` + `SwebKitServiceCollectionExtensions.Agents.cs` (register);
`web/src/components/agent/PendingActionCard.tsx` (`ApplyAksYaml: "AKS"` in
`FEATURE_AREA_BY_ACTION_TYPE`).

- `get_resource_yaml` (Read): `{ kind, name, namespace? }` →
  `IAksClient.GetResourceYamlAsync`. `namespace` defaults from ambient selection
  (Module 2), then `AksConfig.DefaultNamespace`, then `"default"`. Caps output (YAML
  for a big deployment can exceed the 8,000-char tool-result cap — truncate with a
  notice, matching `OpenAiCompatibleAgentClient`'s cap behavior rather than fighting
  it).
- `propose_apply_aks_yaml` (Mutate, `ToolRisk.High`): `{ kind, name, namespace, yaml }`.
  Before registering the pending action, run `client.ValidateResourceYamlAsync` and
  embed the outcome in the proposal: a validation failure returns an error result to
  the model _immediately_ (it can fix and re-propose) instead of producing a pending
  action that can only fail on apply. Preview = a compact diff or, where a diff is
  impractical, the first N lines + byte count; `Payload` carries the full arguments.
  New `AgentActionType.ApplyAksYaml`.
- `AksActionExecutor : IAgentActionExecutor` — `CanHandle(ApplyAksYaml)` →
  `client.ApplyResourceYamlAsync(ns, kind, name, yaml)`. Client resolution identical
  to the existing AKS tools (`IAksClientFactory` + `AppStateService.AksConfig` +
  `DemoAksClient` under `UseDemoData`). Demo mode must report honestly — if the demo
  client's apply is a stub, the executor surfaces "not supported in demo mode" rather
  than pretending success (same convention `ProposeExecuteSqlTool` follows for its
  guards).
- This composes with Module 2: a contextual AKS panel already sends
  `{ namespace, pod }`, so "change this deployment's log level" resolves without the
  model ever naming a namespace.

## Module 6 — Agent panel UX

**Files:** `web/src/components/agent/GlobalAgentPanel.tsx`, `AgentPage.tsx`,
`web/src/lib/hooks/useGlobalAgentConversation.ts`,
`web/src/lib/stores/agent-conversation.ts`, `AgentMarkdown.tsx`,
`ContextualAssistant.tsx`, `web/src/components/ui/ResizablePanel.tsx`.

### Ask & do on the global session

- `useAgentConversationStore` gains `mode`/`setMode` (default `"ask"`, same rule as
  contextual panels: a fresh conversation never inherits `ask_and_do`… actually it
  _can_ persist within the shared session — decide: persist per session, reset on
  Clear. Record whichever is chosen in decisions/status).
- `send` passes `mode` through to `chat.send` — the backend already accepts it
  (`AgentEndpoints` → `SidecarAgentChatService.BeginTurnAsync` → `NormalizeMode`);
  no endpoint changes.
- `GlobalAgentPanel` replaces the static "Ask only" badge with the same
  radio-button toggle `ContextualAssistant` renders; `AgentPage` gets it too — one
  conversation, one mode, shown identically in both containers.
- `PendingActionCard` already renders in the panel via `usePendingActionsFeed` —
  zero approval-plumbing work.

### Wider resize

- `ResizablePanel.maxWidth` is a fixed px. For the two agent panels, allow a
  viewport-relative ceiling: add an optional `maxWidthVw` (e.g. `60`) resolved against
  `window.innerWidth` at drag time (and on window resize), with `maxWidth` kept as a
  px floor. GlobalAgentPanel/ContextualAssistant then use `maxWidthVw={60}` instead of
  `maxWidth={600}`. Double-click-to-maximize keeps working — it already targets
  `maxWidth`.

### Overflow / horizontal scrollbar

- `AgentMarkdown`: add `break-words` on the wrapper and constrain block elements —
  `[&_pre]:max-w-full [&_pre]:overflow-x-auto [&_code]:break-all`,
  `[&_table]:block [&_table]:overflow-x-auto`, `[&_img]:max-w-full`. A wide code block
  scrolls _inside itself_, never the panel.
- Message bubbles: `min-w-0` on the flex child + `overflow-wrap:anywhere` so
  unbroken strings wrap; scroll containers get `overflow-x-hidden` (they're already
  `overflow-auto` → change to `overflow-y-auto overflow-x-hidden` where horizontal
  scrolling is never legitimate).
- Same fixes apply to `AgentThoughtBlock`/`AgentReasoningTrace` if they render raw
  JSON — check for `<pre>` children there too.

## Module 7 — AKS YAML viewer cleanup

**Files:** `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`
(`CleanEditableYaml`), new `web/src/lib/yaml-noise.ts` (+ test),
`web/src/components/aks/YamlViewer.tsx`; `tests/SwebKit.Kubernetes.Tests`.

### Blank lines (backend)

`KubernetesYaml.Serialize` → `CleanEditableYaml` → `yamlStream.Save` — YamlDotNet's
emitter inserts an empty line after every mapping entry whose value is a nested block,
which is why a Deployment reads double-spaced. Fix inside `CleanEditableYaml` after
`yamlStream.Save`: a line pass that drops empty lines **only outside literal/folded
block scalars**.

Block-scalar awareness is the correctness constraint — a genuinely empty line inside a
`|`/`>` block (ConfigMap `data` payloads, multi-line annotation values) is content, not
formatting. Single pass over emitted lines: when a line matches
`key: [|>][+-]?` record its indent as an open block scalar; subsequent lines stay "inside"
the block until a non-empty line indents less-or-equal; empty lines encountered outside
any open block scalar get dropped. The naive `\n\n→\n` collapse is rejected — it
corrupts literal blocks. Unit test with a ConfigMap whose `data` value contains blank
lines is the regression guard.

This fixes the view **and** the edit buffer, since both come from the same cleaned
string — compact output going into `SanitizeYamlForApply` stays valid (it re-parses,
never trusts formatting).

### Generated annotations (view-only)

`YamlViewer` gains a "Hide generated annotations" toggle, **default on**, powered by a
pure helper in `web/src/lib/yaml-noise.ts`:

```ts
filterGeneratedAnnotations(yaml: string): { yaml: string; hidden: number }
```

Line-wise, indent-tracked walk of `metadata.annotations`: drop entries whose key is on
the noise list — `kubectl.kubernetes.io/last-applied-configuration`,
`deployment.kubernetes.io/revision`, `meta.helm.sh/release-name`,
`meta.helm.sh/release-namespace` (the list is a named constant, easy to extend) — including
their (possibly literal-block) values. The toolbar shows "N generated annotations
hidden · show" when `hidden > 0`; hidden when zero.

Presentational only — three invariants:

- **Copy copies the full YAML**, not the filtered view.
- **Edit mode always loads the unfiltered manifest** — `last-applied-configuration`
  feeds kubectl's three-way merge; stripping it from the applied YAML changes apply
  semantics (fields the user removes would no longer be pruned).
- Server-side YAML is unchanged; `get_resource_yaml` (Module 5) and apply paths see the
  real manifest.

`HpaTab`'s `YamlViewer` usage gets this for free — same component.

## Files

| Area           | Files                                                                                                                                                                                                                                                                                                                                                                                 |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| New (backend)  | `Tools/ServiceBusToolContext.cs`, `Tools/GetAksResourceYamlTool.cs`, `Tools/Aks/ProposeApplyAksYamlTool.cs`, `Tools/Aks/AksActionExecutor.cs`, `AgentExecutionContext` (placement per Module 2 note), `SwebKit.App` `AppServiceBusConnectionPool` adapter                                                                                                                             |
| New (frontend) | `web/src/components/shared/ActivityIndicator.tsx`, `web/src/lib/yaml-noise.ts`                                                                                                                                                                                                                                                                                                        |
| Backend        | three Service Bus tools, `IAgentActionCoordinator.cs` (enum), `AgentToolCallOrchestrator.cs`, `SidecarAgentChatService.cs`, `SwebKitToolsMcpBridge.cs`, `src-sidecar/Program.cs`, `SwebKitServiceCollectionExtensions.Agents.cs`, `KubernetesAksClient.cs` (`CleanEditableYaml`)                                                                                                      |
| Frontend       | `log-window.ts`, `useLogWindow.ts`, `LogToolbar.tsx`, `LogOutput.tsx`, `LogLineText.tsx`, `GlobalAgentPanel.tsx`, `AgentPage.tsx`, `useGlobalAgentConversation.ts`, `agent-conversation.ts`, `AgentMarkdown.tsx`, `ContextualAssistant.tsx`, `ResizablePanel.tsx`, `PendingActionCard.tsx`, `AppLayout.tsx`, `YamlViewer.tsx`                                                         |
| Tests          | `ServiceBusToolsTests`, `ServiceBusToolContextTests` (new), `AksToolsTests`/`AksActionExecutorTests` (new), `AgentToolCallOrchestratorTests`, `SwebKitToolsMcpBridgeTests`, `CleanEditableYaml` blank-line tests (Kubernetes tests), `log-window.test.ts`, `yaml-noise.test.ts`, `pending-action-card.test.ts`, Playwright `contextual-assistant`/`global-agent-panel`/`aks-ux` specs |
| Docs           | this folder; `docs/features/README.md` catalog entry; `docs/pitfalls/agent-workflow.md` (new entry: agent tools must share the endpoints' connection pool, not rebuild clients); possibly `docs/architecture/functionalities/service-bus.md` + `aks.md` tool lists                                                                                                                    |

## Sequencing

1. Module 2 first (the ambient-selection seam) — Module 1's namespace defaulting and
   Module 5's namespace defaulting both consume it.
2. Module 1 (unblocks the reported failure; smallest backend change).
3. Module 5 (depends on 1's pattern for executor structure).
4. Modules 3, 4, 6 in parallel — disjoint files, all frontend.
5. Module 7 anywhere — disjoint from everything else (one backend helper + one viewer).
