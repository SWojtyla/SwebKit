# Actionable Error Details

State: In Progress

## Goal

Failures must say *why*. Today an unhandled sidecar exception reaches the user as
"Internal server error" or a coarse classification ("Connection failed",
"Service Bus request failed") — useless for telling a timeout from an auth
failure from a dead server. SwebKit is a technical local app; the user can
reason about real detail.

## Design

**Sidecar** — every error response gains a structured payload:

```json
{ "error": "Service Bus is unreachable", "kind": "unreachable",
  "detail": "ServiceBusException: put_token failed (ServiceCommunicationProblem)",
  "hint": "Check the connection string and that the namespace endpoint is reachable" }
```

- New `ApiErrorClassifier` (extends the existing `ConnectionTestError` mapping):
  exception → `(kind, summary, detail, hint)`. `kind` ∈
  `timeout | unreachable | auth | accessDenied | notFound | busy | clientError |
  serverError`. `detail` = `ExceptionType.Name: scrubbed Message` plus the inner
  exception one level deep. `hint` = what to try next.
- **Scrubbing**: `detail` passes through a scrubber that strips
  connection-string-shaped segments (`Endpoint=…`, `SharedAccessKey=…`,
  `AccountKey=…`, `Password=…`, `SharedAccessSignature=…`, `sig=…`) — details
  without secrets.
- Global handler (`Program.cs`) emits `{ error, kind, detail, hint }` for **all**
  statuses — including 500 (opaque `error`, but `detail` still carries the
  exception type + scrubbed message).
- Connection-test endpoints keep `Describe` for `error` and add `detail`/`kind`.

**Frontend**

- `ApiError extends Error` with `kind`, `detail`, `hint`; `apiFetch`, `apiSend`,
  `streamAgentChat`, `apiUpload` throw it (falls back gracefully on plain-text
  bodies — `kind` inferred from HTTP status).
- `QueryState` error row: message + `kind` badge + expandable **Details**
  (`detail` + `hint`, mono, copyable).
- `notify("error", …)` sites pass `err.detail`/`hint` as the secondary line
  where an `ApiError` is available.

## Non-goals

- Per-endpoint bespoke error contracts.
- Raw stack traces in the UI (server log has them).
- surfacing unscrubbed connection strings — detail is always scrubbed.

## Test plan

- xUnit: classifier table tests (ServiceBusException reasons → kind/hint,
  wrapped auth failure, timeout, socket), scrubber tests (conn-string fields
  removed, normal messages untouched), endpoint test asserting 500 payload shape.
- vitest: `extractErrorMessage`/ApiError mapping tests.
- e2e: fail a Service Bus list call → error row shows kind + expandable detail.
