---
name: feature-archive
description: 'Archive a completed feature by creating a summary and moving the folder. Use when: archive feature, feature is done, move to archive, close out feature, feature lifecycle complete, archive completed feature.'
---

# Feature Archive

Use when a feature is ready to be closed out.

- **If the feature has a linked Jira ticket:** delete the active folder — Jira is the durable record. No archive needed.
- **If there is no Jira ticket:** create a summary and move the folder to `docs/features/archive/`.

## When to Close Out

**HARD GATE — read `status.md` before doing anything else.**

Close out only when **all** of the following are true:

- Feature state is `Done` in `status.md`
- Implementation, tests, and docs are aligned (see `docs/ways-of-working/definition-of-done.md`)
- No open blockers or pending follow-up belonging to this feature folder

**If any condition is unmet: STOP.**
State exactly which condition(s) fail and ask the user for explicit confirmation before proceeding.

Example stop message:
> "The feature is in state `Review`, not `Done`. Visual verification is still listed as pending in `status.md`. Do you want to close it out now anyway?"

Only proceed after the user explicitly confirms.

---

## Path A: Jira ticket linked — Delete active folder

**Use this path when `index.md` contains a Jira ticket link.**

1. **Read `index.md`** to confirm the Jira ticket key.
2. **Add a closing Jira comment** using `addCommentToJiraIssue` (see format below).
3. **Transition to Done** using `transitionJiraIssue`.
4. **Delete the active folder** and all files in it.

### Closing Jira comment format

Choose the format based on the scope of what was delivered.

---

#### Small fix (bug, config change, minor tweak)

Use a plain short comment — 2 to 4 sentences. Describe:
- What the problem was
- What was changed to fix it
- How it was verified (if non-trivial)

Example:
```
Fixed a null reference in the quote response mapper when the insurer returns no coverages.
The mapper now returns an empty list instead of throwing.
Covered by an added unit test.
```

---

#### Feature or substantial change

Use the structured template below. Populate every section with real content from the feature docs. Omit a section only if it has nothing meaningful to say.

```
Implementation complete — <Feature title>
<One sentence describing what was delivered and archived, including the archive date>

What was delivered
<Bullet per major deliverable — describe outcomes, not implementation steps>

Key technical decisions
<Bullet per important tradeoff or architectural choice with future reuse value>

Validation
<Bullet per test suite or validation run — include pass counts if known>

Scope boundary
<Bullet per intentional out-of-scope item deferred or non-blocking>

Code area
<Path(s) to the main source folder(s) touched>
```

---

**How to decide:**

| Situation | Format |
|-----------|--------|
| Single file / single function change | Short fix comment |
| Bug fix with a clear root cause | Short fix comment |
| Config or dependency update | Short fix comment |
| New flow, new handler, new integration | Structured template |
| Multiple deliverables or technical decisions | Structured template |
| Feature with dedicated feature folder and `decisions.md` | Structured template |

**Rules (both formats):**
- Derive content from `status.md`, `decisions.md`, and `test-plan.md`.
- Do **NOT** include step-by-step implementation notes, transient status history, or content already in the ticket description.
- A team member should be able to read the comment and understand what changed and why — in under 2 minutes.

---

## Path B: No Jira ticket — Archive the active folder

**Use this path when `index.md` does NOT contain a Jira ticket link.**

1. **Read the active feature folder** in full — `index.md`, `status.md`, `decisions.md`, and any implementation modules.
2. **Read the archive summary template** at `docs/features/_templates/archive-summary.md`.
3. **Create `summary.md`** inside the active folder using the template — populate every section with real content:
   - Goal: restate what the feature set out to achieve
   - Delivered: concrete list of what was shipped
   - Key decisions: distilled from `decisions.md` or implementation notes — keep only what has future reuse value
   - Validation: what was tested and how
   - Lessons learned: short, actionable, specific — based on actual problems or patterns observed
   - Follow-up: remaining debt or next steps with owners
4. **Create the archive folder** at `docs/features/archive/<feature-name>/`.
5. **Move only durable files** into the archive folder:
   - Always move `summary.md`
   - Move `decisions.md` only if it contains decisions with future reuse value not already captured in `summary.md`
   - Do **not** move `index.md`, `status.md`, `frontend.md`, `backend.md`, `test-plan.md`, or any other transient execution files
6. **Delete the active folder** and all remaining files in it.
7. **Do not touch** files outside the feature folder — architecture docs, pitfalls entries, and source files are updated in place as a separate step.

---

## Quality Bar

**Path A:** After deletion, a team member should be able to read the linked Jira ticket and understand what was delivered.

**Path B:** After archiving, a new team member should be able to read `summary.md` and understand what was built, why, and what was learned — in under 2 minutes. No sensitive or temporary content should remain.
