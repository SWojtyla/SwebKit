---
name: azure-devops
description: 'Commit, push, open a Pull Request, and optionally trigger the CI pipeline in Azure DevOps. Use when: ship feature, commit and push, create PR, open pull request, trigger CI, push to devops, ship to review, publish branch.'
---

# Azure DevOps — Ship

Commit all changes, push to the feature branch, create a Pull Request in Azure DevOps, and record the PR link in the Jira ticket. Optionally trigger the CI pipeline when requested.

---

## Repository constants

- **Organisation:** `https://dev.azure.com/tfsportima`

## Repository discovery

The organisation is fixed. Derive project, repository, and target branch at runtime from the git remote.

Run:
```powershell
git remote get-url origin
```

The URL will be in one of these forms:
- `https://<user>@dev.azure.com/tfsportima/<project>/_git/<repo>`
- `https://dev.azure.com/tfsportima/<project>/_git/<repo>`

Parse out:
- `<ADO_PROJECT>` = URL-decode the project segment (e.g. `briocomp%20-%20Brio%20Compare` → `briocomp - Brio Compare`)
- `<ADO_REPO>` = the final path segment after `_git/`

Discover the default PR target branch from the repo's remote HEAD:
```powershell
git remote show origin | Select-String 'HEAD branch' | ForEach-Object { ($_ -split ':')[1].Trim() }
```
Use this as `<TARGET_BRANCH>`. Typically `dev` or `main`.

Cache `<ADO_PROJECT>`, `<ADO_REPO>`, and `<TARGET_BRANCH>` for the duration of the skill invocation.

---

## Prerequisites

Before running any steps, verify the following tools are available:

1. **Git** — run `git --version`. Must be installed.
2. **Azure CLI with DevOps extension** — run `az devops --version`.
   - If missing: `az extension add --name azure-devops`
3. **Azure DevOps authentication** — run `az devops login` if not already authenticated, or ensure `AZURE_DEVOPS_EXT_PAT` is set in the environment.

If any prerequisite is unmet, STOP and guide the user to resolve it before continuing.

---

## Procedure

### Step 1 — Preflight checks

1. Run `git status --short` to see what's changed. If there is nothing to commit, report it and stop.
2. Run `git branch --show-current` to capture the feature branch name.
3. **Guard:** If the current branch is `dev` or `main`, STOP and ask the user for explicit confirmation before proceeding. Do not push to integration branches without consent.
4. Read `docs/features/active/<feature-name>/index.md` to extract:
   - Feature goal (one-sentence description)
   - Jira ticket key and URL from the Quick links section
5. Read `docs/features/active/<feature-name>/status.md` to confirm the feature state is `Done` (self-assessment passed). If not `Done`, warn the user but do not block — let them confirm.

### Step 2 — Stage and commit

Derive the commit message from the feature docs before touching any files.

**Determine commit type:**

| Scenario | Type |
|----------|------|
| New capability | `feat` |
| Bug fix | `fix` |
| Documentation only | `docs` |
| Refactor without behaviour change | `refactor` |
| Tests only | `test` |
| Infrastructure / pipeline / tooling | `chore` |
| Work in progress | `wip` |

**Determine commit scope** from the affected component(s). Check which `devops/` subdirectory maps to the changed source paths. Use `*` if the change is cross-cutting.

**Compose the commit message** following the Conventional Commits format (see `CONTRIBUTING.md`):

```
<type>(<scope>): <subject — max 72 chars>

<optional body>

Closes <JIRA-KEY>
```

**ALWAYS confirm the composed commit message with the user before executing the commit.** Show the exact message and wait for approval or correction.

Once confirmed, stage and commit:

```powershell
git add -A
git commit -m "<type>(<scope>): <subject>" -m "" -m "Closes <JIRA-KEY>"
```

### Step 3 — Push to remote

1. Check if the branch already exists on the remote:
   ```powershell
   git ls-remote --heads origin <branch>
   ```
