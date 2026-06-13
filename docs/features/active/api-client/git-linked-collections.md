# Git-Linked Collections — API Client Phase 9

---

## Scope

Add SwebKit-owned, git-friendly API collection roots that users can link from one or more local repositories and open alongside local app-managed collections. This is not Bruno sync and not Postman sync. Bruno and Postman remain import/export sources only. The linked roots use a SwebKit-native folder format, are edited by SwebKit, and are versioned by the user's existing Git repositories.

Out of scope for the first implementation: hosted collaboration, cloud sync, automatic pull/rebase/stash, PR creation, arbitrary Git command execution, and storing secret values in the repository.

---

## Goals

- Let users add multiple local repo folders as linked API collection roots.
- Show linked roots in the API collection tree beside local collections.
- Persist each request as a small, stable file so Git diffs and merges are readable.
- Keep large request bodies, GraphQL documents, and variables in sibling files when useful.
- Keep secrets out of Git by storing only references in repo files.
- Provide safe Git awareness and a staged path to basic Git actions without turning SwebKit into a full Git client.

---

## Proposed Folder Format

Root marker:

```text
.swebkit-api/
  swebkit.json
  collections/
    orders/
      collection.json
      get-order.swebreq.json
      create-order.swebreq.json
      create-order.body.json
    admin/
      users.swebreq.json
      users.graphql
      users.variables.json
  environments/
    dev.swebenv.json
    test.swebenv.json
```

`swebkit.json` owns the root schema version and display metadata:

```json
{
  "schemaVersion": 1,
  "format": "swebkit-api-root",
  "name": "Project A APIs"
}
```

Request files are intentionally sparse. Missing fields mean defaults:

```json
{
  "method": "GET",
  "url": "{{baseUrl}}/orders/{{orderId}}"
}
```

Defaults:

| Missing field  | Default                                   |
| -------------- | ----------------------------------------- |
| `name`         | Title-cased file name                     |
| `id`           | Stable ID derived from relative file path |
| `headers`      | Empty                                     |
| `query`        | Empty                                     |
| `body`         | None                                      |
| `auth`         | Inherited                                 |
| `captureRules` | Empty                                     |

Common headers and query values can use compact object syntax:

```json
{
  "method": "POST",
  "url": "{{baseUrl}}/orders",
  "headers": {
    "Content-Type": "application/json"
  },
  "body": {
    "jsonFile": "create-order.body.json"
  }
}
```

Use verbose array syntax only when order, disabled entries, duplicate keys, or comments/metadata are needed:

```json
{
  "headers": [{ "key": "X-Debug", "value": "true", "enabled": false }]
}
```

GraphQL request files should keep GraphQL in real `.graphql` files:

```json
{
  "method": "GRAPHQL",
  "url": "{{graphqlUrl}}",
  "queryFile": "users.graphql",
  "variablesFile": "users.variables.json"
}
```

This keeps PR diffs readable and avoids escaped multiline strings.

---

## Secret Handling

Repo files must never contain secret values. They may contain references only.

Environment file example:

```json
{
  "name": "dev",
  "variables": {
    "baseUrl": "https://dev-api.example.com",
    "graphqlUrl": "https://dev-api.example.com/graphql"
  },
  "secrets": {
    "apiToken": {
      "provider": "CredentialStore",
      "ref": "project-a/dev/api-token"
    },
    "paymentClientSecret": {
      "provider": "KeyVault",
      "ref": "payments-client-secret"
    }
  }
}
```

Request usage:

```json
{
  "headers": {
    "Authorization": "Bearer {{secret:apiToken}}"
  }
}
```

Resolution rules:

- `{{name}}` resolves from non-secret variables in the active linked environment, then collection/root variables.
- `{{secret:name}}` resolves through the active linked environment's secret reference.
- Credential Store values are local to the machine and are not exported into the linked root.
- Key Vault references store the secret name only; value resolution happens at send time.
- Missing secrets render a visible unresolved-secret badge and a [Configure secret] action.

Optional future dev convenience: `.swebkit-secrets.local.json`, ignored by default, can map secret names to local refs. Do not implement this in the first slice.

---

## Architecture Touchpoints

- Project: `src/SwebKit.Core/`
  - New linked-root domain models: `LinkedCollectionRoot`, `SwebKitApiRootManifest`, `SwebKitRequestFile`, `SwebKitEnvironmentFile`.
  - New services: linked-root discovery, root repository, request file reader/writer, environment file reader/writer.
  - New Git abstraction: status/branch service first, safe command service later.
- Project: `src/SwebKit.App/`
  - `ApiClientPage.razor`: load local collections and linked roots as sibling tree sections.
  - `CollectionTree.razor`: grouped roots, root-level status badges, refresh affordances.
  - New dialogs/panels: Add Linked Root, Linked Root Manager, Missing Secrets panel, Git Actions panel.
