---
status: Review
---

# Data-Fetch Performance: AKS, Service Bus and Redis

## Scope

SwebKit is used daily as the operator tool for AKS, Service Bus and Redis, and fetching data
felt slow in all three. The sharpest report: *"When filtering on Redis it sometimes takes a
lot of time and I don't know if it crashed or just takes ages."*

Tracing all three paths end to end found one dominant root cause and a family of wasted work
on top of it.

1. **A connection leak, not a slow query.** Redis opened a brand-new `ConnectionMultiplexer`
   on every HTTP request and **nothing ever disposed it** — `RedisClient.Dispose()` existed
   and was never called. Service Bus was identical: all sixteen endpoint handlers built a
   fresh `ServiceBusClient`, `ServiceBusAdministrationClient` and `DefaultAzureCredential`,
   and disposed none of them. Because `AbortOnConnectFail` is false, once a cache's per-tier
   connection cap is crossed `ConnectAsync` still *succeeds* and every subsequent command
   blocks for the full async timeout before throwing — the app appears to hang rather than
   fail, and it gets worse the longer it stays open. This is the same regression commit
   `cc700f33` fixed for Storage; AKS already had its pool, Redis and Service Bus never got one.
2. **Work nobody asked for.** Browsing Redis keys silently fired two full 500-key metadata
   sweeps on every scan page. AKS shipped every Helm release Secret's gzipped manifest and
   every ConfigMap value to render key names. AKS listed the cluster's namespaces twice per
   page mount and blocked first paint behind it. Service Bus issued one runtime-properties
   call per queue at five concurrent, and the tree fetched every topic's subscriptions on
   render to put a number on a collapsed row.
3. **No cancellation.** `apiFetch` accepted `RequestInit` but nothing ever passed React
   Query's `AbortSignal`, so a superseded request ran to completion server-side holding its
   connection — and with the global `retry: 1` a timed-out request was silently run twice.

## Outcomes

- Redis and Service Bus reuse one client per cache/namespace instead of opening and leaking
  one per request. Repeated filtering stays fast over a long session rather than degrading.
- A Redis filter returns real matches in one request instead of an empty tree and repeated
  "Load more", and the tree no longer blanks between pages.
- Superseded requests are actually cancelled, server-side included.
- Browsing Redis keys no longer triggers the keyspace-health and prefix-memory sweeps.
- Key-row hints are one request per visible window instead of ~30.
- The AKS Secrets tab no longer transfers Helm release manifests; the ConfigMaps list no
  longer transfers values; a deep-linked namespace paints without waiting on the namespace
  list; Helm notes/manifest no longer spawn `helm` processes nobody asked for.
- Service Bus reads message counts in pages of 100 rather than one call per entity, and a
  collapsed topic's dead-letter badge comes from the list instead of a request per topic.

## Fixed along the way

**Subscription operations were 404ing.** `useSbEntityStats` encoded `entityPath`; the other
eleven hooks did not. A subscription path is `orders/subscriptions/audit`, so unencoded it
cannot match the single-segment `{entityPath}` route — every subscription peek, purge,
complete and resubmit failed and was retried once by `retry: 1`. Found while tracing, fixed
here because it was generating pure wasted round trips.

**"Load all" stopped after one page.** Its effect keyed off `scanResult.data` identity, which
went `undefined` the moment the cursor changed, so the loop switched itself off after a single
extra page while the button had already flipped back from "Loading all…".

## Non-goals

Deliberately out of this pass — see `status.md` for the full follow-up list:

- **AKS cluster-scoped list calls.** `ns="*"` still fans out one call per namespace. This is
  the largest remaining win and a substantial change to a 1400-line file.
- **HTTP/2 on Kestrel**, which would stop the multi-pod log view starving other AKS requests
  against the browser's 6-connection limit.
- **Virtualizing** the AKS resource tables and the Service Bus entity tree.
- **Timing instrumentation.** Explicitly declined: the ask was to make it fast, not to measure it.
- **Redis scan progress UI.** The scan reports `pagesScanned`, but no progress indicator was
  added — also explicitly declined.

## Dependencies

- `src/SwebKit.Core/Services/ClientCache.cs` — the existing generic client cache, whose own
  doc comment already named Service Bus and Redis as the intended next adopters and which
  carries a `GetOrAddAsync` overload added "for factories that must await (e.g. Redis)".
- `src-sidecar/Services/SidecarStorageConnectionPool.cs` — the 27-line template both new
  pools follow.
- `docs/pitfalls/azure-sdk.md` AZ-1 through AZ-4, all preserved; AZ-4 in particular (Entra
  clients must be built via `AzureCredentialFactory`) is what caching the *client* satisfies.
