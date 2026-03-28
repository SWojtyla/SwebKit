---
name: swebistart
description: 'Generate the three architecture starter files for a new project or codebase: architecture.md (system map), design.md (component flows), and codebase-guide.md (implementation navigation). Use when: new project, scaffold architecture docs, initialize architecture documentation, generate arch docs, new codebase setup, onboard new repo, swebistart.'
---

# Scaffold Architecture Docs

Generate the three authoritative architecture files for a project that does not yet have them. These files are the foundation of the docs-first AI workflow — without them, AI contributors fly blind.

## When to use

- When opening or onboarding onto a new project or repository
- When `docs/architecture/` is missing or only partially populated
- When a new service is being added that needs its own architecture documentation

Do **not** regenerate these files if they already exist and are up to date. Check first.

---

## The three files and their mandates

| File | Mandate | Answers |
|------|---------|---------|
| `architecture.md` | System-wide map | What are the major components and how do they connect? |
| `design.md` | Component blueprints | How is each component internally structured and what are its key flows? |
| `codebase-guide.md` | Implementation navigation | Where do I start looking in the code? |

Each file has a strict scope. They must not duplicate each other.

---

## Procedure

### Phase 1 — Explore the codebase

Before writing anything, gather facts:

1. **Folder structure** — List `src/` (or equivalent root) to identify top-level projects and their naming patterns.
2. **Entry points** — Find `Program.cs`, `Startup.cs`, `app.ts`, `main.py`, or equivalent bootstrapping files. Read them briefly.
3. **Key projects / services** — Identify which projects are runtime services, which are libraries, which are tests, which are tools.
4. **Messaging / integration** — Check for Service Bus, Event Hub, RabbitMQ, HTTP clients, or other integration patterns.
5. **Cross-cutting concerns** — Spot auth, logging, observability, caching, resilience patterns in shared/infra projects.
6. **Naming conventions** — Infer project suffix conventions, class suffix conventions (e.g., `*Handler`, `*Sender`, `*Service`).
7. **Existing docs** — Check `docs/` for any existing architecture notes, README content, or diagrams to incorporate.

Use targeted directory listings and grep searches. Do not read entire solution files — sample strategically.

---

### Phase 2 — Generate `docs/architecture/architecture.md`

**Target audience:** someone who needs to understand the whole system at a glance.

Structure:

```markdown
# <Project Name> Architecture

## Mandate

**This is the system-wide map.** It answers: _what are the major components and how do they connect?_

[One-sentence description of when to update this file]

## Purpose

[One paragraph describing what the system does and its architectural style]

## System Context

[Bullet list of the most important entry points and external integrations]

## High-Level Flow

[Mermaid flowchart showing the main runtime components and their connections]

## Runtime Components

[One section per major component: project path, responsibility, key files]

## Cross-Cutting Concerns

[Auth, logging, messaging, caching, observability — where they live]

## Where To Start For Common Tasks

[Task → file mapping for 4–6 most common development tasks]
```

Rules:
- Use a Mermaid diagram to visualize the component connections
- List key files per component (3–5 max)
- Do not describe internal flows — that belongs in `design.md`
- Keep it stable: this file should rarely need updating

---

### Phase 3 — Generate `docs/architecture/design.md`

**Target audience:** someone about to implement or refactor a specific component.

Structure:

```markdown
# <Project Name> Design

## Mandate

**This is the component blueprint.** It answers: _how is each component internally structured, and what are the key flows through it?_

[One-sentence description of when to update this file]

## Scope

[Reference to architecture.md; list which flows this document covers]

## <Flow Name> Flow (repeat per significant flow)

### Intent
[What this flow achieves]

### High-Level Sequence
[Mermaid sequence diagram]

### Design Notes
[Key decisions, async vs sync boundaries, responsibilities]

## Key Reference Points

[Compact table or list of the most critical file → responsibility mappings for quick lookup]
```

Rules:
- Cover at minimum the main happy-path flow end-to-end
- Use Mermaid sequence diagrams for flows that cross component boundaries
- Do not list every file — focus on the files that encode the key decisions
- Do not duplicate the folder map (that belongs in `codebase-guide.md`)

---

### Phase 4 — Generate `docs/architecture/codebase-guide.md`

**Target audience:** an AI or developer who is about to start writing code and needs to know where to look.

Structure:

```markdown
# <Project Name> Codebase Guide

## Mandate

**This is the implementation navigation map.** It answers: _where do I start looking in the code?_

[One-sentence description of when to update this file]

## Entry Points by Task Type

[Table: Task | Starting file — cover 8–12 common implementation tasks]

## Key Folders and Responsibilities

[ASCII tree or bullet list of src/ with one-line responsibility per folder]

## Naming Conventions

[Table: Pattern | Meaning — project suffixes and class suffixes]

## Cross-Cutting Concerns

[Table: Concern | Where it lives — auth, logging, messaging, caching, resilience, etc.]

## Feature-to-File Quick Lookup

[Table: Feature area | Key files — most common feature areas mapped to their files]
```

Rules:
- Be concrete: actual file paths, not descriptions
- Cover every major folder in the entry points or folder map
- The naming conventions table should let an AI infer where a new file should go without searching
- No flows, no diagrams — this is a lookup table, not a narrative

---

### Phase 5 — Create the files

- Write the files to `docs/architecture/architecture.md`, `docs/architecture/design.md`, and `docs/architecture/codebase-guide.md`.
- If `docs/architecture/` does not exist, create the directory.
- If any file already exists, **do not overwrite it** — ask the user first.

---

### Phase 6 — Report back

After generating the files, provide a brief summary:

- What was discovered about the codebase
- What was written in each file (one line each)
- Any gaps or assumptions made that the user should verify (e.g., unclear entry points, ambiguous folder responsibilities)
- Remind the user: these files are living documents — update them when component structure or flows change
