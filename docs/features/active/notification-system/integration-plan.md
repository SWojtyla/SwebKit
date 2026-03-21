# Notification Integration Plan

Track the wiring of `INotificationService` calls into each feature page.

---

## Status key

- ✅ Done
- 🔲 Pending

---

## Redis (`RedisPage.razor`) ✅ Complete

| Action                    | Severity | Message                                           |
| ------------------------- | -------- | ------------------------------------------------- |
| Key deleted               | Success  | "Key deleted" + key name                          |
| Database purged           | Warning  | "Database purged" + "All keys have been removed." |
| TTL updated               | Success  | "TTL updated" + `{n}s on '{key}'`                 |
| TTL removed               | Success  | "TTL removed" + expiry note                       |
| String value saved        | Success  | "Value saved" + key name                          |
| Hash field saved          | Success  | "Field saved" + field + key                       |
| TTL / value / hash errors | Error    | descriptive + exception message                   |

---

## Service Bus (`ServiceBusPage.razor` → `MessageComposer.razor`) ✅ Complete

Inject: `@inject INotificationService Notifications`

| Action                      | Method          | Severity | Suggested message              |
| --------------------------- | --------------- | -------- | ------------------------------ |
| Message sent                | send single     | Success  | "Message sent" + entity path   |
| Batch sent                  | send batch      | Success  | "Batch sent" + count + entity  |
| Message scheduled           | schedule        | Success  | "Message scheduled" + entity   |
| Scheduled message cancelled | cancel schedule | Success  | "Scheduled message cancelled"  |
| DLQ message resubmitted     | resubmit        | Success  | "Message resubmitted" + entity |
| Send / schedule errors      | catch blocks    | Error    | descriptive + ex               |

---

## AKS (`AksPage.razor`) ✅ Complete

Inject: `@inject INotificationService Notifications`

| Action               | Method             | Severity | Suggested message                       |
| -------------------- | ------------------ | -------- | --------------------------------------- |
| Deployment restarted | restart rollout    | Success  | "Deployment restarted" + name           |
| Pod deleted          | delete pod         | Success  | "Pod deleted" + name                    |
| Port-forward started | start port-forward | Success  | "Port-forward started" + `local:remote` |
| Port-forward stopped | stop port-forward  | Info     | "Port-forward stopped"                  |
| Errors               | catch blocks       | Error    | descriptive + ex                        |

---

## Storage (`BlobDetailPane.razor`, `StorageBlobList.razor`) ✅ Complete

Inject: `@inject INotificationService Notifications`

| Action          | Method       | Severity | Suggested message             |
| --------------- | ------------ | -------- | ----------------------------- |
| Blob downloaded | download     | Success  | "Blob downloaded" + filename  |
| SAS URL copied  | copy SAS     | Success  | "SAS URL copied to clipboard" |
| Errors          | catch blocks | Error    | descriptive + ex              |

---

## Releases (`ApprovalCenter.razor`, `PipelineTriggerHub.razor`) ✅ Complete

Inject: `@inject INotificationService Notifications`

| Action               | Method       | Severity | Suggested message                    |
| -------------------- | ------------ | -------- | ------------------------------------ |
| Approval submitted   | approve      | Success  | "Approval submitted" + release name  |
| Deployment triggered | trigger      | Success  | "Deployment triggered" + environment |
| Errors               | catch blocks | Error    | descriptive + ex                     |

---

## ErrorCallout migration (incremental) 🔲

Once all integrations are done, `ErrorCallout` usages that duplicate notification errors can be removed page by page. Non-error callouts (connection state) should be **kept** — they serve a different UX role (persistent inline context, not ephemeral toast).

Pages with `ErrorCallout`:

- `RedisPage.razor` — connection/scan errors (keep for now — different from write errors)
- `ServiceBusPage.razor`
- `AksPage.razor`
- `StoragePage.razor`