2. If it does NOT exist, push with upstream tracking:
   ```powershell
   git push --set-upstream origin <branch>
   ```
   If it already exists: `git push`
3. Confirm the push succeeded (exit code 0) before continuing. If the push fails with a non-fast-forward error, STOP and tell the user to run `git pull --rebase` first.

### Step 4 — Create the Pull Request

Build the PR description from the feature docs:

```markdown
## What

<feature goal from index.md — one sentence>

## Jira

[<JIRA-KEY>](<jira-ticket-url>)

## Checklist

- [ ] Build passes
- [ ] Tests pass
- [ ] Docs updated
```

Run the following command and capture the output as JSON:

```powershell
az repos pr create `
  --org "https://dev.azure.com/tfsportima" `
  --project "<ADO_PROJECT>" `
  --repository "<ADO_REPO>" `
  --source-branch "<feature-branch>" `
  --target-branch "<TARGET_BRANCH>" `
  --title "<type>(<scope>): <subject>" `
  --description "<pr-description>" `
  --output json
```

Extract the PR URL from the `url` field in the JSON response.

**If a PR already exists** for the branch (command fails with a conflict error), retrieve it instead:

```powershell
az repos pr list `
  --org "https://dev.azure.com/tfsportima" `
  --project "<ADO_PROJECT>" `
  --repository "<ADO_REPO>" `
  --source-branch "<feature-branch>" `
  --output json
```

Use the existing PR's `url`. Do not attempt to create a duplicate.

### Step 5 — Trigger the CI pipeline (default: off, optional)

**Skip this step by default.** Only run it if the user explicitly requests CI triggering — for example: "trigger CI", "run pipeline", "with CI", or "queue the pipeline".

If CI was not requested, set the CI run output to `"not triggered"` and continue to the output summary.

If CI was requested:

1. Discover available CI pipelines by scanning the `devops/` folder at the repo root:
   ```powershell
   Get-ChildItem -Path devops -Recurse -Filter "*.ci.yml" | Select-Object -ExpandProperty FullName
   ```
   Each file is named `<component>.ci.yml` under `devops/<component>/pipelines/`. The `<component>` name is the pipeline name.

2. Match changed file paths (from `git diff --name-only origin/<TARGET_BRANCH>...HEAD`) against the `paths.include` entries in each `.ci.yml`. Pick the component whose includes cover the most changed files.

3. If a single component matches clearly, use it. If multiple match or the match is ambiguous, ask the user which pipeline(s) to trigger before running.

4. Queue the pipeline against the feature branch:
   ```powershell
   az pipelines run `
     --org "https://dev.azure.com/tfsportima" `
     --project "<ADO_PROJECT>" `
     --name "<component>.ci" `
     --branch "<feature-branch>" `
     --output json
   ```

5. Capture the pipeline run URL from the output.

---

## Output summary

```
Shipped to Azure DevOps.

Branch:      <feature-branch>
PR:          <PR URL>
CI run:      <pipeline-run-URL or "not triggered">
```

Once CI results are available, use the `swebifix` skill to address any review comments or quality gate failures.

---

## Error handling

| Error | Action |
|-------|--------|
| `az devops` not installed | STOP — `az extension add --name azure-devops` |
| Not authenticated | STOP — `az devops login` or set `AZURE_DEVOPS_EXT_PAT` |
| On `dev` or `main` branch | STOP — ask for explicit confirmation before pushing |
| Push rejected (non-fast-forward) | STOP — tell user to `git pull --rebase` |
| PR already exists | Retrieve existing PR URL, skip creation, continue |
| Pipeline name unknown | Ask user before running |

---

## Guardrails

- **Never force push** (`--force`) to any branch.
- **Never push directly to `dev` or `main`** without explicit user consent.
- **Always confirm the commit message** before executing.
- **Never expose PATs, tokens, or secrets** in output, commits, or docs.
- Do not add binary files or generated artefacts (`.dll`, `bin/`, `obj/`) to commits — confirm `.gitignore` covers them if unexpected files appear in `git status`.