# Decisions — Application Insights Viewer

---

## D-1 — New `SwebKit.Observability` project, not extending `SwebKit.Azure`

**Chosen:** Create a separate `SwebKit.Observability` class library.

**Rejected:** Adding observability code into `SwebKit.Azure`.

**Why:** `SwebKit.Azure` is already responsible for Service Bus and Storage. Application Insights is conceptually distinct (query/read-heavy analytics, not messaging), uses different Azure SDK packages (`Azure.Monitor.Query` rather than `Azure.Messaging.*`), and may grow to support non-Azure observability backends (OTLP, Elastic, Datadog) in the future. Keeping it separate avoids bloating `SwebKit.Azure` and makes the boundary clear.

---

## D-2 — `DefaultAzureCredential` only; no connection string or API key option

**Chosen:** Authenticate exclusively via `DefaultAzureCredential` (Azure CLI, Managed Identity, VS credential chain).

**Rejected:** Supporting legacy API keys or connection strings for App Insights.

**Why:** API keys for App Insights are deprecated by Microsoft and do not support the Azure Monitor Logs API. `DefaultAzureCredential` works for all current Azure authentication scenarios and aligns with the AKS feature's approach. Reduces credential management complexity for the user.

**Note:** Surface the resolved identity (e.g. `az account show` output) somewhere in the UI so users can verify which account is active.

---

## D-3 — Query results capped at 500 rows by default

**Chosen:** Enforce a configurable max-row limit (default 500) in `IAppInsightsClient.RunKqlAsync`.

**Rejected:** Returning unlimited rows.

**Why:** Azure Monitor charges per GB queried and large result sets can freeze the Blazor UI. The limit is user-adjustable in settings (min 100, max 5000) and a truncation warning is shown in the UI so users know they may not be seeing all results.

---

## D-4 — Resource discovery scans all subscriptions the credential has access to

**Chosen:** Enumerate all subscriptions via `ArmClient.GetSubscriptions()` and scan each for App Insights components.

**Rejected:** Requiring the user to manually enter a subscription ID or resource ID.

**Why:** Developers often have many subscriptions (dev, staging, prod, partner). Manual entry is error-prone and friction-heavy. The discovery UX shows a progress indicator so the latency is transparent. Results are cached in-memory for the session to avoid repeated scans.

---

## D-5 — No live log stream in MVP

**Chosen:** Polling-based "near real-time" not included in MVP. Logs tab queries on demand only.

**Rejected:** Adding a polling loop to simulate live tailing.

**Why:** Azure Monitor Logs API has a data latency of 1–5 minutes, making a "live" stream misleading. A polling approach at any sensible interval would either be too slow to feel live or too fast and expensive (billed queries). This can be revisited if a streaming API becomes available or if a coarser near-live feed (5-minute poll) proves useful in practice.

---

## D-6 — KQL presets use a `let` variable substitution pattern for time range

**Chosen:** All built-in presets start with `let _range = ...;` which is substituted at runtime before sending to Azure Monitor.

**Rejected:** Appending `| where timestamp between (start .. end)` at the end of user KQL.

**Why:** Appending a filter at the end can conflict with summarize/project steps that drop the timestamp column. The `let` substitution pattern is more predictable, works correctly with aggregation queries, and mirrors what Azure Monitor itself does internally.
