# Technical Plan — Service Bus Download, Counts, and Parity

This plan lists the symbols to modify in control/data-flow order. No code changes should be made until the user explicitly approves this plan.

## Legend

- `BE` = backend sidecar / .NET core / MAUI Blazor
- `FE` = React/Tauri frontend
- `*` = new symbol

---

## 1. Message counts must match the global view

### 1.1 Mutation invalidation (React)

`web/src/lib/hooks.ts`

Every Service Bus mutation hook must invalidate the entity-stats and entity-list queries so the Active/DLQ tab counts and the entity-tree counts refresh together.

```ts
// After each mutation in:
// useSbSendMessage, useSbBatchSend, useSbScheduleMessage,
// useSbCompleteMessages, useSbCompleteDlq, useSbResubmitDlq, useSbPurgeMessages

onSuccess: (_data, vars) => {
  qc.invalidateQueries({ queryKey: ["sb-peek", vars.nsId, vars.entityPath] });
  qc.invalidateQueries({ queryKey: ["sb-dlq", vars.nsId, vars.entityPath] });
  qc.invalidateQueries({ queryKey: ["sb-entity-stats", vars.nsId, vars.entityPath] });
  qc.invalidateQueries({ queryKey: ["sb-queues", vars.nsId] });
  qc.invalidateQueries({ queryKey: ["sb-topics", vars.nsId] });
  // topic/subscription lists need a prefix invalidation because the
  // mutation only knows the entityPath, not the expanded topic name
  qc.invalidateQueries({ queryKey: ["sb-subs", vars.nsId] });
};
```

### 1.2 Message-list status footer

`web/src/components/service-bus/MessageList.tsx` — new props

```ts
interface Props {
  nsId: string | null;
  entity: SbEntityInfo | null;
  viewMode: "active" | "dlq";
  messages: SbMessage[];
  isLoading: boolean;
  isLoadingMore: boolean;
  canLoadMore: boolean;
  totalAvailable: number | null;
  selectedMessage: SbMessage | null;
  onSelectMessage: (message: SbMessage) => void;
  onLoadMore: () => void;
}
```

`MessageList` no longer fetches its own messages. It renders the `messages` prop, applies the local text/advanced filters, and prints:

```tsx
<span data-testid="message-filter-count">
  {totalAvailable != null
    ? `Showing ${filteredMessages.length} of ${totalAvailable} message(s)`
    : `Showing ${filteredMessages.length} message(s)`}
  {isLoadingMore && " · Loading more…"}
</span>
```

`web/src/components/service-bus/ServiceBusPage.tsx` computes `totalAvailable` from `selectedEntity.stats` and passes it down:

```ts
const totalAvailable = selectedEntity?.stats
  ? (viewMode === "active"
      ? selectedEntity.stats.activeMessageCount
      : selectedEntity.stats.deadLetterMessageCount)
  : null;
```

### 1.3 Entity-tree count display

`web/src/components/service-bus/EntityTree.tsx`

Change `EntityStatsBadges` so a `null` `stats` renders a loading dot instead of `–`, while `0` still renders `–`:

```tsx
if (entity.isTopic) return <span>–</span>;
if (!entity.stats) return <span className="sbg-loading-dot">·</span>;
```

This makes it visible when counts have not been fetched yet, matching MAUI behavior.

---

## 2. Load more / scroll for large Tauri lists

### 2.1 Lift the message window to `ServiceBusPage`

`web/src/components/service-bus/ServiceBusPage.tsx`

Replace the local `messages` use of `useSbPeekMessages`/`useSbPeekDlq` with a lifted `messageWindow` state:

```ts
const activeQuery = useSbPeekMessages(
  viewMode === "active" ? selectedNsId : null,
  selectedEntity?.entityPath ?? null,
  prefs.peekCount,
);
const dlqQuery = useSbPeekDlq(
  viewMode === "dlq" ? selectedNsId : null,
  selectedEntity?.entityPath ?? null,
  prefs.peekCount,
);
const peekData = viewMode === "active" ? activeQuery.data : dlqQuery.data;

const [messageWindow, setMessageWindow] = useState<SbMessage[]>([]);
const [lastSeq, setLastSeq] = useState<number | null>(null);
const [isLoadingMore, setIsLoadingMore] = useState(false);

useEffect(() => {
  if (peekData) {
    setMessageWindow(peekData);
    setLastSeq(maxSequenceNumber(peekData));
  }
}, [peekData]);

const loadMore = async () => {
  if (!selectedNsId || !selectedEntity || lastSeq == null || isLoadingMore) return;
  setIsLoadingMore(true);
  try {
    const mode = viewMode === "active" ? "peek" : "dlq";
    const next = await apiFetch<SbMessage[]>(
      `/api/servicebus/${selectedNsId}/entities/${encodeURIComponent(selectedEntity.entityPath)}/${mode}?count=${prefs.peekCount}&fromSeq=${lastSeq + 1}`,
    );
    setMessageWindow((prev) => mergeUniqueByKey(prev, next));
    setLastSeq((prev) => Math.max(prev ?? 0, maxSequenceNumber(next)));
  } finally {
    setIsLoadingMore(false);
  }
};

const canLoadMore =
  (totalAvailable != null && messageWindow.length < totalAvailable) ||
  (peekData ? peekData.length === prefs.peekCount : false);
```

