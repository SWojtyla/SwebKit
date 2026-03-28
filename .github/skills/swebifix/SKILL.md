---
name: swebifix
description: 'Find the active Pull Request for the current branch, read all open review threads (SonarQube, reviewer comments), fix the reported issues in code, commit and push, then mark each thread as resolved. Use when: swebifix, fix PR comments, resolve review feedback, address review comments, fix pull request feedback, sonarqube findings, fix review, resolve threads, PR quality gate failures.'
---

# Swebifix

Read open review threads on the current branch's Pull Request (reviewer comments, SonarQube/quality-gate findings), fix the reported issues in code, commit and push the fixes, then resolve each thread.

> **Note:** This skill is meant to be triggered manually, after CI and quality tools have had time to run. It complements the `azure-devops` ship skill but is invoked separately.

---

## Repository constants

- **Organisation:** `https://dev.azure.com/tfsportima`

## Repository discovery

Run once at startup and cache:

```powershell
git remote get-url origin
```

Parse `<ADO_PROJECT>` and `<ADO_REPO>` from the URL:
- `https://<user>@dev.azure.com/tfsportima/<project>/_git/<repo>`
- `https://dev.azure.com/tfsportima/<project>/_git/<repo>`

URL-decode the project segment (`briocomp%20-%20Brio%20Compare` → `briocomp - Brio Compare`).

Capture the current branch:
```powershell
git branch --show-current
```

---

## Prerequisites

1. **Git** — `git --version`
2. **Azure CLI with DevOps extension** — `az devops --version`. Install via `az extension add --name azure-devops` if missing.
3. **Authentication** — `az devops login` or `AZURE_DEVOPS_EXT_PAT` set in environment.

---

## Procedure

### Step 1 — Find the PR for the current branch

```powershell
az repos pr list `
  --org "https://dev.azure.com/tfsportima" `
  --project "<ADO_PROJECT>" `
  --repository "<ADO_REPO>" `
  --source-branch "<current-branch>" `
  --output json
```

Extract the PR `pullRequestId` and `url` from the first result.

If no PR exists for the current branch, STOP and tell the user to create one first (use the `azure-devops` skill).

### Step 2 — Read open threads

Azure DevOps does not expose `az repos pr thread list` as a CLI command. Use `az devops invoke` to call the REST endpoint directly.

```powershell
az devops invoke `
  --org "https://dev.azure.com/tfsportima" `
  --area git `
  --resource pullRequestThreads `
  --route-parameters project="<ADO_PROJECT>" repositoryId="<ADO_REPO>" pullRequestId="<PR_ID>" `
  --http-method GET `
  --api-version "7.1" `
  --output json
```

From the JSON response, collect all threads where:
- `status` is `"active"` (not `"fixed"`, `"wontFix"`, `"closed"`, or `"pending"`)

For each active thread, extract:
- `id` — the thread ID (needed for resolution in Step 5)
- First comment's `content` — the review note text
- `threadContext.filePath` — the file path (if a code comment)
- `threadContext.rightFileStart.line` — the line number (if a code comment)

If there are **no active threads**, report that and stop — nothing to fix.

### Step 3 — Fix the reported issues

Work through the active threads one by one:

1. Read the comment text carefully.
2. Identify the file and line from `threadContext` (or from the comment body if it references a specific location).
3. Edit the code to address the finding.

**Common patterns:**

| Finding type | Approach |
|---|---|
| SonarQube CA* / IDE* code analysis | Apply the recommended fix (e.g., `ArgumentNullException.ThrowIfNull`, `string.IsNullOrEmpty`, etc.) |
| Missing null check | Add guard or use `ThrowIfNull` |
| Simplify expression | Apply the suggested simplification |
| Reviewer style / naming | Rename or restructure as requested |
| Test coverage gap | Add a test covering the missing scenario |
| Architecture or design concern | Read the feature docs and assess; if the fix is straightforward, apply it; if it requires significant rework, ask the user |

