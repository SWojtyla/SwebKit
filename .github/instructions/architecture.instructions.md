---
description: Rules for editing architecture documentation
applyTo: 'docs/architecture/**'
---

# Architecture Documentation — Edit Rules

Before editing any file under `docs/architecture/`, follow these rules.

## Architecture docs are constraints

These documents define the system's structure, design decisions, and integration boundaries.
Treat them as authoritative — do not silently contradict them in code.

## When to update

Update architecture docs when:

- A supported functionality's behavior changes (Service Bus, Observability, AKS, Redis, Settings, Projects)
- A new top-level functionality is added
- A design decision is revised or superseded
- The tech stack or solution layout changes

## How to update

- Update the matching file under `docs/architecture/functionalities/` in the same change set as the code
- If a new functionality is added, create a new file and add it to the Functional Deep Dives list in `docs/architecture/architecture.md`
- Record the reason for the change in the feature's `decisions.md` if the change is non-obvious

## What not to do

- Do not update architecture docs speculatively — only reflect implemented reality
- Do not remove constraints without a recorded decision
- Do not add temporary or experimental patterns to architecture docs
