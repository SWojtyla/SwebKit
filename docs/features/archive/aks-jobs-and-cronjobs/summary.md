# Archive Summary - aks-jobs-and-cronjobs

Feature archived on 2026-04-10.

---

title: "Archive Summary - aks-jobs-and-cronjobs"
owner: ""
completed_date: "2026-04-10"
pr: ""
commit: ""

---

## Goal

Extend the existing AKS page so operators can browse Kubernetes Jobs, inspect Job and CronJob YAML, manually trigger CronJobs, and rerun existing Jobs without leaving SwebKit.

## Delivered

- Jobs became a first-class AKS resource tab in the existing page, with browse, filter, selection, namespace-click navigation, and YAML access.
- CronJobs remained visible in single-namespace and all-namespaces mode, including default namespace entries.
- `Run now` for CronJobs and `Rerun job` for Jobs create new Jobs without mutating the source resource.
- Batch actions and YAML routing use the selected row namespace rather than the namespace selector.
- Shared AKS contracts were extended with `GetJobsAsync`, `TriggerCronJobAsync`, `RerunJobAsync`, `JobInfo`, and `AksBatchAnnotations`.
- `KubernetesAksClient` and `DemoAksClient` both support Jobs browse, batch YAML, trigger flows, rerun flows, and source provenance.
- Targeted UI, demo, and Kubernetes tests were added for row-scoped actions, default-namespace visibility, cancellation behavior, YAML parity, and clone sanitization.

## Key decisions

- Keep the feature inside `AksPage.razor` and the existing `IAksClient` abstraction instead of creating a new page or batch-specific service layer.
- Treat `Run now` and `Rerun job` as create-new-Job operations rather than mutations of existing CronJobs or Jobs.
- Keep Jobs and CronJobs visible in all-namespaces mode and always scope actions to the selected row namespace.
- Use minimal `swebkit.io/source-kind` and `swebkit.io/source-name` annotations to preserve trigger provenance when owner references are not sufficient.
- Keep app-level AKS tests on the existing curated source-link model instead of adding a direct project reference to `SwebKit.App`.

## Validation Performed

- `tests/SwebKit.App.Tests/AksConnectionBarTests.cs`: 5 passed, 0 failed.
- `tests/SwebKit.App.Tests/AksPageBatchTests.cs`: 9 passed, 0 failed.
- `tests/SwebKit.Core.Tests/DemoAksClientTests.cs`: 40 passed, 0 failed.
- `tests/SwebKit.Kubernetes.Tests/KubernetesAksClientTests.cs`: 42 passed, 0 failed.
- Combined targeted `runTests` validation: 96 passed, 0 failed.
- `dotnet build src/SwebKit.App/SwebKit.App.csproj -c Debug -p:Platform=x64`: passed.

## Lessons Learned

- All-namespaces batch workflows must carry the row namespace end-to-end; even small namespace filtering heuristics can hide real Jobs or CronJobs.
- Cancellation semantics need to be preserved through the UI layer, otherwise user-cancelled cluster actions show up as false failure notifications.
- Demo-mode namespace lists need to mirror real feature expectations closely, or UI regressions can be masked by test data shape rather than code correctness.

## Follow-up

- Live-cluster manual smoke validation for create permissions, RBAC-denied behavior, and post-create eventual consistency was not recorded at archive time. Owner: next AKS validation or follow-up feature.
- If operators want stronger confirmation after trigger actions, a future enhancement could optionally switch to the created Job and select it automatically.

## Archive Metadata

- Archived from `docs/features/active/aks-jobs-and-cronjobs/` on 2026-04-10 at user request.
- Jira: not linked.
- Main code areas: `src/SwebKit.App/Components/Pages/AksPage.razor`, `src/SwebKit.Core/Abstractions/IAksClient.cs`, `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`, `src/SwebKit.Core/Services/DemoAksClient.cs`.