- Persistence:
  - Add linked root list to user settings or a new small app-data file (`api-linked-roots.json`).
  - Keep root paths user-local; never include them in export bundles.
- File I/O:
  - Load linked roots from disk on page start and refresh.
  - Save request edits atomically to the corresponding request/body/query file.
  - Use file hash or last-write metadata to detect external edits before overwriting.

---

## UI Integration

### Tree Layout

The left tree should have two durable top-level groups:

```text
Local Collections
  Scratch
  Personal Debugging

Linked Repositories
  Project A APIs        main  3 modified
    Orders
      GET Get Order
      POST Create Order
    Admin
      GRAPHQL Users
  Project B APIs        feature/auth-cleanup  clean
```

Root header actions:

- Refresh
- Open folder
- Reveal in terminal
- Manage secrets
- Git panel
- Remove from SwebKit (does not delete files)

### Toolbar

Add an [Add linked root] action near collection import/export, plus a compact linked-root selector/filter when multiple roots exist.

### Empty State

When no linked roots exist, show a direct [Add linked root] affordance in the collection pane. The dialog should explain that SwebKit will look for `.swebkit-api/swebkit.json` or create one if the chosen folder is empty/confirmed.

### Linked Root Manager

Settings-style panel:

| Column  | Meaning                                       |
| ------- | --------------------------------------------- |
| Name    | Manifest display name                         |
| Path    | Local folder path                             |
| Branch  | Current Git branch when available             |
| Status  | Clean / modified / missing / invalid format   |
| Actions | Open, refresh, remove, repair/create manifest |

### Save UX

- Local collections keep current repository save behavior.
- Linked request edits save to files in the linked root.
- If external changes are detected, show: Reload from disk / Keep mine / Save as copy. Do not silently overwrite.

### Missing Secrets UX

Show unresolved secret state close to where the user acts:

- Environment manager: secret rows with "not configured on this machine" badges.
- Request builder: inline unresolved secret badges in URL/header/body preview.
- Send action: soft block only when the unresolved secret is needed by the request; offer [Configure secret].

---

## Git Actions Strategy

Yes, SwebKit should eventually include Git actions, but they should be staged and scoped to linked API roots only.

### Phase 9A — Git Awareness

- Detect whether a linked root is inside a Git worktree.
- Show branch name and clean/modified/untracked counts.
- Show changed files under the linked API root only.
- Provide [Open terminal here] and [Open folder] actions.

### Phase 9B — Safe Basic Actions

- Create branch.
- Switch branch only when the linked API root is clean, or after explicit confirmation when dirty.
- Commit selected SwebKit API files with a message.
- Push current branch when an upstream exists.

### Phase 9C — Review/PR Helpers

- Copy branch summary.
- Open remote compare URL when remote provider can be inferred.
- Create PR only after Azure DevOps/GitHub integration is explicit and authenticated.

### Git Implementation Recommendation

Start with the installed `git` CLI rather than a Git library.

Rationale:

- No new native dependency.
- Matches the user's actual local Git config, remotes, credential helpers, and SSH setup.
- Easier to reason about for branch/commit/push.

Guardrails:

- Run Git commands only with a fixed command builder, never user-supplied raw arguments.
- Set working directory to the linked root or detected repo root.
- Scope status/commit file lists to `.swebkit-api/` or configured API root path.
- Confirm before branch switch, commit, or push.
- Do not implement pull/rebase/stash in the first Git action slice.

---

## Implementation Tasks

### Format and Persistence

- [ ] Define linked-root DTOs and serializer options with omitted defaults.
- [ ] Implement root discovery from user-provided paths.
- [ ] Implement request/environment file readers with validation diagnostics.
- [ ] Implement deterministic writers for request metadata and body/query sidecar files.
- [ ] Add external-change detection before file overwrite.
- [ ] Store configured linked roots in app-local settings.

### UI

- [ ] Add linked roots section to the collection tree.
- [ ] Add Add Linked Root dialog with create/use-existing modes.
- [ ] Add Linked Root Manager panel.
- [ ] Add root header actions and root status badges.
- [ ] Add missing secret badges and configure-secret flow for linked environments.

### Git

- [ ] Implement Git status provider for branch and changed files.
- [ ] Add Git panel with status and changed-file list.
- [ ] Add safe branch create/switch actions.
- [ ] Add commit selected API files action.
- [ ] Add push current branch action.

---

## Validation Notes

- Unit tests for compact request serialization and default inference.
- Unit tests for request sidecar body/query file loading.
- Unit tests that secret values are never written to linked root files.
- Unit tests for invalid manifest/request diagnostics.
- Unit tests for Git command builders and output parsers.
- bUnit tests for tree grouping, linked root badges, missing-secret prompts, and root manager actions.
- Manual checks with two local Git repos open at the same time, external file edits, branch switch, commit, and push.
