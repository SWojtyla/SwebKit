---
name: subagent-contract
description: 'Standard response format for subagents reporting back to an orchestrator. Use when: completing a delegated task, reporting results to orchestrator, operating as a subagent, response format for orchestrator, single response contract.'
---

# Subagent Response Contract

When reporting to an orchestrator, always return **plain text** using this exact template. No JSON. No extra headers outside the template.

~~~
Brief description of what was done.

Files created/modified:
- path/to/file.ext - short purpose

Validation:
✓ Build: <command> (pass/fail/not run)
✓ Tests: <command> (pass/fail/not run)
✓ Lint: <command> (pass/fail/not run)
✓ A11y: <check> (pass/fail/not run)
✓ Security: <check> (pass/fail/not run)

Open questions:
- Q1
~~~

## Rules

- Mark inapplicable validation steps as `n/a` with a brief reason (e.g., `A11y: n/a — backend only`).
- Omit `Open questions` if there are none.
- For transient failures, attempt one retry with a clearer prompt. For non-recoverable failures, return a plain-text error in this format stating the failure and remediation steps.
