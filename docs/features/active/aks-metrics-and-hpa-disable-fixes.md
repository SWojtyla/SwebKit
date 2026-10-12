# AKS pod metric severity + HPA disable fixes

State: Review

## Goal

Fix two AKS bugs reported by the operator:

1. Pods tab CPU/Memory columns judge usage against fixed ceilings (500m / 512Mi),
   so a pod requesting 1Gi but using 500Mi renders red. Severity must relate to
   the pod's own declared limits/requests.
2. "Disable autoscaling" reports success but the HPA stays active. Root causes:
   - KEDA ownership detection only checks the `scaledobject.keda.sh/name` label;
     adopted HPAs (transfer-hpa-ownership) carry no label — only a
     `controller=true` ScaledObject ownerReference. The freeze patch on the
     generated HPA is then reverted by KEDA's reconcile loop while the
     `swebkit.io/scaling-disabled` annotation keeps the badge lit.
   - The freeze fallback `Math.Max(maxReplicas, 1)` when `currentReplicas == 0`
     scales the workload UP to max — the opposite of disabling.
   - Demo mode never freezes bounds, so a disabled demo HPA looks fully active.

## Scope

- `PodInfo` gains summed container requests/limits (cpu cores + memory bytes, nullable).
- `KubernetesAksClient.GetPodsAsync` populates them; `ParseMemoryToBytes` extended
  for Ti/Pi/Ei and decimal SI suffixes (requests like `1G` are valid quantities).
- `DemoAksClient` pods get plausible requests/limits for demo fidelity.
- `PodsTab` severity: usage vs limit (warn ≥70%, crit ≥90%), vs request
  (warn ≥100%, crit ≥150%), or the existing fixed ceilings when neither declared.
- `GetKedaScaledObjectName` also recognizes a `ScaledObject` ownerReference;
  `ApplyScalingMetadata` shares it so list + mutation paths agree.
- Freeze target: current → desired → max(min,1). Never above max.
- `DemoAksClient.SetHpaScalingEnabledAsync` stashes bounds and freezes at current
  replicas; enable restores them.

## Non-goals

- No HPA create/edit UI; no per-container metric breakdown changes.
- No change to `paused`/`paused-replicas` KEDA semantics — `paused: "true"` stays the switch.

## Tasks

- [x] Model + client mapping (`AksModels.PodInfo`, `GetPodsAsync`, quantity parsing)
- [x] PodsTab basis/severity rework (`web/src/lib/aksUsage.ts` + PodsTab)
- [x] KEDA ownerRef detection + freeze-target fix + demo freeze/restore
- [x] Tests: KubernetesAksClientTests (ownerRef, freeze target, memory parser),
      DemoAksClientTests (freeze/restore), `aksUsage.test.ts`, e2e HPA disable row
- [x] Validate: `web npm run build`, `dotnet build src-sidecar`, dotnet tests, scoped playwright

## Test plan

- Unit: ownerRef-only KEDA HPA detection; freeze target never exceeds maxReplicas;
  memory parser covers `1G`/`1Gi`/`512Mi`/`128974848`.
- Demo: disable → bounds collapse to `N–N` and Disabled pill; enable → original bounds back.
- e2e: hpa row → context menu "Disable autoscaling" → confirm → `Disabled` pill + `3–3` bounds.
- Pods tab in demo: `inventory-worker`/`search-indexer` show warning/red against their
  tighter limits while normal pods render green.
