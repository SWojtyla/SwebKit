---
name: pre-ship-review
description: 'Structured pre-commit quality gate. Runs before shipping to Azure DevOps. Checks DoD conditions, architecture compliance, security patterns, docs alignment, and commit hygiene. Produces a go/no-go report. Use when: review before push, pre-ship check, quality gate, review my changes, is this ready to ship, DoD check.'
---

# Pre-Ship Review

A structured quality gate that runs **after self-assessment and before shipping to Azure DevOps**. It does not replace automated tests — it catches the things tests miss: docs drift, architecture divergence, security anti-patterns, forgotten follow-up debt, and commit hygiene.

Checks **six areas**: Definition of Done, architecture compliance, security, docs alignment, and commit hygiene.

Produces a concise **go / conditional-go / no-go** report. The agent continues shipping only on go or conditional-go with user acknowledgement.

---

## Input

One of:

- The current working branch (auto-detected via `git branch --show-current`)
- A specific feature name (maps to `docs/features/active/<feature-name>/`)

---

## Procedure

### Step 1 — Gather changed files

Detect the default remote branch:

```powershell
git remote show origin | Select-String 'HEAD branch' | ForEach-Object { ($_ -split ':')[1].Trim() }
```

Run:

```powershell
git diff --name-only origin/<DEFAULT_BRANCH>...HEAD
```

If the remote is unreachable, fall back to:

```powershell
git diff --name-only HEAD~1...HEAD
```

Categorise the changed files into buckets:

- `src/` — application source
- `tests/` — test projects
- `devops/` — pipelines, charts, variables
- `docs/` — documentation
- `docs/features/active/` — feature docs
- `docs/architecture/` — architecture docs
- config files (`.yml`, `.json`, `.csproj`, `.sln` at root)

This categorisation drives which checks run below.

---

### Step 2 — Definition of Done check

Read `ai-setup/ways-of-working/definition-of-done.md` and evaluate each condition against the actual state of the feature folder and changed files.

| Condition                             | How to verify                                                                      |
| ------------------------------------- | ---------------------------------------------------------------------------------- |
| Requested behaviour is implemented    | `status.md` progress checklist is fully checked                                    |
| Code follows architecture constraints | Cross-reference changed `src/` paths against `docs/architecture/codebase-guide.md` |
| Relevant tests are added or addressed | Changed `src/` files have corresponding entries in `tests/` or `test-plan.md`      |
| Existing tests still pass             | Build + test run was completed in Phase 5 of swebify (check `status.md`)           |
| Related documentation updated         | If `src/` changed, corresponding `docs/` entries must exist                        |
| Technical decisions recorded          | Non-obvious tradeoffs must appear in `decisions.md`                                |
| No hidden blockers                    | `status.md` must not contain items marked as blocked or TODO                       |
| Status accurately reflects reality    | `status.md` state must be `Done`                                                   |

Flag each condition as ✓ pass, ⚠ warn (minor gap, not blocking), or ✗ fail (blocker).

---

### Step 3 — Architecture compliance check

1. **Functionality docs** — if any changed file path matches a supported functionality, verify the matching doc was also updated:

   | Changed path contains                              | Required doc update                                  |
   | -------------------------------------------------- | ---------------------------------------------------- |
   | `ServiceBus`, `NServiceBus`, `MessageBus`          | `docs/architecture/functionalities/service-bus.md`   |
   | `ObservabilityService`, `Telemetry`, `AppInsights` | `docs/architecture/functionalities/observability.md` |
   | `AksClient`, `kubernetes`, `helm`, `chart`         | `docs/architecture/functionalities/aks.md`           |
   | `RedisCache`, `StackExchange.Redis`                | `docs/architecture/functionalities/redis.md`         |
   | `ReleaseService`, `deployment`, `release`          | `docs/architecture/functionalities/releases.md`      |
   | `SettingsService`, `IOptions`, `appsettings`       | `docs/architecture/functionalities/settings.md`      |

2. **No silent divergence** — if `design.md` or `architecture.md` describe a pattern that the changed code visibly contradicts (e.g., a new direct DB call bypassing the repository layer), flag it as ✗ fail.

3. **Codebase guide** — if new entry points, folders, or naming conventions were introduced, verify `docs/architecture/codebase-guide.md` was updated.

---

### Step 4 — Security scan

Scan the changed `src/` files for known OWASP patterns. Flag any hit as ✗ fail.

| Pattern to search                                                                                                                 | Risk                                                                          |
| --------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------- | ------ | --------------------------------------- | ------------------- |
| Hardcoded connection strings, passwords, API keys (regex: `(?i)(password                                                          | secret                                                                        | apikey | connectionstring)\s*=\s*["'][^"']{4,}`) | Credential exposure |
| `SqlCommand` with string concatenation (`"SELECT" + ` or `$"SELECT`)                                                              | SQL injection                                                                 |
| `Html.Raw(` or `@Html.Raw(` with a non-literal argument                                                                           | XSS                                                                           |
| `Process.Start(` with user-controlled input                                                                                       | Command injection                                                             |
| `new HttpClient(` without a factory or `IHttpClientFactory`                                                                       | Unstable HTTP client lifecycle (not a security issue, but a reliability flag) |
| Disabled SSL/TLS validation (`ServerCertificateCustomValidationCallback.*true` or `DangerousAcceptAnyServerCertificateValidator`) | MITM exposure                                                                 |
| `[AllowAnonymous]` on a new controller or endpoint                                                                                | Unintended anonymous access                                                   |
| Secrets in config files committed to the repo (`appsettings.*.json` with non-placeholder values for sensitive keys)               | Secret leakage                                                                |

