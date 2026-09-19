# Linked API Projects (folder-per-project storage)

## Scope

Port the linked-collection storage model to the live Tauri + React + sidecar runtime, and make
collection storage a first-class, visible choice instead of an invisible app-data blob.

Today every collection, request, and environment lives in `%APPDATA%\SwebKit\collections.json` /
`environments.json`. Bruno import is a one-time copy, and the Git drawer operates on a
user-picked folder SwebKit never reads or writes. This feature makes it possible to say
"this project lives in folder X" per collection, in a git-friendly file layout, with optional
two-way Bruno `.bru` sync.

## Outcomes

- A user can link any folder as an **API project root** (`.swebkit-api/` inside it), per project.
- Collections and environments in a linked root are stored as files
  (`.swebreq.json`, `collection.json`, `folder.json`, `.swebenv.json`) — readable and
  diffable in git.
- Saving a linked request/collection/environment writes the linked files, not `collections.json`.
- External edits are protected by content-stamp conflict detection (changed-on-disk → conflict,
  never silent overwrite).
- Bruno folders can be *linked* (import + continuous `.bru` write-back) instead of copied.
- The collection tree and settings make storage location visible per collection.
- The Git drawer is wired to linked roots so commits actually contain the user's API files.

## Approach

Keep the React app's whole-store mental model: `useUpdateCollections` still sends the merged
collections array; the **sidecar partitions the save** — local collections go to
`collections.json`, linked collections are synced to their folders by a diff engine in
`LinkedCollectionFileService`. Same for environments.

IDs are persisted explicitly in manifests/files (`collection.json`, `.swebreq.json`,
`.swebenv.json`) so request/collection/environment IDs survive renames and moves — path-derived
`StableId` remains the fallback for files written before this feature.

## Dependencies

- `LinkedCollectionFileService`, `LinkedCollectionRootRepository`, `LinkedGitService`,
  `BrunoSyncService`, `CollectionImportService.ImportBrunoFolderToLinkedRootAsync` — all already
  exist in `SwebKit.Core` (built for the legacy MAUI app, deliberately dormant in the sidecar).
- `ApiClientAgentService` already merges local + linked collections for agent tools.

## Non-goals

- Editing linked roots' files inside an external editor is not watched live — reload is manual
  (a Reload button) or on save-conflict.
- Git rebase/merge/stash stay out of scope (same deferral as the existing Git drawer).
- Moving a *local* collection into a linked root (or back) via drag/drop — v1 exposes "new
  collection location" and "import into root" instead.
