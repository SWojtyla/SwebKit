# Test Plan - style-system-css-architecture

---

title: "Test Plan - style-system-css-architecture"
owner: ""
status: "In progress"
created: "2026-06-14"
updated: "2026-06-14"

---

## Goal

Validate that splitting `app.css` into imported layers preserves app startup, stylesheet loading, inventory reporting, and current visuals.

## Automated Checks

- `dotnet build src/SwebKit.App/SwebKit.App.csproj /p:AppxPackageSigningEnabled=false`
- `scripts/style-inventory.ps1 -Top 20`
- `git diff --check`
- Editor diagnostics for `app.css`, `wwwroot/styles/*.css`, `style-inventory.ps1`, and docs.

## Manual Checks

- Launch app in dark theme and confirm shell/nav/page surfaces render normally.
- Switch to a light theme and confirm tokens/theme imports apply.
- Open Dashboard, AKS, Service Bus, API Client, Redis, Storage, Observability, Pipelines, and Settings.
- Confirm no blank/unstyled screen appears during startup.

## Acceptance Criteria

- `app.css` is a small import-only entry point.
- Layer files load in the original selector order.
- App build passes with local signing disabled.
- Inventory reports layer metrics.
- No obvious visual regression in manual review.