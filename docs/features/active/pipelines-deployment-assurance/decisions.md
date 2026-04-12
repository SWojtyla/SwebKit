# Decisions - pipelines-deployment-assurance

---

title: "Decisions - pipelines-deployment-assurance"
owner: "GitHub Copilot"
status: "Planned"

---

## Decision 001 - Runtime assurance requires explicit bindings

**Status:** Accepted

**Date:** 2026-04-12

### Context

The current release records know which pipeline and target tag belong to a component, but they do not know how to find the running workload in AKS or which App Insights resource represents the deployed application. Guessing from names would create false drift results.

### Decision

Release components will gain explicit runtime binding metadata for AKS and optional Observability targets. Drift detection and post-deploy validation will only use those bindings.

### Consequences

- Drift and validation become trustworthy enough to show inline.
- Operators must author bindings before the most valuable assurance states appear.
- Missing bindings surface as `Unknown` or `Not configured`, not as healthy.

### Alternatives considered

- Alternative A - Infer ownership from pipeline or component names: rejected because it is too error-prone for deployment assurance.
- Alternative B - Limit assurance to Azure DevOps data only: rejected because it cannot answer whether runtime actually matches intent.

---

## Decision 002 - Persist assurance snapshots with release data

**Status:** Accepted

**Date:** 2026-04-12

### Context

SwebKit already persists release records and deployment snapshots locally in `releases.json`. Creating a separate assurance store would fragment the operator story and make release detail harder to reconstruct.

### Decision

Additive validation and assurance snapshot data will live alongside existing release persistence in `ReleaseRepository`.

### Consequences

- Release detail can show a coherent history without a second repository abstraction.
- Existing persistence tests need to expand to cover new additive fields.
- The JSON shape must remain backward-compatible for older installs.

### Alternatives considered

- Alternative A - Create a second local assurance repository: rejected because it splits one operator workflow across two stores.
- Alternative B - Do not persist validation history at all: rejected because assurance becomes too ephemeral to review later.

---

## Decision 003 - Validation is manual and advisory only

**Status:** Accepted

**Date:** 2026-04-12

### Context

It is tempting to treat runtime validation as an automatic release gate. That would silently move the product from operator aid to release-governance engine, which is outside this feature's scope and would need stronger workflow controls.

### Decision

Validation runs only when explicitly triggered by the operator and records advisory results. It does not auto-approve, auto-promote, auto-reject, or auto-rollback anything.

### Consequences

- The feature stays safe and implementation-sized.
- Operators can validate at the right time for their rollout rather than during warm-up noise.
- Future automation remains possible, but only as a separate explicit scope.

### Alternatives considered

- Alternative A - Auto-run validation after every completed deployment: rejected because it adds noisy timing problems and hidden governance behavior.
- Alternative B - Treat validation failure as an automatic rollback trigger: rejected because that is far beyond current product scope.

---

## Decision 004 - Start with code-defined approval SLA defaults

**Status:** Accepted

**Date:** 2026-04-12

### Context

Approval aging needs a concrete policy to be useful, but adding a full per-environment SLA editor in the same feature would enlarge the plan before the basic approval-aging workflow is proven.

### Decision

Wave 1 will ship with a small code-defined default policy based on environment naming, and the UI will render both age and derived state. SLA customization can be added later if the default proves insufficient.

### Consequences

- Approval aging can ship without a new configuration editor.
- The feature must document the default thresholds clearly.
- Future customization remains possible if teams need different windows.

### Alternatives considered

- Alternative A - Block the feature until a full SLA editor exists: rejected because it delays useful assurance without evidence that customization is required.
- Alternative B - Show raw age only and no state: rejected because operators still have to infer urgency themselves.