The `selectedMessage` lookup now searches `messageWindow` instead of the first-page query data, so a message loaded by "Load more" can still be selected from the detail pane.

### 2.2 `MessageList` triggers load more on scroll

`web/src/components/service-bus/MessageList.tsx`

Add a sentinel div at the bottom of the message table and an `IntersectionObserver`:

```tsx
<div ref={sentinelRef} className="h-1" data-testid="message-load-sentinel" />
```

```ts
const sentinelRef = useRef<HTMLDivElement>(null);
useEffect(() => {
  if (!sentinelRef.current || !canLoadMore) return;
  const observer = new IntersectionObserver(
    ([entry]) => {
      if (entry.isIntersecting && !isLoadingMore) onLoadMore();
    },
    { root: listRef.current, threshold: 0 },
  );
  observer.observe(sentinelRef.current);
  return () => observer.disconnect();
}, [canLoadMore, isLoadingMore, onLoadMore]);
```

Also render an explicit button:

```tsx
<button
  data-testid="load-more-button"
  onClick={onLoadMore}
  disabled={!canLoadMore || isLoadingMore}
>
  {isLoadingMore ? "Loading…" : canLoadMore ? `Load more (+${prefs.peekCount})` : "All loaded"}
</button>
```

The list container keeps `flex-1 overflow-auto`; add `min-h-0` to the table wrapper if needed to guarantee the flex item shrinks and scrolls.

---

## 3. Download a single Service Bus message

### 3.1 Shared download helper (React)

`* web/src/lib/download.ts`

```ts
export function downloadText(filename: string, content: string, mimeType = "application/json"): void {
  downloadBlob(filename, new Blob([content], { type: mimeType }));
}

export function downloadBlob(filename: string, blob: Blob): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
```

### 3.2 ZIP helper using `fflate` (React)

`web/package.json`

Add runtime dependency:

```json
"fflate": "^0.8.3"
```

`* web/src/lib/zip.ts`

```ts
import { zip, strToU8 } from "fflate";

export function buildZip(files: Record<string, string>): Promise<Uint8Array> {
  const payload: Record<string, Uint8Array> = {};
  for (const [name, content] of Object.entries(files)) {
    payload[name] = strToU8(content);
  }
  return new Promise((resolve, reject) => {
    zip(payload, { level: 1 }, (err, data) => {
      if (err) reject(err);
      else resolve(data);
    });
  });
}
```

### 3.3 Single-message JSON payload helper

`web/src/components/service-bus/exportHelpers.ts` (*new, shared by `MessageList` and `MessageDetail`)

```ts
import type { SbMessage } from "@/lib/types";

export function messageToDownloadObject(message: SbMessage): unknown {
  return {
    messageId: message.messageId,
    correlationId: message.correlationId,
    subject: message.subject,
    contentType: message.contentType,
    body: message.body,
    applicationProperties: message.applicationProperties,
    systemProperties: message.systemProperties,
    enqueuedAt: message.enqueuedAt,
    deliveryCount: message.deliveryCount,
    sequenceNumber: message.sequenceNumber,
    sessionId: message.sessionId,
    deadLetterReason: message.deadLetterReason,
    deadLetterErrorDescription: message.deadLetterErrorDescription,
  };
}

export function safeFileName(name: string, maxLength = 80): string {
  return name.replace(/[^a-zA-Z0-9_-]/g, "_").slice(0, maxLength);
}
```

### 3.4 React message detail — single download

`web/src/components/service-bus/MessageDetail.tsx`

Add to the action row:

