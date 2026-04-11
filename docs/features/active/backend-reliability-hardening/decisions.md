# Decisions - backend-reliability-hardening

---

title: "Decisions - backend-reliability-hardening"
owner: "GitHub Copilot"
status: "Review"

---

## Decision 001 - Use immutable DevOps client snapshots instead of mutable singleton configuration

**Status:** Accepted

**Date:** 2026-04-11

### Context

The real DevOps path currently relies on shared mutable state in `DevOpsClient` and `DevOpsAuthHandler`. That is unsafe when different pages or environment switches reuse the same singleton-backed objects.

### Decision

The real DevOps path will use immutable per-configuration client creation, most likely behind an additive factory or session boundary, while preserving the existing `IDevOpsClient` consumer shape.

### Consequences

- Removes cross-environment and cross-page configuration bleed.
- Requires small app-layer DI and caller changes.
- Preserves the current `AddStandardResilienceHandler` investment.

### Alternatives considered

- Alternative A - keep the singleton and serialize `Configure` calls. Rejected because it still relies on shared mutable state.
- Alternative B - pass `DevOpsConfig` through every `IDevOpsClient` method call. Rejected because it pollutes all consumers and weakens the abstraction.

---

## Decision 002 - DLQ mutation operations must be exhaustive or explicitly fail

**Status:** Accepted

**Date:** 2026-04-11

### Context

`CompleteDeadLetterAsync` and `ResubmitDeadLetterAsync` currently inspect only one receive batch. That can produce silent partial success when the requested sequence numbers are not all present in the first batch.

### Decision

DLQ mutation paths must continue receiving until the requested sequence set is exhausted or the queue is drained. If the queue is drained first, the operation must fail explicitly instead of silently doing partial work.

### Consequences

- Correctness takes priority over best-effort partial completion.
- Shared receive-loop logic becomes worth centralizing and testing.
- UI callers will receive a clearer failure model.

### Alternatives considered

- Alternative A - keep best-effort behavior and log a warning. Rejected because it hides incomplete work from the caller.
- Alternative B - rely on peeked UI state and assume the first batch is enough. Rejected because broker ordering and batch boundaries make that unsafe.

---

## Decision 003 - Continuation cursors are source-owned tokens and must not be fabricated

**Status:** Accepted

**Date:** 2026-04-11

### Context

Redis set-member paging currently derives the next cursor from `cursor + page length`. That is not a real Redis continuation contract and can skip or duplicate members.

### Decision

The Redis set-member page contract will keep a cursor field, but the implementation must use a source-issued `SSCAN` cursor or equivalent opaque token instead of a synthetic offset.

### Consequences

- Page continuity becomes correct.
- The UI must treat the cursor as opaque state rather than a predictable offset.
- Strict global ordering is no longer an implied contract.

### Alternatives considered

- Alternative A - keep the fabricated offset cursor. Rejected because it is incorrect.
- Alternative B - materialize the full set and page in memory. Rejected because it defeats the point of incremental loading.

---

## Decision 004 - Profile load failures are surfaced, not swallowed

**Status:** Accepted

**Date:** 2026-04-11

### Context

`ProfileRepository` currently catches load exceptions and replaces state with defaults. That hides the root cause and makes it easy to overwrite a broken `profiles.json` file after startup.

### Decision

Profile loading will return an explicit success or failure outcome to the caller, startup will remain non-fatal, and failed loads will not be treated as silent successful resets.

### Consequences

- Operators can distinguish a genuinely empty profile from a failed profile load.
- `AppStateService` must carry initialization diagnostics.
- Broad generalization to every repository is deferred unless a very small shared helper naturally emerges.

### Alternatives considered

- Alternative A - keep the current silent fallback. Rejected because it hides corruption and invites data loss.
- Alternative B - crash startup on any profile-load failure. Rejected because it is too disruptive for a desktop operations tool.

---

## Decision 005 - Publish and PublishAsync remain distinct event-bus modes

**Status:** Accepted

**Date:** 2026-04-11

### Context

Existing tests already imply that `Publish` is sync-only and that async subscribers belong to `PublishAsync`. The current false cast logging comes from violating that distinction internally.

### Decision

Keep `Publish` as sync-only and `PublishAsync` as sync plus async, with type-aware dispatch that ignores async subscribers during sync publish without logging false errors.

### Consequences

- Existing `Publish_IgnoresAsyncHandlers` behavior remains valid.
- Log noise drops and real handler failures are easier to trust.
- No new fire-and-forget behavior is introduced.

### Alternatives considered

- Alternative A - auto-run async handlers from `Publish`. Rejected because that would hide asynchronous work behind a synchronous API.
- Alternative B - remove async handler support entirely. Rejected because `PublishAsync` is already a legitimate use case.

---

## Decision 006 - Observability result capping happens at the projection boundary

**Status:** Accepted

**Date:** 2026-04-11

### Context

The current `AzureAppInsightsProvider` behavior applies truncation too late in the returned-row projection path.

### Decision

`RunQueryAsync` should build at most `maxRows + 1` projected rows, use the extra row only to determine truncation, and keep the free-form query text unchanged.

### Consequences

- Returned model construction becomes bounded.
- The provider contract stays stable for the logs UI.
- Free-form user KQL is not rewritten behind the user’s back.

### Alternatives considered

- Alternative A - append a `take` clause to every user query. Rejected because it changes free-form query semantics.
- Alternative B - accept the current projection behavior. Rejected because it does not meet the correctness-first hardening goal.