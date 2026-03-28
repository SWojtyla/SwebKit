---
name: atlassian-integration
description: 'Jira and Confluence integration for the feature execution workflow. Covers ticket creation, status tracking, commenting, and Confluence reference lookups. Use when: creating features with Jira tracking, updating Jira tickets, searching Jira/Confluence, linking features to tickets, feature lifecycle with Jira.'
---

# Atlassian Integration

Integrate Jira and Confluence into the docs-first feature execution workflow. Features get a linked Jira ticket for team-level visibility; ticket status stays synchronized with feature `status.md`.

## Prerequisites

The Atlassian MCP server (`com.atlassian/atlassian-mcp-server/*`) must be configured and authenticated in VS Code. All tools require a `cloudId` parameter.

## CloudId discovery

Use the site hostname directly as `cloudId` — pass `myportima.atlassian.net` to any tool's `cloudId` parameter. If that fails, call `getAccessibleAtlassianResources` to list available cloud instances and extract the UUID.

Cache discovery results for the session. Never store cloudId in code or memory — rediscover per session.

## Content format

Always use `contentFormat: "markdown"` when creating or reading Jira/Confluence content. This keeps content readable and avoids ADF complexity.

## Jira workflow

### When creating a new feature

1. **Get the ticket key** — the user provides the Jira ticket key (e.g., `PROJ-123`). If the user does not provide one, search Jira using `searchJiraIssuesUsingJql` or `searchAtlassian` to find a matching ticket by title or keyword.
2. **Record the ticket in feature docs** — add a `Jira ticket` entry in the feature `index.md` quick links section:
   ```markdown
   ## Quick links

   - **Jira ticket:** [PROJ-123](https://myportima.atlassian.net/browse/PROJ-123)
   ```
3. **Set initial status** — if the ticket needs to be moved from its default status (e.g., to "In Progress"), call `getTransitionsForJiraIssue` to find the transition ID, then `transitionJiraIssue`.

> **Do NOT create Jira tickets automatically.** Only create a ticket if the user explicitly asks you to. Use `createJiraIssue` only on explicit request.

### When updating feature progress

- **Status changes** — when `status.md` state changes (e.g., Planned → In Progress → Review → Done), transition the Jira ticket to match using `transitionJiraIssue`.
- **Progress comments** — after significant milestones (implementation complete, tests passing, blocking issue found), call `addCommentToJiraIssue` with a brief markdown summary. Keep comments to **3–5 lines** — outcomes only; do not duplicate `status.md` content verbatim.
- **Blocker found** — add a comment to the Jira ticket describing the blocker. If the blocker is another Jira ticket, use `createIssueLink` with type `"Blocks"`.

### When completing a feature

**HARD GATE — check subtasks before doing anything else.**

1. **Check for subtasks/child issues** — call `getJiraIssue` and inspect the `subtasks` field (or use `searchJiraIssuesUsingJql` with `parent = PROJ-123`). If any subtask is NOT in `Done` status:
   - **STOP.** Do not transition the parent ticket to Done.
   - Report which subtasks are still open and ask the user for explicit confirmation before proceeding.
   - The active feature folder belongs to a single subtask scope — only that subtask's work is being closed, not the parent story.

   Example stop message:
   > "PROJ-123 has open subtasks: PROJ-124 (In Progress), PROJ-125 (New). Only the BE subtask is done. Do you want to close only the BE subtask, or the entire story?"

2. **Add a closing comment** — call `addCommentToJiraIssue` with a concise summary (5–8 lines maximum):
   - What was delivered: outcomes only — no implementation steps, no checklist items
   - Any intentional out-of-scope follow-up items, if any
   - Omit anything already visible in the ticket description or fields
   - If commenting on a subtask only, add the comment to the **subtask**, not the parent story
3. **Transition to Done** — use `transitionJiraIssue` to move **only the relevant ticket** (subtask or story) to Done. Never transition the parent story if subtasks remain open.
4. **Delete the active folder** — since the Jira ticket is the durable record, delete the `docs/features/active/<feature-name>/` folder. Do NOT archive it.

### When searching

- **Rovo Search** (general) — `searchAtlassian` with a natural language query. Use this by default for Jira + Confluence combined search.
- **JQL** (structured Jira) — `searchJiraIssuesUsingJql` for precise queries like `project = PROJ AND status = "In Progress"`.
- **CQL** (structured Confluence) — `searchConfluenceUsingCql` for Confluence-specific queries.

## Confluence workflow

### Reading context

- **Get a page** — `getConfluencePage` with the page ID and `contentFormat: "markdown"`.
- **Search for pages** — `searchConfluenceUsingCql` with CQL like `title ~ "architecture" AND space = "TEAMSPACE"`.
- **Browse a space** — `getConfluenceSpaces` to find the space ID, then `getPagesInConfluenceSpace`.

### Writing (when explicitly requested)

- **Create a page** — `createConfluencePage` with markdown content. Link back to the feature folder in the page body.
- **Update a page** — `updateConfluencePage` to revise existing content. Always include a `versionMessage`.
- Do NOT create or update Confluence pages unless the user explicitly asks. Default Confluence usage is read-only for context gathering.

## Tool reference (most common)

| Action | Tool |
|--------|------|
| Discover cloud instances | `getAccessibleAtlassianResources` |
| List projects | `getVisibleJiraProjects` |
| Create ticket | `createJiraIssue` |
| Update ticket fields | `editJiraIssue` |
| Add comment | `addCommentToJiraIssue` |
| Transition status | `transitionJiraIssue` |
| Get transitions | `getTransitionsForJiraIssue` |
| Get ticket details | `getJiraIssue` |
| Search Jira + Confluence | `searchAtlassian` |
| Search Jira (JQL) | `searchJiraIssuesUsingJql` |
| Search Confluence (CQL) | `searchConfluenceUsingCql` |
| Read Confluence page | `getConfluencePage` |
| Create Confluence page | `createConfluencePage` |
| Link two issues | `createIssueLink` |

## Safety

- Never store API tokens, cloudId UUIDs, or account IDs in files, code, or memory.
- **Never transition a parent story to Done if it has open subtasks** — check subtasks first, transition only the relevant ticket.
- Never transition a ticket to Done without verifying feature completion criteria.
- Never create Confluence pages without explicit user request.
- Use `contentFormat: "markdown"` consistently to avoid ADF parsing issues.
- Prefer `searchAtlassian` (Rovo) over JQL/CQL unless structured queries are needed.
