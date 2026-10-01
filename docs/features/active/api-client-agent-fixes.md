# API Client agent fixes + cURL import

State: Review

## Goal

Three reported problems, one missing feature:

1. Confirming a proposed "Create request" action fails with
   `Collection 'Phone Notification' not found.` — the model has no way to learn
   collection IDs (`search_api_requests` returns only the collection _name_, and
   no `list_collections` tool exists), and `CreateRequestAsync` matches on `Id`
   only. Missing folders fail the same way.
2. Agent chat is badly formatted: `@tailwindcss/typography` is not installed, so
   the `prose` classes passed to `AgentMarkdown` are dead — `pre`/`code`/lists
   get no styling. The stale "~N tokens · X% of context window" header stat is
   still rendered on all three chat surfaces.
3. Support cURL import without AI: `ApiClientWorkflowService.ImportCurl` exists
   in Core with tests but is not registered in the sidecar, has no endpoint, and
   has no UI.

## Scope

- `ApiClientAgentService`: resolve create target by collection ID **or** name;
  auto-create the collection (local) and missing folder segments on confirm.
- New `list_api_collections` read tool; `search_api_requests` gains
  `collection_id`; create preview marks what "will be created".
- `POST /api/api-client/import-curl` (parse only) + `ApiClientWorkflowService` DI.
- `CurlImportDialog` from the collection-tree toolbar: paste command, pick target
  collection + folder, creates the node and opens it in a tab. Disabled in demo.
- Chat surfaces: drop the token estimate + `ContextUsageIndicator` (keep
  "N messages in history"); delete the orphaned component.
- `AgentMarkdown`: real styling for `pre`, inline `code`, lists, headings,
  blockquote, links, tables via Aurora tokens. User bubble moves from
  `bg-primary` to `bg-secondary` so a pasted dump is not a solid primary wall.

## Non-goals

- No new collection/folder proposal tools — confirmation is the gate, the
  preview names everything that will be created.
- `ExecuteHttpRequest` stays unimplemented (documented deferral).
- No changes to context budgeting itself — only the display is dropped.

## Decisions

- Auto-create on confirm rather than a separate collection-creation proposal:
  the user already approves each action, and the preview marks "(will be
  created)" per missing segment.
- cURL parse stays server-side (`ApiClientWorkflowService.ImportCurl` already
  exists + is unit tested) instead of a second TS tokenizer that would drift.

## Tasks

- [x] `IApiClientAgentService`: replace `GetCollectionsAsync` tuple with
      `ApiCollectionSummary` (id, name, origin, linkedRootId, folder paths,
      request count); `CreateRequestAsync` resolves id-or-name and auto-creates.
- [x] `ApiClientTools`: `ListApiCollectionsTool`; `collection_id` in search
      output; create preview marks "(will be created)" for collection/folders.
- [x] `Program.cs`: register `ListApiCollectionsTool` + `ApiClientWorkflowService`.
- [x] `ApiClientEndpoints`: `POST /api/api-client/import-curl`.
- [x] Web: `importCurl` in `lib/api/apiClient.ts`; `CurlImportDialog` + tree
      toolbar button; `handleImportCurlRequest` in `ApiClientPageContext`.
- [x] Web: strip tokens/context-% from `GlobalAgentPanel`, `ContextualAssistant`,
      `AgentPage`; delete `ContextUsageIndicator.tsx`.
- [x] Web: `AgentMarkdown` styling pass; user bubble to `bg-secondary`.
- [x] Tests: `ApiClientActionExecutorTests` fake update + new cases; sidecar
      import-curl endpoint test; Playwright spec for the dialog.

## Test plan

- `dotnet test tests/SwebKit.Agents.Tests` — create-by-name, auto-create
  collection + folder path, list tool output.
- `dotnet test tests/SwebKit.Core.Tests` — `ImportCurl` already covered.
- `dotnet test tests/SwebKit.Sidecar.Tests` — endpoint happy + failure paths.
- `cd web && npm run build && npx vitest run`.
- `cd web && npx playwright test e2e/api-client.spec.ts` (+ new curl-import case).
