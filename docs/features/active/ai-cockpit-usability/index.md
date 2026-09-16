# AI Cockpit Usability

## Status

`Planned` — created 2026-09-16 from a batch of user-reported friction with the embedded AI
and the surrounding UI. One folder, seven modules; each is independently shippable but they
share the same conversation surfaces and tool plumbing, so they land together.

## What this is

Five reported problems, one theme: the AI assistant looks wired in but isn't — its tools
don't reach the resources the user is actually looking at, it can't act on AKS at all, and
the panel it lives in fights the user. Plus two general UX gaps noticed alongside it.

### 1. Service Bus agent tools can't reach any real namespace

Reported symptom: asked about error-queue messages from the Service Bus page (dev
namespace selected), the agent answered "connection string not available" for _both_
configured namespaces — and tried `namespaces[0]` (prd) rather than the dev namespace the
user was looking at.

Root causes, verified against the code (2026-09-16):

- `GetQueueStatsTool`, `GetQueueMessagesTool`, `AnalyzeQueueHealthTool` resolve a client
  via `_credentialStore.Get(ns.CredentialKey)` + `_sbFactory.Create(connectionString)` —
  a MAUI-era path that bypasses `IServiceBusConnectionPool`, the pool every working
  Service Bus endpoint uses. The pool handles `SbAuthMode` (`CreateWithEntra` for
  Entra/managed-identity namespaces), `SbTransportType` (AmqpWebSockets), and client
  reuse; the tools honor none of it. An Entra namespace has no connection string at all
  → guaranteed "not available".
- Namespace targeting: `GetQueueStatsTool`/`GetQueueMessagesTool` have **no** namespace
  parameter and use `namespaces[0]` unconditionally; `AnalyzeQueueHealthTool` accepts
  `namespace_alias` but defaults to `[0]`. The page's selection (`nsId`) reaches the
  system prompt only — no tool can see it.
- They also build-and-dispose a client per call — the exact leak the pools were built
  to fix (`data-fetch-performance`).

### 2. Selection never reaches the tools

`AgentChatContext.Selection` (e.g. `{ nsId, entityPath }` from ServiceBusPage,
`{ namespace, pod }` from AksPage) is injected into the system prompt by
`AgentSystemPromptBuilder` — the model _reads about_ the selection but every tool call
still starts from globals. This is the general mechanism behind "went to prd while I
was on dev" and applies to every area, not just Service Bus.

### 3. No way to act on AKS

Every other area has propose→confirm mutation tools; AKS has read-only tools only. The
user's concrete scenario: read a pod's logs, then update the deployment YAML to change
its log level. `IAksClient` already exposes `GetResourceYamlAsync`,
`ApplyResourceYamlAsync`, `ValidateResourceYamlAsync` — the capability exists end to
end, just not as agent tools.

### 4. Log search ≠ filter

`filterLogEntries` (`web/src/lib/log-window.ts`) drops every non-matching line — the
colleague's ask: search for a term and see the lines _around_ the hit (grep `-C`
style), not only the hits. Both log views share `useLogWindow`/`LogToolbar`/`LogOutput`,
so this is one implementation, not two.

### 5. Agent panel UX

- `GlobalAgentPanel` is deliberately "Ask only" — no Ask & do toggle, and
  `useGlobalAgentConversation.send` never passes `mode`, so the global session can
  never propose actions even though the backend accepts `mode`.
- `ResizablePanel` `maxWidth={600}` caps both agent panels — users want it much wider.
- Long/unbroken assistant content (URLs, code blocks, wide tables) overflows the
  bubble and produces a horizontal scrollbar — `AgentMarkdown` and the message
  containers have no overflow containment.

### 6. Global "something is happening" feedback

Not AI-specific: loading and mutations sometimes feel stuck while still processing.
Scope kept small: a header-level activity indicator driven by TanStack Query's
in-flight state, so any pending query/mutation is visibly signalled app-wide.

### 7. AKS YAML viewer is noisy

Reported 2026-09-16 alongside the SQL items: the manifest view wastes most of its
height. Two causes, both verified against the code:

- **Blank lines after every block-valued key.** `KubernetesYaml.Serialize` output goes
  through `CleanEditableYaml`, which re-emits via YamlDotNet — and YamlDotNet's emitter
  inserts an empty line after each mapping entry whose value is a nested block. The
  result is a Deployment that reads double-spaced.
