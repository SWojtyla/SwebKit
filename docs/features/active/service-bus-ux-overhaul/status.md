# Status — Service Bus UX Overhaul

State: `Review`

## Workstream checklist

### A — Batch resend (fresh MessageIds, copy semantics)

- [x] `cloneForResend` helper: fresh `crypto.randomUUID()` per message, strips
      DLQ app-prop keys, clears broker-owned fields (`sequenceNumber`,
      `lockToken`, `deadLetterReason`, `deliveryCount`)
- [x] "Resend" bulk action in `MessageList` bulk bar — visible in Active and DLQ
- [x] ConfirmBar confirmation stating copies are sent and originals kept
- [x] Resend via `useSbResendMessages` → `POST …/batch-send`; invalidate via
      `invalidateServiceBusQueries` (peek + stats, no topology)
- [x] Subscription entities resolve send target to parent `topicName`
      (`sendableEntityPath`)
- [x] Success/error notifications through `useNotification()`

### B — Composer → resizable side panel

- [x] `MessageComposer` renders inside `SidePanel`/`ResizablePanel` (push on
      wide, overlay on narrow), `storageKey="service-bus-composer"`
- [x] MessageId field: visible, editable, regenerate button; replay/edit modes
      default to a fresh GUID (`composer-message-id`, `composer-regenerate-id`,
      `composer-restore-id`)
- [x] CodeMirror body editor (`MessageBodyEditor`) with `swebkitHighlighting()`
      + JSON/XML language sniffing, hidden mirror textarea (`composer-body`),
      Format JSON kept
- [x] Field layout rework (no clipped controls at min panel width)
- [x] "Save as template" action inside the composer (`composer-save-template`)
- [x] Quick-apply template entry still reachable (`composer-load-template` →
      pick-mode `TemplateManager`)

### C — Templates manager

- [x] Dedicated manager surface reachable from the page toolbar
      (`sb-templates-button` → `template-manager` dialog)
- [x] List + search (`template-search`, `template-item-*`)
- [x] Create blank, edit (name/body/subject/correlationId/contentType/
      properties), rename, duplicate, delete with confirmation
      (`template-new`, `template-save`, `template-duplicate-*`,
      `template-delete-*` + `template-delete-confirm`)
- [x] "Use in composer" applies a template into the composer panel
      (`template-use-in-composer`)
- [x] All mutations via existing `POST`/`DELETE /api/servicebus/templates`
      (upsert by id) with notify + `sb-templates` invalidation
- [x] `TemplatePicker` kept as thin pick-mode wrapper (existing testids intact)

### D — Overview & toolbar

- [x] Toolbar regrouped: Compose primary + Templates + Search Entities;
      Batch Send / Scheduled / Batch Replay grouped under `sb-actions-menu`
- [x] No-entity overview pane (`NamespaceOverview`): stat cards + entities with
      DLQ backlog as jump targets (`sb-overview-dlq-*`)
- [x] Empty/loading/error states (`sb-overview-empty/-loading/-error`,
      `template-picker-empty`, `template-editor-error`); `data-testid` on every
      new control

## Validation

- [x] `cd web && npx tsc -b` — clean
- [x] `cd web && npx eslint` (service-bus scope) — 0 errors, warnings only
      (pre-existing patterns; `onChangeRef` render-assign matches BodyCodeEditor)
- [x] `cd web && npm run build` — clean
- [x] `cd web && npx vitest run resendHelpers.test.ts` — 9/9 pass
- [x] `cd src-sidecar && dotnet build` — clean (0 warnings/errors)
- [x] `cd tests/SwebKit.Sidecar.Tests && dotnet test` — 495/495 pass
      (no endpoint changes, suite confirms no regression)
- [x] `cd web && npx playwright test e2e/service-bus*.spec.ts` — 34/34 pass,
      including 5 new tests: bulk resend (active + DLQ), fresh-GUID replay,
      templates manager CRUD, namespace overview
- [x] Full `npx playwright test` sweep — 357/357 pass
- [ ] Manual Tauri-window check for any drag interactions (Playwright can't
      catch `dragDropEnabled` issues)
- [ ] Aikido MCP scan — server not configured in this session; needs a run
      per `docs/security/aikido-mcp-scan.md`
