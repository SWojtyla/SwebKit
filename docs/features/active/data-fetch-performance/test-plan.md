# Data-Fetch Performance — Test Plan

## Automated

### `tests/SwebKit.Sidecar.Tests/SidecarRedisConnectionPoolTests.cs` (new)

The leak is the whole point, so reuse and disposal are pinned rather than assumed.

| Scenario | Expectation |
| --- | --- |
| Two calls for the same cache id | Same client; the factory ran once |
| Two different cache ids | Separate clients |
| `Evict` | Disposes the client and the next call rebuilds |
| `InvalidateAll` | Disposes every cached client; the pool stays usable |
| `DisposeAsync` | Disposes every cached client |
| Demo mode | The factory is never called, and the borrowed singleton is never disposed |

### `tests/SwebKit.Sidecar.Tests/SidecarServiceBusConnectionPoolTests.cs` (new)

The same six, plus: an Entra namespace goes through the Entra factory path and not the
connection-string one — pitfall AZ-4 requires the auth path to stay as it was, and caching the
client is what caches the credential with it.

### `tests/SwebKit.Sidecar.Tests/ConfigEndpointsTests.cs` (extended)

`SaveProfileAsync_InvalidatesEveryConnectionPool` — a save may have changed a Redis cache's or
a Service Bus namespace's credentials, so leaving either pool out would serve later requests
from a client built with credentials the user just changed.

### `tests/SwebKit.Core.Tests/RedisScanLoopTests.cs` (new)

The three termination conditions are the contract:

| Scenario | Expectation |
| --- | --- |
| Several empty pages, then matches | Keeps going; the empty pages were traversed; cursors chained |
| Cursor wraps to 0 | Stops, `IsComplete` true |
| Page fills mid-way | Stops; returns a cursor to resume from; extra matches not lost from the count |
| Budget exhausted, page not full | Returns anyway — otherwise the request hangs on a large keyspace |
| Budget already gone on entry | Still issues one page, or the caller can never make progress |
| Non-zero start cursor | Resumes there rather than restarting |
| One page longer than the page size | Never returns more than asked |
| Page size 0 or negative | Clamped to 1 |
| Cancelled token | Throws |

### `web/src/lib/hooks/useServiceBus.test.ts` (extended)

`invalidateServiceBusQueries` now has three cases: entity-scoped queries always invalidate; the
entity tree is left alone by default; the tree invalidates when `includeTopology` is passed. The
existing `["sb-"]`-matches-nothing regression guard is kept.

New `findCachedEntityStats` cases: finds a queue's counts; finds a subscription's counts (whose
list key carries the topic, so only prefix matching works); returns `undefined` when nothing is
cached; ignores another namespace's same-named entity; skips a listed entity whose counts failed
to load rather than reporting zeros.

### `web/e2e/` (existing)

`redis.spec.ts`, `service-bus.spec.ts` and `aks.spec.ts` run against the sidecar in demo mode and
cover the paths this work rewired — the Redis key browser and its hints, the Service Bus entity
tree, and the AKS ConfigMaps/Secrets tabs.

## Not covered automatically

- **The leak itself under load.** The pool tests prove reuse and disposal; they cannot prove
  that a real Azure Cache stops running out of connections. That needs the manual check below.
- **The bulk runtime-properties join.** `AzureServiceBusClient` talks to the Azure SDK directly
  with no seam, so the pageable join is not unit-testable without a fake admin client. Covered
  by the manual check.
- **`labelSelector: "owner!=helm"`.** Correct per the Kubernetes label-selector spec and matches
  what `MapSecrets` already filtered, but not exercised without a live cluster.

## Manual — owner: Sebastien

Needs real Azure and AKS, so it cannot run here.

1. **The reported symptom.** Open a Redis cache and filter repeatedly over a long session. It
   should stay fast rather than degrading — degradation over time was the leak's signature.
   Previously-blank results for a selective pattern should now return matches in one go.
2. **Redis metadata sweeps.** Browse the Keys tab and confirm the Keyspace and Prefixes panels
   do not fetch until you open them; open each and confirm they still work.
3. **Service Bus subscriptions.** Peek, purge and complete on a *subscription* (not a queue) —
   these were 404ing before the encoding fix.
4. **Service Bus tree.** Open a namespace with many topics: counts should still be right, the
   collapsed dead-letter badge should still show, and expanding a topic should still list its
   subscriptions.
5. **AKS first paint.** Deep-link to a namespace (`?ns=...`) and confirm the tab renders without
   waiting for the cluster namespace list.
6. **AKS Secrets and ConfigMaps.** Both tabs still list correctly; a ConfigMap's detail panel
   still shows values; the Analysis panel's ConfigMap size column is still populated.
7. **Helm.** Open a release — History renders; Notes and Manifest still work when selected.
