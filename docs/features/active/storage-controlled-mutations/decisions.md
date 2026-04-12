# Decisions - storage-controlled-mutations

---

title: "Decisions - storage-controlled-mutations"
owner: "GitHub Copilot"
status: "Planned"

---

## Decision 001 - Keep Storage read-only by default and opt into mutations per account

**Status:** Accepted

**Date:** 2026-04-12

### Context

The current Storage experience is intentionally safe because it is read-only. Adding write operations without an explicit mode boundary would make it too easy to assume every configured account is safe to mutate from the UI.

### Decision

Additive configuration will enable mutations per storage account, with the default remaining read-only when the new field is absent or false.

### Consequences

- Existing environments keep their current behavior automatically.
- Operators must make an explicit configuration choice before mutation controls appear.
- UI needs a clear read-only versus mutation-enabled banner or mode indicator.

### Alternatives considered

- Alternative A - Enable all supported mutations whenever credentials allow it: rejected because it is too risky for production accounts.
- Alternative B - Hide all mutations behind a hidden developer switch only: rejected because the feature would become too hard to use intentionally.

---

## Decision 002 - Recovery should preserve version history where possible

**Status:** Accepted

**Date:** 2026-04-12

### Context

Restoring an older version by replacing the current blob in place can be operationally correct, but the operator still needs an audit-friendly history after the recovery. Version-enabled accounts already give Azure Blob Storage a natural way to preserve that history.

### Decision

When versioning is enabled, recovery will be modeled as copy-forward restore into the current blob path so the restored content becomes a new current version. When only soft delete is available, the client will use undelete if the account supports it.

### Consequences

- Recovery remains auditable.
- The confirmation UI must explain that a new current version will be created rather than history being erased.
- Capability detection becomes essential to decide which recovery path is available.

### Alternatives considered

- Alternative A - Blindly overwrite the current blob with older content and ignore version history: rejected because it weakens auditability.
- Alternative B - Only allow recovery to a new side-path and never restore in place: rejected because operators often need the original logical path back quickly.

---

## Decision 003 - Version diff is bounded and content-type aware

**Status:** Accepted

**Date:** 2026-04-12

### Context

The current Storage page already uses preview caps and binary detection. A version diff feature that ignores those constraints would either become slow or produce unreadable garbage for binary content.

### Decision

Version diff will be text-first for supported content types inside existing preview limits. Large or binary blobs fall back to metadata, size, timestamps, and version identifiers rather than forcing a text diff.

### Consequences

- Diff remains useful and predictable.
- The UI must explain when it is showing metadata-only comparison.
- The backend can reuse current preview and content-loading logic rather than inventing a heavy diff pipeline.

### Alternatives considered

- Alternative A - Always render a raw text diff regardless of content type: rejected because binary and oversized content produce poor results.
- Alternative B - Skip version diff entirely and offer only download: rejected because comparison is one of the main operator needs driving this feature.

---

## Decision 004 - Keep v1 mutations single-blob scoped

**Status:** Accepted

**Date:** 2026-04-12

### Context

The existing Storage UI already has some multi-select patterns, which makes bulk mutation tempting. That would rapidly increase both product risk and confirmation complexity.

### Decision

Wave 1 through Wave 3 stay single-blob scoped for upload replacement, copy, metadata edit, compare, and recovery. Bulk mutation remains out of scope.

### Consequences

- Confirmation and progress UX stays understandable.
- The feature remains implementation-sized and production-safe.
- Future bulk workflows can be evaluated later with a different safety model if there is proven need.

### Alternatives considered

- Alternative A - Add bulk upload/copy/delete because multi-select already exists: rejected because it raises the safety bar significantly.
- Alternative B - Allow bulk metadata changes by prefix: rejected because it is too easy to misuse in production.