```tsx
import { Download } from "lucide-react";
import { downloadText } from "@/lib/download";
import { buildZip } from "@/lib/zip";
import { useNotification } from "@/components/layout/NotificationSystem";
import { messageToDownloadObject, safeFileName } from "./exportHelpers";

const { notify } = useNotification();

const baseName = `message-${safeFileName(message.messageId)}${message.sequenceNumber != null ? `-${message.sequenceNumber}` : ""}`;

const downloadJson = () => {
  downloadText(`${baseName}.json`, JSON.stringify(messageToDownloadObject(message), null, 2));
  notify("success", "Message downloaded as JSON");
};

const downloadZip = async () => {
  const files = { [`${baseName}.json`]: JSON.stringify(messageToDownloadObject(message), null, 2) };
  const zipped = await buildZip(files);
  downloadBlob(`${baseName}.zip`, new Blob([zipped], { type: "application/zip" }));
  notify("success", "Message downloaded as ZIP");
};
```

Render two small buttons next to the copy buttons:

```tsx
<button data-testid="message-download-json" onClick={downloadJson} title="Download message as JSON">
  <Download className="h-3 w-3" /> JSON
</button>
<button data-testid="message-download-zip" onClick={downloadZip} title="Download message as ZIP">
  <Download className="h-3 w-3" /> ZIP
</button>
```

### 3.5 MAUI message detail — single download

`src/SwebKit.App/Components/ServiceBus/MessageDetailPane.razor`

The component already injects `IJSRuntime JS` and `INotificationService Notifications`. Add two methods and two buttons in the action row.

```csharp
@using System.IO.Compression
@using System.Text.Json

private async Task DownloadMessageJsonAsync()
{
    if (Message is null) return;
    var json = JsonSerializer.Serialize(CreateDownloadPayload(Message), new JsonSerializerOptions { WriteIndented = true });
    var name = $"message-{SafeFileName(Message.MessageId)}.json";
    await JS.InvokeVoidAsync("SwebKit.downloadText", name, "application/json", json);
    Notifications.ShowSuccess("Message downloaded");
}

private async Task DownloadMessageZipAsync()
{
    if (Message is null) return;
    var base64 = BuildZipBase64([Message]);
    var name = $"message-{SafeFileName(Message.MessageId)}.zip";
    await JS.InvokeVoidAsync("SwebKitUi.downloadBinaryFile", name, base64, "application/zip");
    Notifications.ShowSuccess("Message downloaded as ZIP");
}

private static object CreateDownloadPayload(SbMessage m) => new
{
    m.MessageId,
    m.CorrelationId,
    m.Subject,
    m.ContentType,
    m.Body,
    m.ApplicationProperties,
    m.SystemProperties,
    m.EnqueuedAt,
    m.DeliveryCount,
    m.SequenceNumber,
    m.SessionId,
    m.DeadLetterReason,
    m.DeadLetterErrorDescription
};

private static string SafeFileName(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return "unknown";
    var safe = new string(value.Where(char.IsLetterOrDigit).ToArray());
    return string.IsNullOrEmpty(safe) ? "unknown" : safe[..Math.Min(safe.Length, 80)];
}

private static string BuildZipBase64(IReadOnlyList<SbMessage> messages)
{
    using var ms = new MemoryStream();
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
    {
        for (int i = 0; i < messages.Count; i++)
        {
            var m = messages[i];
            var entryName = $"message-{i + 1:000}-{SafeFileName(m.MessageId)}.json";
            var entry = zip.CreateEntry(entryName);
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write(JsonSerializer.Serialize(CreateDownloadPayload(m), new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    return Convert.ToBase64String(ms.ToArray());
}
```

Add the buttons next to the existing `Copy Full Message` / `Save as Template` buttons:

```razor
<AppButton OnClick="DownloadMessageJsonAsync" CssClass="mdp-btn" Size="Small" Variant="Secondary" Title="Download message as JSON">
    JSON
</AppButton>
<AppButton OnClick="DownloadMessageZipAsync" CssClass="mdp-btn" Size="Small" Variant="Secondary" Title="Download message as ZIP">
    ZIP
</AppButton>
```

---

## 4. Download selected / filtered messages as a ZIP

### 4.1 React message-list download

`web/src/components/service-bus/MessageList.tsx`

Add to the toolbar a `Download` button group. If rows are selected, download those; otherwise download the currently filtered messages.

```ts
const messagesToDownload = selectedMsgs.size > 0
  ? filteredMessages.filter((m) => selectedMsgs.has(`${m.messageId}-${m.sequenceNumber}`))
  : filteredMessages;

const downloadZip = async () => {
  if (messagesToDownload.length === 0) return;
  const files: Record<string, string> = {};
  messagesToDownload.forEach((m, i) => {
    const seq = m.sequenceNumber != null ? `-${m.sequenceNumber}` : "";
    const name = `message-${String(i + 1).padStart(3, "0")}-${safeFileName(m.messageId)}${seq}.json`;
    files[name] = JSON.stringify(messageToDownloadObject(m), null, 2);
  });
  const zipped = await buildZip(files);
  const scope = selectedMsgs.size > 0 ? "selected" : "filtered";
  const fileName = `${safeFileName(entity?.name ?? "messages")}-${scope}-${new Date().toISOString().slice(0,19).replace(/[:T]/g, "-")}.zip`;
  downloadBlob(fileName, new Blob([zipped], { type: "application/zip" }));
  notify("success", `Downloaded ${messagesToDownload.length} message(s) as ZIP`);
};
```

