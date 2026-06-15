# Archive Summary — API Client

## Goal

Add a Postman/Insomnia/Bruno-alike API client to SwebKit so users can author, execute, version, and inspect API requests without leaving the desktop operations workspace.

## Delivered

- REST request authoring and execution with params, headers, body modes, auth, response viewer, history, and request formatting.
- GraphQL query/mutation support, schema introspection, operation selection, error rendering, and `graphql-ws` subscriptions.
- WebSocket connect/send/listen workflow with bounded virtualized message log and saved message templates.
- Local collections/environments persisted with existing app-data atomic write and backup recovery patterns.
- Secret-safe variables backed by plain values, Windows Credential Store, Azure Key Vault, and generated variables.
- Auth support for Bearer, API key, Basic, OAuth 2 client credentials, and OAuth 2 auth code through MAUI `WebAuthenticator`.
- No-code post-request capture rules using JSONPath, headers, and status code.
- SwebKit/Postman/Bruno export/import plus full configuration bundle integration.
- Git-linked `.swebkit-api/` repository roots with sparse request files, sidecars, linked environments, conflict detection, and scoped Git actions.
- Workflow trust polish: target chip, in-app Git diff preview, staged/unstaged Git review layout, commit preview, and linked-save conflict actions.
- Request portability and inspection: Copy as cURL, Import from cURL, active-request variable inspector, and scrubbed saved response examples.
- Feedback cleanup: consolidated linked repository, import/export, and variable controls into menus; defaulted REST requests to the Body tab; fixed linked-root request targeting; hardened splitter initialization during collection switches; cleaned response history styling.
- Request pinning and the active collection runner were retired after UX review; future custom flows belong to the dedicated advanced-workflows feature.
- Architecture and feature docs updated to describe the implemented API Client foundation.

## Key Decisions

- Collections and environments are stored in separate files to avoid rewriting large collections for environment-only changes.
- SwebKit-native JSON and linked `.swebkit-api/` folder formats are the source schemas; Postman and Bruno remain projections.
- Secrets are stored by reference only and resolved from Credential Store or Key Vault at runtime.
- Monaco remains the editor foundation for code-oriented API surfaces.
- Auth inheritance is null-propagation from request to folder to collection, with explicit `None` opting out.
- Dynamic variables and post-request captures use safe building blocks, not scripts.
- Linked Git actions are scoped to linked API-root files and use fixed command builders.
- Workflow trust work was prioritized before broader automation so users can see where edits are saved, what is committed, and what values resolve.

## Validation Performed

- Focused API Client Core tests passed during the final implementation pass: 26 passed, 0 failed.
- Linked-root/API Client focused diagnostics passed after feedback cleanup.
- MAUI app build passed after implementation and feedback cleanup.
- `git diff --check` passed after the final implementation/review pass.
- Known unrelated build warnings remained outside this feature scope: `DlqView.ShowConfirm` and OAuth2 `WebAuthenticator` platform support.
- Aikido scan could not be run because the Aikido MCP tool was unavailable in the session.

## Lessons Learned

- Linked-root file operations need visible target context and explicit conflict choices; passive error banners are not enough for Git-backed workflows.
- Secret safety needs to be enforced in every projection: exports, cURL, examples, diffs, and linked files.
- Keeping captures, generated variables, assertions, and future flows script-free gives the API Client a safer and more reviewable model than Postman-style scripting.
- Large Blazor workspaces benefit from flattened/virtualized lists and parent-owned state for pinned tabs, response history, and runner output.

## Follow-Up

- Advanced workflow planning moved to `docs/features/active/api-client-advanced-workflows/`.
- Future work should start from that active feature, not this archived foundation folder.

## Archive Note

This feature had no Jira ticket. It was archived by explicit user confirmation after implementation and documentation were complete, then refreshed after feedback cleanup removed request pinning and active collection running. Archive location: `docs/features/archive/api-client/`.
