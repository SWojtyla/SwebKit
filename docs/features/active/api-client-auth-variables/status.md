---
status: Review
---

# API Client Auth: Variable Substitution & Secret Visibility — Status

- **Current phase:** Review — implemented and validated, awaiting sign-off.
- **Reported by:** Sebastien, from daily use of the Tauri build against the Portima
  authentication API: a `POST` returning `400 Bad Request` whose cURL panel, with secrets
  revealed, showed `Authorization: Bearer {{AUTH_PI2_KEY}}` — the token unresolved while
  every other line of the same command was substituted. Reported alongside the complaint
  that an auth value cannot be read back once it has been set.
- **Implementation PR:** not raised yet.

## Validation

| Gate | Result |
| --- | --- |
| `npx tsc -b` | clean |
| `npx eslint` (changed files) | no new findings; the 7 pre-existing `RequestEditor.tsx` problems reproduce with the change stashed |
| `npm run test:unit` | 408 passed (37 files) |
| `dotnet test tests/SwebKit.Core.Tests` | 949 passed |
| `dotnet test tests/SwebKit.Sidecar.Tests` | 339 passed |
| `npx playwright test e2e/api-client.spec.ts -g "auth secret can be revealed"` | 1 passed |

`dotnet build src/SwebKit.App` fails at `SigningCertificateThumbprintNotInStore`, an MSIX
packaging step unrelated to this change — the C# compile of the edited `AuthHeaderBuilder`
succeeds before it.

## Definition of Done

- [x] Every auth field resolves `{{variables}}` at send time, against the same scope as the
      rest of the request, for bearer, API key, Basic and OAuth2 alike.
- [x] An undefined variable in an auth field is left literal and named in the existing
      unresolved-variable banner.
- [x] Auth secrets can be revealed and hidden; revealed, they show variable resolution state.
- [x] The cURL panel substitutes the non-secret auth fields; the secret stays masked until
      the panel's own reveal.
- [x] Unit tests for every new pure function, sidecar tests per auth type, e2e for the
      reveal toggle.
- [x] Feature docs written.
- [ ] Manual verification in the running Tauri app (see `test-plan.md`) — **owner:
      Sebastien**, since it needs the real `DEV (via APIM)` environment and Portima
      credentials.
- [ ] Aikido security scan per `docs/security/aikido-mcp-scan.md` — the MCP server timed out
      on connect during this session, so the scan has not run.

## Follow-ups (not in this feature)

1. `IOAuth2TokenManager` in the legacy Blazor app takes a credential *key* and reads the
   client secret itself, so that one path still cannot see the scope. The sidecar, which the
   shipping app uses, has no such gap.
2. `ApiClientWorkflowService.BuildCurlAsync` builds cURL server-side on its own code path and
   was not touched here; it has the same auth-rendering question the frontend panel had.
