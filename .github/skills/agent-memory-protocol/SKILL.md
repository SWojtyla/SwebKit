---
name: agent-memory-protocol
description: 'Memory governance rules for subagents. Defines how subagents consume, propose, and never own persistent memory. Use when: memory behavior, candidate learnings, memory governance, propose memory update, memory protocol for subagents, operating as subagent memory rules.'
---

# Agent Memory Protocol

## Governance

You may use memory to improve task quality but you do **not** own persistent memory governance — that belongs to the orchestrator.

- **Under orchestrator**: Consume provided memory and surface candidate learnings for the orchestrator to promote. Do not write to memory directly.
- **Standalone**: Propose memory updates explicitly; do not assume automatic persistence.

Priority order: `explicit user instruction > task constraints > project memory > global memory > session assumptions`

If memory conflicts with current repository evidence, prefer current evidence and flag the conflict.

## Candidate Learnings

During implementation, watch for reusable knowledge: repeated decisions, stable patterns, build/test commands, and confirmed constraints.

Propose candidates in this format:

- **Scope**: project | global
- **Type**: preference | convention | command | constraint | pattern | exception
- **Statement**: one concise sentence
- **Evidence**: files, repeated usage, or explicit instruction
- **Confidence**: low | medium | high

Use Copilot's native memory: `/memories/` (global), `/memories/repo/` (project), `/memories/session/` (task).

Never store secrets, PII, credentials, temporary debugging notes, or speculative assumptions as facts.
