# SwebKit Production-Readiness Review — 2026-08-06

## Executive Summary

The React + Tauri SwebKit app is **functionally solid and well-tested**, but it is not yet a
polished, production-ready product. The code base, build, unit tests, and full Playwright suite all
pass. Two concrete UX gaps called out by the user — Mermaid diagrams not rendering in the AI chat
and the AI chat panel being a fixed width — are now fixed. The remaining blockers are mostly
product/design decisions rather than broken code:

- App Insights still requires the user to paste a resource ID, even though authentication already
  works via `az login`.
- The dashboard is still a static launchpad, not the “AI cockpit” the user wants.
- Cross-feature deep-dive analysis has no visual maps, timelines, or diagrams.

## Validation Run

All checks passed on the current `main`-based branch:

- `npm run build` in `web/` — passes (new Mermaid chunk triggers the usual >500 kB warning).
- `npx vitest run` — 116/116.
- `dotnet build src-sidecar/SwebKit.Sidecar.csproj` — passes.
- `dotnet test tests/SwebKit.Sidecar.Tests/SwebKit.Sidecar.Tests.csproj` — 247/247.
- `npx playwright test` — 228/228.

## Concrete Fixes Applied in This Pass

1. **Mermaid diagram rendering in AI chat**
   - Added `mermaid@11.16.0` as a web dependency.
   - Created `web/src/components/agent/AgentMarkdown.tsx`, a shared markdown renderer that detects
     ` ```mermaid ` code blocks, renders them as SVG, and exposes a collapsible “Source” view so
     users see both the diagram and the code.
   - Wired it into `AgentPage.tsx`, `GlobalAgentPanel.tsx`, and `ContextualAssistant.tsx`.
   - Mermaid is loaded on-demand via a dynamic import so the initial bundle is not inflated until a
     diagram is actually rendered.

2. **Resizable AI chat panels**
   - Reused the existing `ResizablePanel` component for `GlobalAgentPanel.tsx` and
     `ContextualAssistant.tsx`.
   - Both panels now support drag-to-resize, double-click to reset, and `localStorage`-persisted
     width.

## App Insights / Agent Authentication

The user’s complaint is partially correct: the agent should not need a credential in the app
settings, and it already does not. The sidecar uses `DefaultAzureCredential` with environment and
managed-identity credentials excluded, so it will pick up an `az login` or Visual Studio credential
from the user’s environment. See `src/SwebKit.Core/Services/AzureCredentialFactory.cs`.

The only remaining setting is `ObservabilityConfig.SelectedResourceId`, which is required by the
agent tools `GetMetricsTool` and `QueryLogsTool`. The user has to paste this ID because
`AppInsightsDiscoveryService` exists but is **not registered in the sidecar** and has **no React
API/UI endpoint**. The fix is to:

1. Register `AppInsightsDiscoveryService` in `src-sidecar/Program.cs`.
2. Add an endpoint such as `GET /api/observability/resources`.
3. Replace the manual resource-ID text box in `web/src/components/settings/AgentSettings.tsx` with
   a dropdown that lists accessible App Insights components and auto-selects the first (or only)
   one.

That would make App Insights truly zero-config after `az login`.

## Dashboard / AI Cockpit Gap

`web/src/components/dashboard/DashboardPage.tsx` is currently a read-only grid of service health
and counts. It does not act as a central AI cockpit. Recommended cockpit ingredients:

- A pinned/favorite agent conversation panel.
- A live workspace topology graph using the existing `WorkspaceMap` data.
- A feed of proactive monitoring insights with one-click “Investigate”.
- A natural-language command bar that lets the agent drive navigation or generate diagrams.

## Deep-Dive Cross-Feature Visualization Gap

When the agent performs cross-feature analysis, it currently returns only text. There is no
built-in visualization for:

- Dependency/topology maps across AKS, Service Bus, Redis, Storage, and API Client.
- Time-line or trace diagrams for incident root-cause analysis.
- Architecture or sequence diagrams from agent output.

`Mermaid` is now available in the agent chat, so the agent can already emit flowcharts, sequence
diagrams, and timelines. The next step is to add a dedicated “Visualize” panel in the agent UI that
renders richer output — for example using the already-present `cytoscape` dependency for interactive
topology maps, or extending Mermaid with a timeline/quadrant renderer.

## Remaining Production-Readiness Items

| Area | Status | Notes |
|------|--------|-------|
| Build & tests | Green | All suites pass. |
| Agent chat Mermaid | Fixed | Diagram + source toggle in all three chat surfaces. |
| Resizable agent panels | Fixed | Reuses `ResizablePanel`. |
| App Insights zero-config | Not done | Discovery service exists but is unwired. |
| AI cockpit dashboard | Not done | Static dashboard; needs redesign. |
| Cross-feature visual debugging | Not done | Agent output is text-only. |
| Security scan | Skipped | Aikido MCP server is not installed in this environment. |
| MAUI legacy app | In repo | `src/SwebKit.App` is still present; harmless but increases maintenance. |
| Bundle size | Watch | Mermaid adds a ~620 kB `mermaid.core` chunk and many per-diagram chunks. Acceptable for now, but consider a custom manual chunk strategy if more diagram libraries are added. |

## Security Note

The `CLAUDE.md` security rule requires an Aikido MCP scan on new/modified first-party code. The
Aikido MCP server is not available in this Devin environment, so that scan could not be run. The
Mermaid renderer intentionally sets `securityLevel: "strict"` and does not execute arbitrary
JavaScript; the SVG is rendered by Mermaid itself and inserted via `dangerouslySetInnerHTML` only
after Mermaid’s own DOMPurify pass in strict mode.

## Recommended Next Steps

1. Decide whether to implement App Insights auto-discovery now or as a follow-up.
2. Design the AI cockpit dashboard: what widgets, what data sources, and whether it replaces or
   augments the current dashboard.
3. Design the deep-dive visualization experience: which diagram types the agent should emit, where
   the visual panel lives, and how it receives data from the agent tools.
4. Re-run the Aikido security scan once the MCP server is available.
