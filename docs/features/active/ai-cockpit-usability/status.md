---
status: Planned
---

# AI Cockpit Usability — Status

- **Current phase:** Planned — not yet implemented.
- **Reported by:** Sebastien, 2026-09-16 — AI couldn't read the Service Bus error queue
  ("connection string not available", and it queried the prd namespace while dev was
  selected); wants ambient context in tools, AKS CRUD tools, log search-with-context,
  global action feedback, and agent panel fixes (Ask & do, resize, overflow).
  Second batch same day: AKS YAML viewer blank lines + generated-annotation noise
  (Module 7). The SQL items from the same batch went to `sql-database-explorer`
  Phase 4 — that's its area and it's still pre-PR.
- **Implementation PR:** not raised yet.

## Scope decisions (from the user, 2026-09-16)

- Namespace resolution when unspecified: **selection → explicit arg → error listing
  aliases**. Never `namespaces[0]` again.
- AKS mutate scope for v1: **apply YAML only** (`propose_apply_aks_yaml` +
  `get_resource_yaml` for read-modify-write). Restart/scale/delete are follow-ups.
- "Action feedback" = non-AI loading/processing feedback → global TanStack-Query-driven
  activity indicator (Module 4). Deliberately not a per-control audit — that's
  `ux-interaction-consistency`'s territory.
- Structure: one feature folder (this one), seven modules.
- YAML noise annotations are a **view-level** filter only — `last-applied-configuration`
  feeds kubectl's three-way merge, so Edit/Apply/Copy always see the full manifest.

## Modules (see technical-plan.md)

- [ ] Module 1 — Service Bus tools through `IServiceBusConnectionPool` +
      `ServiceBusToolContext` resolution; MAUI adapter `AppServiceBusConnectionPool`.
- [ ] Module 2 — Ambient `AgentExecutionContext` (AsyncLocal) set by the tool
      orchestrator and the ACP MCP bridge (selection in the bridge URL).
- [ ] Module 3 — Log context mode (match highlight, ±N lines, gap separators,
      match nav) in the shared `log-window`/`useLogWindow`/`LogToolbar`/`LogOutput`.
- [ ] Module 4 — Global activity indicator in `AppLayout` header
      (`useIsFetching`/`useIsMutating`).
- [ ] Module 5 — `get_resource_yaml` + `propose_apply_aks_yaml` +
      `AksActionExecutor` + `AgentActionType.ApplyAksYaml`.
- [ ] Module 6 — Global panel/page Ask & do toggle, viewport-relative max width,
      markdown/bubble overflow containment.
- [ ] Module 7 — `CleanEditableYaml` blank-line compaction (block-scalar-aware) +
      `YamlViewer` generated-annotations toggle (`yaml-noise.ts`).

## Open questions / things to verify during implementation

1. **`credentialKey` semantics in the sidecar.** `SidecarServiceBusConnectionPool`
   passes `ns.CredentialKey` straight to `factory.Create` as the connection string,
   while `ServiceBusSettings.tsx` tells the user it's "looked up in your OS credential
   store." One of those is wrong — find out which before writing
   `ServiceBusToolContext`'s error text, and fix the hint or the resolution as a
   documented follow-up if they disagree.
2. **Where `SwebKitToolsMcpBridge.BuildUrl` is called** — confirm the turn's
   `AgentChatContext` is in scope there (expected: ACP session setup in
   `AcpAgentModelClient`/`AcpAgentHost`) so `sel=` params can be baked in.
3. **Whether global-session mode persists across sends** in the shared conversation
   store or resets — pick one, record it here.

## Definition of done

See `index.md` "Outcomes". Non-negotiables beyond the listed behavior:

- Unit tests for every new pure function and tool (per the repo's
  test-what-you-move rule — the three rewritten Service Bus tools keep their existing
  test coverage shape, extended for the new resolution).
- Playwright coverage for the panel mode toggle, overflow fix, and log context mode.
- `docs/features/README.md` catalog entry added.
- New pitfall entry: agent tools must share the endpoints' connection pool rather
  than rebuild clients — `docs/pitfalls/agent-workflow.md`.
- Manual smoke against a real multi-namespace Service Bus config (see test-plan.md —
  the reported bug was environmental and unit tests alone can't prove it's gone).

## Validation

_To be filled during implementation — see test-plan.md for the matrix._