- **Generated annotations dominate the view.** `kubectl.kubernetes.io/
last-applied-configuration` embeds the entire spec as a JSON blob;
  `deployment.kubernetes.io/revision` and `meta.helm.sh/release-name`/`release-namespace`
  duplicate what's already shown elsewhere. These must remain presentational-only —
  `last-applied-configuration` feeds kubectl's three-way merge on apply, so stripping
  it from the editable/applied YAML would silently change apply semantics.

## Outcomes / definition of done

- Service Bus tools resolve the namespace the user has selected (or an explicit
  `namespace` arg), connect through `IServiceBusConnectionPool` exactly like the page
  does (Entra, connection string, websockets transport), and — when the target is
  ambiguous — fail with an actionable error listing the configured aliases instead of
  silently hitting `namespaces[0]`.
- The same ambient-selection mechanism also lets AKS tools default `namespace`/`pod`
  and other areas default their resource, from the page the panel was opened on.
- The agent can `get_resource_yaml`, then `propose_apply_aks_yaml` — a confirmed,
  high-risk proposal executed through a new `AksActionExecutor`.
- Both log views gain a context mode: matches highlighted, ±N surrounding lines shown,
  match count + prev/next navigation.
- Global agent panel (and `/agent` page) get the Ask / Ask & do toggle, a much larger
  resize ceiling, and assistant content can never produce a horizontal scrollbar.
- A global activity indicator shows whenever requests/mutations are in flight.
- The AKS YAML view drops the emitter blank lines and hides generated annotations by
  default (toggleable), while Copy and Edit/Apply always operate on the full manifest.

## Non-goals

- **No broad AKS CRUD** — v1 is apply-YAML only (covers deployment env/log-level edits
  and any other manifest change). Restart/scale/delete-pod tools are a deliberate
  follow-up, not silently dropped.
- **No rework of `CredentialKey` semantics.** The tools stop caring how a namespace
  authenticates by going through the pool. The pre-existing question of whether the
  Settings hint text ("looked up in your OS credential store") matches what the sidecar
  actually does with `credentialKey` is recorded as a follow-up, not fixed here.
- **No per-control busy-state audit** — `ux-interaction-consistency` (Review) already
  standardized notify-on-mutation and loading/error/empty states; this feature adds the
  _global_ signal only.
- **No changes to the area/scope tool gates** — `agent-correlation` (Planned) owns
  fence transparency and escalation UX.

## Dependencies / prior art

- `SidecarServiceBusConnectionPool` (`src-sidecar/Services/`) — the correct client path.
- `RedisToolContext` / `SqlToolContext` (`src/SwebKit.Agents/Tools/`) — the shared
  per-area resolution-helper pattern `ServiceBusToolContext` should mirror.
- `ProposeSetRedisKeyTtlTool` + `RedisActionExecutor` + `AgentActionApplier` — the
  propose/confirm/apply pattern `propose_apply_aks_yaml` copies.
- `aks-log-parity` (Review) — owns `log-window.ts`/`useLogWindow`/`LogToolbar`/
  `LogOutput`; this feature extends that shared code rather than working around it.
- `usePendingActionsFeed`/`PendingActionCard` — already render in the global panel, so
  Ask & do there needs no new approval plumbing.
- MAUI parity: the same tools are registered in
  `SwebKitServiceCollectionExtensions.Agents.cs` — constructor changes must resolve in
  both hosts (see technical-plan.md Module 1).

## Risks

- **Selection trust.** Ambient selection must remain a _default_, never an override — an
  explicit tool argument always wins, and `nsId` from a page's selection must be
  validated against configured namespaces before use.
- **ACP bridge.** The MCP bridge is stateless per request; ambient selection has to
  travel in the per-session bridge URL — must not leak selection across sessions.
- **MAUI DI.** `IServiceBusConnectionPool` has no `SwebKit.App` implementation today;
  switching the tools to it requires a small adapter or a different resolution seam
  (decision in technical-plan.md Module 1).

## Traceability

- Technical plan: `technical-plan.md`
- Test plan: `test-plan.md`
- Status: `status.md`
- Related: `../aks-log-parity/`, `../ux-interaction-consistency/`, `../agent-correlation/`,
  `../ai-augmented-app/` (Ask & do flow), `../workspace-intelligence/` (scope gate)
