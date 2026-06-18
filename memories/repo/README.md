# SwebKit Memories Repository

## 📚 Purpose

This folder contains **session-persistent memory and context** for Mistral Vibe when working on SwebKit. Unlike Vibe's session memory (which is lost between sessions), files in this folder provide **durable project context** that Vibe can reference across sessions.

## 📁 Files

| File | Purpose | Audience |
|------|---------|----------|
| `README.md` | This file - explains the memories system | Developers |
| `project-context.md` | Current project state, recent activity, navigation guide | Vibe + Developers |
| `aks-kubernetesclient-notes.md` | Historical notes about Kubernetes client | Developers |
| `editing-notes.md` | Historical editing notes | Developers |

## 🤖 How Vibe Uses This

When you start a session and reference this folder, Vibe can:

1. **Quickly onboard** to the project's current state
2. **Understand recent changes** and active work
3. **Navigate the codebase** more efficiently
4. **Avoid repeating** past debugging sessions

## 💡 Best Practices

### For Vibe
- Reference `project-context.md` at the start of each session
- Update `project-context.md` when significant changes occur
- Add session notes using the template at the bottom of `project-context.md`

### For Developers
- Add historical context to this folder when it's useful for future work
- Keep files concise and focused
- Use markdown formatting for readability
- Date all entries

## 📝 Adding New Memory Files

Create new `.md` files in this folder for:
- **Feature-specific context** that should persist beyond the feature's lifecycle
- **Debugging session summaries** that contain valuable lessons
- **Architecture decisions** that aren't documented elsewhere
- **Integration patterns** that are non-obvious or unique to SwebKit

## 🗑️ Cleanup

Periodically review this folder and:
- **Archive** old files that are no longer relevant (move to `memories/archive/` if created)
- **Update** stale information
- **Remove** duplicate or redundant files

## 🔗 Related

- `.vibe/instructions.md` - Project-specific AI instructions (loaded automatically)
- `AGENTS.md` - Global agent instructions for the repository
- `docs/architecture/` - Official architecture documentation
- `docs/pitfalls/` - Known issues and solutions

---

**Maintained by**: SwebKit Team + Mistral Vibe  
**Location**: `memories/repo/`  
**Created**: 2026-06-18