Also check:

- `.gitignore` covers `bin/`, `obj/`, `*.user`, `*.suo`, and any new artefact folders introduced.
- No `TODO: remove before PR` or `HACK:` comments remain in changed files.

---

### Step 5 — Docs alignment check

1. **Feature docs** — read `docs/features/active/<feature-name>/index.md` and `status.md`. Compare the stated scope against the actual changed file list.
   - If the changed files include something not mentioned in the feature scope, flag it as ⚠ warn (scope creep) or ✗ fail (significant silent expansion).
   - If the feature scope claims something that has no corresponding changed file, flag it as ✗ fail (claimed but not delivered).

2. **Stale references** — scan feature docs for TODO, FIXME, or `[TBD]` markers that were not resolved. Flag each as ⚠ warn.

3. **Test plan** — verify `test-plan.md` acceptance criteria:
   - Every AC that is **checked** counts as ✓ pass.
   - Every AC that is explicitly deferred **with a stated reason and owner** counts as ⚠ warn.
   - Every AC that is unchecked, marked `[TBD]`, or deferred with no reason counts as ✗ fail.

   A bare "deferred" with no explanation is not acceptable at ship time — flag it as ✗ fail.

---

### Step 6 — Commit hygiene check

Run:

```powershell
git log origin/<DEFAULT_BRANCH>..HEAD --oneline
```

Verify:

- Each commit message follows Conventional Commits format (`<type>(<scope>): <subject>`).
- No commit subject exceeds 72 characters.
- No "WIP", "fixup", "temp", "debug", or "test commit" messages exist (these should be squashed before shipping).
- If there are no commits ahead of `origin/<DEFAULT_BRANCH>`, flag as ⚠ warn (not ✗ fail):
   - "No commits ahead of default branch yet — continue implementation/shipping flow, then re-run commit hygiene after the first commit."

Commit hygiene findings are warnings by default:

- Non-conventional format, long subjects, and squashable commit messages are ⚠ warn.
- "No commits yet" is ⚠ warn.
- Do not produce NO-GO from commit hygiene alone unless explicitly marked security-critical (none in this policy).

If squashable commits are found, recommend the user run:

```powershell
git rebase -i origin/<DEFAULT_BRANCH>
```

before proceeding to ship. Flag as ⚠ warn (not a hard blocker).

If no commits exist yet, continue with the review result as **CONDITIONAL GO** and add an explicit instruction to re-check Step 6 immediately after the first commit.

---

### Step 7 — Produce the report

Output the review report in this format:

```
## Pre-Ship Review — <feature-name>

### Result: GO / CONDITIONAL GO / NO-GO

### Definition of Done
✓ / ⚠ / ✗  <condition>
...

### Architecture Compliance
✓ / ⚠ / ✗  <check>
...

### Security
✓ / ⚠ / ✗  <check>
...

### Docs Alignment
✓ / ⚠ / ✗  <check>
...

### Commit Hygiene
✓ / ⚠ / ✗  <check>
...

### Blockers  (only if NO-GO or CONDITIONAL GO)
1. <specific issue — file or doc — action required>
...

### Warnings  (only if any ⚠)
- <issue — file — recommended action>
...
```

**Result logic:**

| Condition                    | Result                                                                         |
| ---------------------------- | ------------------------------------------------------------------------------ |
| All checks ✓                 | **GO** — proceed to ship automatically                                         |
| Only ⚠ warn items, no ✗ fail | **CONDITIONAL GO** — present warnings, ask user to acknowledge before shipping |
| Any ✗ fail                   | **NO-GO** — list blockers, stop, do not ship                                   |

---

### Step 8 — Act on the result

- **GO** — proceed directly to the `azure-devops` skill without interruption.
- **CONDITIONAL GO** — present the report, list warnings explicitly, and ask:
  > "There are warnings but no blockers. Do you want to proceed with shipping, or fix these first?"
  > Wait for user response.
- **NO-GO** — present the report, list each blocker with the exact file/doc and what needs to change. Do NOT invoke the `azure-devops` skill. Hand back to the user.

---

## Standalone use

When invoked outside of `swebify`, auto-detect the feature name from the active feature folder that matches the current branch name. Strip any user prefix from the branch name — e.g. `sw/dev/feature-name` → `feature-name` — and look for `docs/features/active/<feature-name>/`.

---

## Guardrails

- Do NOT modify any source files during review — this skill is read-only.
- Do NOT suppress or omit ✗ fail items to achieve a GO result.
- Do NOT interpret a passing build/test run as sufficient — review is broader than compilation.
- Flag security issues as ✗ fail regardless of scope. Never downgrade a security issue to a warning.
