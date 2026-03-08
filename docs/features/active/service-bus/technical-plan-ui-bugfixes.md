---
title: "Technical Plan - Service Bus: UI Bug Fix Pack (2026-03)"
owner: ""
status: "In Progress"
created: "2026-03-08"
updated: ""
---

# Technical Plan - Service Bus: UI Bug Fix Pack (2026-03)

## Status

- Plan state: In progress (SB-UI-BUG-01..04 complete where noted)
- Scope: UI-focused fixes for 4 reported Service Bus defects

## Goal

Address DLQ count/render mismatch, table truncation and horizontal scroll issues, left-panel scroll interference, and encoded topic label artifacts.

## Sequencing & Fixes

- Fix data/render correctness (DLQ counts & mode distinction)
- Refactor table layout to avoid unnecessary horizontal scroll
- Add namespace panel collapse/expand ergonomics
- Remove encoded artifacts from topic labels and standardize iconography

## Traceability

- `docs/features/active/service-bus/index.md`
- `technical-plan-ui.md`
- `test-plan.md`

