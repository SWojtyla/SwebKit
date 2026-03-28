# Feature: <Feature Name>

> **When to use this template:** For small-to-medium features where the entire plan fits in one file. For large features (3+ implementation modules, multi-sprint scope, or separate frontend/backend teams), use `index.md` + dedicated `backend.md`, `frontend.md`, and `decisions.md` files instead.

---

title: "Feature - <Feature Name>"
owner: ""
status: "Proposed"
jira: ""
created: ""
updated: ""

---

## Summary

One paragraph: what is the feature, what user problem does it solve, what does success look like.

## Goals

- Primary goal(s)
- Out of scope: what this does NOT do

## Jira

<ticket-url> _(or: not linked)_

## Scope & dependencies

- In scope: concise list of deliverables
- Dependencies: related features, external services, or SDKs required
- Risks: primary risks and mitigations

## Technical plan

### Overview

What components are touched, what the approach is, and how it fits existing architecture. Reference `docs/architecture/codebase-guide.md` for entry points.

### Backend changes

- Services / files affected: `src/...`
- Contracts, DTOs, or interfaces to add or change
- Error handling, logging, telemetry changes

### Frontend changes

- Components / pages affected: `src/...`
- User flows: happy path, loading, error, empty state
- Contract or viewmodel changes

### Migration / compatibility

_(Omit if not applicable.)_ Config, data, or breaking changes required on deploy.

## Key decisions

- Decision: [what was decided] — Rationale: [why]
- _(Add more as needed, or extract to `decisions.md` if the list grows)_

## Test plan

- Scenarios: [list the main scenarios to validate]
- Automated: unit tests in `<project>.Tests`, CI gate
- Manual: [specific manual checks required]

## Actions

- [ ] Next step — owner
- [ ] Next step — owner
