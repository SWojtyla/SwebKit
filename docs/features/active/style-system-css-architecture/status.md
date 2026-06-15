# Status - style-system-css-architecture

---

title: "Status - style-system-css-architecture"
owner: ""
state: "Review"
jira: ""
branch: ""
started: "2026-06-14"
last_updated: "2026-06-14"

---

## Quick Summary

`app.css` has been reduced to an ordered import entry point and the global rules now live in named layer files under `wwwroot/styles/`.

**Jira:** not linked

**Current focus:** Validate visually in the running app, then continue future cleanup by moving feature-specific global styles into component `.razor.css` files when touched.

## Progress Checklist

- [x] CSS section boundaries mapped
- [x] `wwwroot/styles/` layer files created
- [x] `app.css` converted to import entry point
- [x] Inventory script updated for layer metrics
- [x] Architecture guide updated
- [ ] Manual visual review complete
- [x] Ready for review

## Completed

- Split the current global stylesheet into 8 ordered layer files.
- Preserved the existing `index.html` link to `app.css`.
- Updated `scripts/style-inventory.ps1` to report `StyleLayerFileCount` and `StyleLayerLines`.
- Updated `docs/architecture/codebase-guide.md` with layer ownership.
- Final inventory reports `AppCssEntryLines: 9`, 8 layer files, and 5,725 total style-layer lines.

## Remaining

- Manual visual review in dark and light themes.
- Future cleanup: move remaining feature-specific global rules out of `styles/03-workspaces.css`, `styles/06-observability.css`, and `styles/07-pipelines-legacy.css` when those areas are touched.

## Validation

- Test Plan: `test-plan.md`
- Validation status: automated checks passed; manual visual review remains.
- `scripts/style-inventory.ps1 -Top 20` passed and reports the new layer metrics.
- `dotnet build src/SwebKit.App/SwebKit.App.csproj /property:GenerateFullPaths=true /p:Configuration=Debug /p:Platform=x64 /p:AppxPackageSigningEnabled=false /consoleloggerparameters:NoSummary` passed.
- `get_errors` passed for `app.css`, `wwwroot/styles`, `style-inventory.ps1`, `codebase-guide.md`, and feature docs.

## Notes

- This is a structural split, not a visual redesign.
- `app.css` must remain the stable entry point unless `index.html` is intentionally updated in a future feature.