# GitHub Copilot Instructions

You are working in a repository that follows a docs-first AI workflow.
Your goal is not only to write code, but to preserve clean project state, keep documentation aligned with implementation, and make progress traceable.

## Core working model

This repository separates:

- stable project guidance,
- active feature execution,
- historical archived work,
- recurring pitfalls and learned patterns.

Always prefer this workflow over inventing a new one for the current task.

## Authoritative documents

Before making significant changes, consult these sources in this order when they exist:

1. `ai-setup/ways-of-working/ai-workflow.md`
2. `ai-setup/ways-of-working/definition-of-done.md`
3. `docs/architecture/architecture.md` — system-wide component map (what connects to what)
4. `docs/architecture/design.md` — component-level flows (how internals work)
5. `docs/architecture/codebase-guide.md` — implementation navigation (where to touch the code)
6. `docs/pitfalls/`

If the task is about a specific feature, then also read:

- `docs/features/active/<feature-name>/index.md`
- `docs/features/active/<feature-name>/status.md`
- other files in that feature folder only if relevant

Do not scan unrelated feature folders unless explicitly asked.

## Feature execution rules

When working on a feature:

- treat the feature folder as the source of truth for scope and progress
- keep implementation aligned with the documented plan
- update `status.md` as work progresses
- record important technical decisions in `decisions.md`
- update test notes or test plan when behavior changes
- prefer editing existing feature docs over creating scattered new markdown files

If a feature folder does not exist and the task is substantial, propose or create one before large implementation work begins.

## Jira integration

Features should be linked to Jira tickets for team-level visibility and tracking.

When creating a new feature:

- the user provides the Jira ticket key, or the agent searches Jira to find one
- add the Jira ticket link to the feature `index.md` under Quick links
- do NOT create Jira tickets automatically — only create one if the user explicitly asks
- if no ticket is found or provided, note it in `status.md` and continue

Jira lifecycle ownership — each skill owns a specific transition:

- `swebify` — transitions to `In Progress` (Phase 3), sets components (Phase 4), transitions to `Review` and adds PR link (Phase 8)
- `atlassian-integration` — ad-hoc status updates, comments, and lookups outside the swebify flow
- `feature-archive` — transitions to `Done` and adds closing comment after merge

Do NOT transition Jira tickets outside these skills. Direct ad-hoc transitions cause status drift.

Jira complements the docs-first feature model — it does not replace it. The feature folder remains the source of truth for scope, plan, and technical decisions.

## Status discipline

Each active feature should maintain a small `status.md` file.
Use it to track:

- current state
- current focus
- completed work
- remaining work
- blockers
- validation status

Do not mark a feature as done unless implementation, tests, and related documentation are aligned.

## Pitfalls discipline

Before making non-trivial changes, check relevant files in `docs/pitfalls/`.
If you notice a repeated failure mode, risky assumption, or recurring code-generation mistake, add or update a concise pitfalls entry.

Pitfalls should be:

- short
- actionable
- specific
- based on real mistakes or repeated review findings

## Architecture discipline

Treat `architecture.md`, `design.md`, and `codebase-guide.md` as constraints, not background reading.

- `architecture.md` — system-wide map. Stable, rarely changes.
- `design.md` — component blueprints. Changes when internals are refactored.
- `codebase-guide.md` — navigation index. Update when folders, entry points, or naming conventions change.

If implementation needs to diverge from documented architecture or design:

- do not silently drift
- update the relevant decision record or feature decision note
- explain the reason for the change

When implementation changes behavior for an app functionality (Projects, Service Bus,
Observability, AKS, Redis, Settings), also update the corresponding file under
`docs/architecture/functionalities/` in the same change set.

## Archive discipline

Active work belongs under `docs/features/active/`.
Completed work should not remain mixed with active work forever.

When a feature is complete, the close-out path depends on whether a Jira ticket is linked:

- **Jira ticket linked:** delete the active feature folder — Jira is the durable record, no archive needed. Add a concise closing comment to the ticket (outcomes only, 5–8 lines max).
- **No Jira ticket:** prepare a concise archive-ready summary, preserve reusable decisions and lessons, move the folder to `docs/features/archive/`.

In both cases: avoid keeping large execution checklists in the active area.

Do not read archived feature folders by default.
Use archived features only when explicitly asked for history, precedent, or reusable implementation patterns.

## Change style

When making changes:

- prefer small, coherent edits
- avoid unnecessary file proliferation
- keep naming predictable
- preserve existing conventions unless there is a clear reason to improve them
- explain tradeoffs briefly when making non-obvious structural decisions

## Validation expectations

Before considering work complete:

- verify the implementation against the feature plan
- verify tests or test coverage expectations
- verify related docs are updated
- note any assumptions, gaps, or follow-up items clearly

## Communication style

When responding:

- be explicit about what was changed
- mention which feature docs were updated
- mention blockers or uncertainties
- do not claim completion if validation is incomplete
- suggest the next smallest useful step when work cannot be fully completed

## Available skills

The following skills automate multi-step workflows. Invoke them by name when the trigger matches:

- `project-context` — load architecture, codebase-guide, and pitfalls before any non-trivial task
- `swebistart` — generate `architecture.md`, `design.md`, and `codebase-guide.md` for a new project
- `swebify` — end-to-end feature delivery from a Jira ticket
- `swebiplan` — create a fully populated feature plan from a description (Jira optional); does NOT implement
- `feature-archive` — archive a completed feature after the PR is merged
- `atlassian-integration` — Jira/Confluence integration (status sync, comments, ticket lookups)
- `pre-ship-review` — quality gate before push: DoD, architecture compliance, security scan, docs alignment, commit hygiene
- `azure-devops` — commit, push, open a Pull Request, and trigger the CI pipeline
- `swebifix` — read open PR threads (SonarQube, reviewer comments), fix reported issues, commit, push, and resolve threads

## Delivery paths

Two paths exist. Use the one that fits your workflow:

**Jira-driven (autonomous):** `swebify` — takes a ticket key and delivers the full feature end-to-end.

**General (manual control):** `swebiplan` → implement via orchestrator → `pre-ship-review` → `azure-devops` → `swebifix` → `feature-archive`

## Guardrails

Do not:

- treat archived docs as active requirements
- create duplicate planning files for the same feature without reason
- invent requirements not grounded in the task or docs
- silently ignore architecture, test expectations, or known pitfalls
- leave the repo in a partially updated state without saying so
- write plans, feature docs, or decisions outside the repository — everything belongs under `docs/features/active/<feature-name>/`
- start implementing without reading `docs/architecture/codebase-guide.md` — it exists to prevent context-blind code generation