Add a `Download ZIP` button next to the existing toolbar buttons:

```tsx
<button data-testid="message-download-zip" onClick={downloadZip} disabled={messagesToDownload.length === 0} title="Download selected or filtered messages as ZIP">
  <Download className="h-3.5 w-3.5" /> ZIP
</button>
```

### 4.2 MAUI message-list download

`src/SwebKit.App/Components/ServiceBus/MessageListView.razor`

The existing `ExportFilteredJsonAsync` exports the filtered list as JSON. Add a ZIP export next to it.

Add `@inject INotificationService Notifications` to the existing `@inject` block, plus `@using System.IO.Compression` and `@using System.Text.Json`.

```csharp
private async Task ExportFilteredZipAsync()
{
    var messages = _selection.Any
        ? Messages.Where(m => _selection.IsSelected(m.MessageId)).ToList()
        : FilteredMessages;
    if (messages.Count == 0) return;

    var base64 = BuildZipBase64(messages);
    var entitySlug = (EntityPath ?? "messages").Replace('/', '-');
    var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    var scope = _selection.Any ? "selected" : "filtered";
    var fileName = $"{entitySlug}-{scope}-{ts}.zip";
    await JS.InvokeVoidAsync("SwebKitUi.downloadBinaryFile", fileName, base64, "application/zip");
    Notifications.ShowSuccess($"Downloaded {messages.Count} message(s) as ZIP");
}
```

Add a toolbar button next to the existing `Export JSON` button:

```razor
<AppButton OnClick="ExportFilteredZipAsync" Size="Small" Variant="Secondary"
           CssClass="message-list-view__toolbar-button"
           Disabled="@(IsLoading || IsMutating || FilteredMessages.Count == 0)"
           Title="Download selected or filtered messages as a ZIP">
    ⬇ ZIP
</AppButton>
```

Reuse `BuildZipBase64` and `CreateDownloadPayload` helpers defined in section 3.5.

---

## 5. Parity audit

`* docs/features/active/service-bus-download-parity/parity.md` (optional, can live in `technical-plan.md`)

A short table to be filled during implementation:

| Feature | MAUI/Blazor | Tauri/React | Notes |
|---|---|---|---|
| Namespace selector | yes | yes |  |
| Entity tree with active/DLQ/scheduled counts | yes | yes | Needs invalidation fix (§1.1) |
| Peek count / auto-refresh / density | yes | yes |  |
| Column chooser + custom property columns | yes | yes |  |
| Text filter + saved filters + advanced rules | yes | yes |  |
| Multi-select + bulk complete/resubmit/delete | yes (active deletes; DLQ resubmits) | yes | Tauri missing delete-filtered/purge-all in list toolbar |
| Compose / send / schedule | yes | yes |  |
| Batch send / batch replay | yes | yes |  |
| Load more + total count | yes | no | Implemented in §2 |
| Download single message (JSON/ZIP) | copy only | copy only | Implemented in §3 |
| Download selected/filtered as ZIP | no | no | Implemented in §4 |
| Message detail: trace-pivot tab | yes | no | Out of scope |
| Message detail: filter by session | yes | no | Out of scope |
| Message detail: investigate | yes | no | Out of scope (Incident Timeline) |

The parity table is recorded so remaining gaps can be triaged separately.

---

## 6. Control / data flow summary

1. `ServiceBusPage` fetches the first page of messages (`useSbPeekMessages`/`useSbPeekDlq`) and the entity stats (`useSbEntityStats`).
2. After mutations, all Service Bus query keys are invalidated together (§1.1), so the entity tree, the Active/DLQ tab counts, and the message list show the same numbers.
3. `MessageList` receives the lifted `messageWindow` and `totalAvailable`, filters locally, and renders the footer count.
4. When the user scrolls to the sentinel or clicks `Load more`, `ServiceBusPage` calls `apiFetch` with the next `fromSequenceNumber`, appends the new batch to `messageWindow`, and updates `lastSeq`.
5. `MessageDetail` and `MessageList` serialize the selected/filtered messages via `messageToDownloadObject` and produce JSON or ZIP downloads; `fflate` is used in the browser, `System.IO.Compression` is used in MAUI.
