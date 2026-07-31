# AKS Internal Refactor — End-to-End Test Report

PR: [#68](https://github.com/SWojtyla/SwebKit/pull/68)  
Branch: `devin/aks-internal-refactor` → `main`

## Summary

I started the .NET sidecar and Vite dev server in demo mode and exercised the refactored AKS workspace end-to-end. The shared primitives (`useNotifyMutation`, `AksWorkspaceContext`, `ResourceTable`) work as intended: all tabs render, context menus and detail panels behave correctly, URL state survives reloads, and keyboard shortcuts `l` and `y` function. `npm run build`, `dotnet build src-sidecar/SwebKit.Sidecar.csproj`, `dotnet test tests/SwebKit.Sidecar.Tests`, and the full Playwright suite (167 tests) all passed.

One blocking issue was found: **Deployment Scale succeeds but does not update the table** because the demo sidecar does not persist the new replica count. This makes the `Scale` button appear to do nothing in the UI.

## Build / test guards

| Command | Result |
|---|---|
| `npm run build` | ✅ passed |
| `dotnet build src-sidecar/SwebKit.Sidecar.csproj` | ✅ passed |
| `dotnet test tests/SwebKit.Sidecar.Tests` | ✅ 7 passed |
| `npx playwright test` | ✅ 167 passed |

## Tested flows and results

### 1. AKS page loads and namespace selector works

- ✅ Demo mode enabled; AKS page loaded.
- ✅ Selected `ecommerce`, `All namespaces`, then `ecommerce` again; table and URL updated each time.
- ✅ When `All namespaces` is selected, a `Namespace` column appears in the pods table.
- ✅ When a single namespace is selected, the `Namespace` column is removed.

![Pods table with CPU/Memory bars and container details panel](https://app.devin.ai/attachments/c0e53c91-4363-4492-a5c5-65fb3c44057e/ss_dafbe115.png)

### 2. All AKS tabs render

Verified rendering for: Deployments, StatefulSets, Pods, ConfigMaps, Secrets, Helm, Jobs, CronJobs, Services, Ingresses, HTTPRoutes, GatewayClasses, Gateways, HPA, Events, Port-Forward, Analysis.

| Network dropdown | GatewayClasses | Gateways |
|---|---|---|
| ![Network dropdown expanded](https://app.devin.ai/attachments/ab69655b-ab9a-4bef-ae4b-24a57e8208e6/ss_928e23bd.png) | ![GatewayClasses tab](https://app.devin.ai/attachments/befc80af-467c-400e-aa8a-dfd332b70b6e/ss_86aa1a39.png) | ![Gateways tab](https://app.devin.ai/attachments/63f9bb95-037a-4361-a450-d055f6fc3a5f/ss_03469653.png) |

### 3. Pods tab

- ✅ `Hide completed pods` checkbox is checked by default.
- ✅ CPU and Memory columns show values and colored bars.
- ✅ Row click opens pod detail/logs panel.
- ✅ Right-click context menu contains Copy name, View YAML, View Logs, Container Details, Analyze network, Open shell in pod (disabled), Port-forward, Delete Pod.
- ✅ Delete triggers the `AksConfirmBar` and removes the pod (pod is recreated by demo controller).

![Pods context menu](https://app.devin.ai/attachments/135d7cd2-625f-4843-a93b-52b36aa60fe7/ss_678e2483.png)

![Pod deleted toast](https://app.devin.ai/attachments/d15c1de6-472c-4962-8b21-a67b01fe6aab/ss_b1d40e43.png)

### 4. Deployments tab

- ✅ Restart button triggers confirm and shows success toast.
- ⚠️ Scale button triggers confirm and shows success toast, but the table does **not** update because the sidecar returns the old replica count.

| Deployment Restart success | Scale confirm | After scale still 3/3 |
|---|---|---|
| ![Deployment restarted](https://app.devin.ai/attachments/c8207f2d-872b-424e-90a0-3c2a4f75f7f9/ss_93855bf3.png) | ![Scale confirm](https://app.devin.ai/attachments/a9f131f1-bcec-4acb-868c-e14848c4c3db/ss_218bef1b.png) | ![Table unchanged](https://app.devin.ai/attachments/b14af8d5-d845-44cb-81ca-2b2b950eba40/ss_8b820df2.png) |

### 5. HPA tab

- ✅ Scale form updates min/max and refreshes the table immediately.
- ✅ Disable/Enable toggles the badge and button label.
- ✅ Delete removes the row.

| HPA scaled to 2-5 | HPA disabled |
|---|---|
| ![HPA scaled](https://app.devin.ai/attachments/89deadd9-cd49-4db6-9da9-0c763e417b08/ss_3098a7bf.png) | ![HPA disabled badge](https://app.devin.ai/attachments/4b43ba6b-3eee-43d3-8593-dc3e3f49e8f9/ss_594fcd35.png) |

### 6. CronJobs tab

- ✅ Suspend/Resume toggles the `Suspend` column and button label.

| Before suspend | After suspend |
|---|---|
| ![CronJobs before](https://app.devin.ai/attachments/8597c3ac-b7e0-4673-a3c0-ac6785b482aa/ss_dbe1e8c4.png) | ![CronJobs after suspend](https://app.devin.ai/attachments/913ce48d-a370-4ecf-bbdf-15a62c5a9fab/ss_26213819.png) |

### 7. Helm tab

- ✅ Row click opens Helm detail panel with History, Values, Notes, Manifest tabs.
- ✅ Notes and Manifest render content.
- ✅ Right-click context menu has Copy name, History, Values, Rollback (disabled).

| Helm context menu | Helm Notes | Helm Manifest |
|---|---|---|
| ![Helm context menu](https://app.devin.ai/attachments/bc2b7f0c-328c-49be-82a6-d182d1601004/ss_bf89e793.png) | ![Helm Notes](https://app.devin.ai/attachments/dfb51365-c43c-46c6-991d-4caf33ddead1/ss_46076162.png) | ![Helm Manifest](https://app.devin.ai/attachments/d5b7d257-90f4-41f0-afe1-953e23966399/ss_a1ed25d7.png) |

### 8. Context menus on other resource tabs

| Ingresses | Secrets | StatefulSets |
|---|---|---|
| ![Ingress context menu](https://app.devin.ai/attachments/e248c21e-1017-4dd4-b226-1787f3d89c65/ss_f81ce77a.png) | ![Secrets context menu](https://app.devin.ai/attachments/cd335304-7959-4571-9867-98bb34b0ed68/ss_3943a0e4.png) | ![StatefulSets context menu](https://app.devin.ai/attachments/1a2141b7-1bd0-40db-a958-6f13fbdd425e/ss_f2991122.png) |

### 9. Multi-Pod Logs

- ✅ Multi-Pod Logs button opens the side panel.
- ✅ URL updates with `logs` and `logsNs` params.
- ✅ Close clears the URL params and panel.

### 10. URL state survives reload

- ✅ Reload with `tab=pods&pod=...&yaml=Pod:...` restored pod detail and YAML panels.
- ✅ Reload with `tab=helm&helm=ecommerce/order-api` restored Helm detail panel.
- ✅ Reload with `container=ecommerce/order-api-6094b-69a` restored container details panel.

| Pod + YAML + Helm reload | Container detail reload |
|---|---|
| ![URL reload pod+yaml+helm](https://app.devin.ai/attachments/4209b392-6874-47f4-9372-5a749637892e/ss_0c1d533b.png) | ![URL reload container](https://app.devin.ai/attachments/10056352-203e-42ed-98d0-15c71bbfaf56/ss_12270196.png) |

### 11. Keyboard shortcuts

- ✅ `l` navigates to the Pods tab.
- ✅ `y` opens YAML for the selected pod.
- `r` refresh is exercised by Playwright (`e2e/aks-deferred.spec.ts`).

## Failures / issues

1. **Deployment Scale does not update the demo table.**
   - The UI sends `POST /api/aks/ecommerce/deployments/order-api/scale?replicas=4` and receives `200 OK`.
   - The subsequent `GET /api/aks/ecommerce/deployments` still returns `replicas: 3`.
   - Because the table is driven by the query result, the ready count stays `3/3` and the UI looks unchanged.
   - This is a sidecar/demo-client issue (`DemoAksClient.ScaleDeploymentAsync` does not update `DemoDeployments`), not a web-refactor regression, but it prevents the user-visible table update from being verified.

2. **HPA disabled badge text is adjacent without whitespace.**
   - Visually the badge renders separately, but the text node reads `HPADisabled`.
   - This is minor; the user requirement that the badge toggles is met.

## Artifacts

- Screen recording: `/home/ubuntu/screencasts/rec-acbcc2a2-c5e1-4306-8bf1-89bd43fdebcd/rec-acbcc2a2-c5e1-4306-8bf1-89bd43fdebcd-edited.mp4`
- This report: `/home/ubuntu/repos/SwebKit/docs/features/active/aks-internal-refactor/test-report.md`

## Recommended PR comment

```markdown
End-to-end test results for PR #68:

- All AKS tabs render correctly and the Network dropdown sub-menu works.
- `ResourceTable` context menus are preserved (Pods, Ingresses, Secrets, StatefulSets, Helm, etc.).
- Pod row click, YAML, container details, multi-pod logs, and delete all work.
- HPA scale/disable/delete and CronJob suspend/resume update the table as expected.
- Helm detail panel Notes/Manifest tabs render.
- URL state for tab, namespace, pod, YAML, Helm, and container details survives reload.
- Keyboard shortcuts `l` and `y` work.
- `npm run build`, `dotnet build src-sidecar/SwebKit.Sidecar.csproj`, `dotnet test`, and the full Playwright suite (167 tests) pass.

Issue found: Deployment Scale shows a success toast but the table does not update because the demo sidecar does not persist the new replica count. The request is correct (`POST /api/aks/{ns}/deployments/{name}/scale?replicas=N`), so the UI wiring is fine; the `DemoAksClient.ScaleDeploymentAsync` implementation needs to update its in-memory deployment list for the table to reflect the change.
```