Always read the full surrounding context of every changed file before editing. Do not patch blindly.

After applying all fixes, run the project build to verify no compile errors were introduced:

```powershell
dotnet build <affected-project>.csproj --no-restore
```

If the build fails, fix the build errors before proceeding to commit.

### Step 4 — Commit the fixes

Compose a conventional commit message covering all fixes:

```
fix(<scope>): address PR review findings

<list each resolved thread briefly, one line each>
```

As always, confirm the message with the user before committing.

```powershell
git add -A
git commit -m "fix(<scope>): address PR review findings" -m "" -m "<summary>"
git push
```

If the push is rejected, STOP and tell the user to `git pull --rebase` first.

### Step 5 — Resolve each thread

For each active thread that was addressed, mark it as `fixed`:

1. Write the patch payload to a temp file:
   ```powershell
   '{"status": "fixed"}' | Set-Content "$env:TEMP\thread-patch.json" -Encoding utf8
   ```

2. Call the PATCH endpoint for each thread:
   ```powershell
   az devops invoke `
     --org "https://dev.azure.com/tfsportima" `
     --area git `
     --resource pullRequestThreads `
     --route-parameters project="<ADO_PROJECT>" repositoryId="<ADO_REPO>" pullRequestId="<PR_ID>" threadId="<THREAD_ID>" `
     --http-method PATCH `
     --in-file "$env:TEMP\thread-patch.json" `
     --api-version "7.1"
   ```

3. For threads that were **intentionally not fixed** (out-of-scope, won't fix, disagree), set status to `"wontFix"` and leave a reply explaining:
   ```powershell
   # First post a reply comment on the thread
   $reply = @{ content = "<your explanation>" } | ConvertTo-Json
   $reply | Set-Content "$env:TEMP\thread-comment.json" -Encoding utf8

   az devops invoke `
     --org "https://dev.azure.com/tfsportima" `
     --area git `
     --resource pullRequestThreadComments `
     --route-parameters project="<ADO_PROJECT>" repositoryId="<ADO_REPO>" pullRequestId="<PR_ID>" threadId="<THREAD_ID>" `
     --http-method POST `
     --in-file "$env:TEMP\thread-comment.json" `
     --api-version "7.1"

   # Then mark the thread
   '{"status": "wontFix"}' | Set-Content "$env:TEMP\thread-patch.json" -Encoding utf8

   az devops invoke `
     --org "https://dev.azure.com/tfsportima" `
     --area git `
     --resource pullRequestThreads `
     --route-parameters project="<ADO_PROJECT>" repositoryId="<ADO_REPO>" pullRequestId="<PR_ID>" threadId="<THREAD_ID>" `
     --http-method PATCH `
     --in-file "$env:TEMP\thread-patch.json" `
     --api-version "7.1"
   ```

### Step 6 — Output summary

```
PR review fix complete.

Branch:           <current-branch>
PR:               <PR URL>
Threads resolved: <count>
Threads wontFix:  <count>
Commit:           <short SHA>
```

---

## Error handling

| Error | Action |
|---|---|
| `az devops` not installed | STOP — `az extension add --name azure-devops` |
| Not authenticated | STOP — `az devops login` or set `AZURE_DEVOPS_EXT_PAT` |
| No PR found for branch | STOP — create the PR first with the `azure-devops` skill |
| Build fails after fix | Fix the build error before committing |
| Push rejected (non-fast-forward) | STOP — `git pull --rebase` then retry |
| Thread PATCH fails | Log the error, continue with remaining threads, report at the end |
| Review comment requires design rework | Ask the user — do not attempt large structural changes autonomously |

---

## Guardrails

- **Never force push** (`--force`) to any branch.
- **Always confirm the commit message** before executing.
- **Never set a thread to `wontFix` silently** — always post a reply explaining why first.
- **Never expose PATs, tokens, or secrets** in output, commits, or docs.
- If a finding is ambiguous or would require significant rework, ask the user before editing files.
