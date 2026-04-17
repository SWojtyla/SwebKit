# Test Plan - incident-investigation-workflows

---

title: "Test Plan - incident-investigation-workflows"
owner: "GitHub Copilot"
updated: "2026-04-17"

---

## Test approach

Two layers: unit tests for seed construction logic and bUnit tests for visible/disabled state of
the "Investigate" action on each source page.

Validation of the full drill-through flow (launch → navigate → banner displays) is covered by
existing `incident-timeline-workbench` bUnit coverage and manual environment validation.

---

## Unit tests — seed construction

Location: `tests/SwebKit.App.Tests/` or `tests/SwebKit.Core.Tests/` depending on where the
handler logic lives.

### Observability area

| Case                         | Assertion                                                                                                                     |
| ---------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| Resource selected, range set | Seed has `SourceArea = Observability`, `EvidenceRef.ResourceId` matches selected resource, `SelectedRange` matches page range |
| No resource selected         | "Investigate" button is disabled; no seed built                                                                               |

### ServiceBus area

| Case                                           | Assertion                                                                                              |
| ---------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| Entity tab active (queue), no selected message | Seed has `SourceArea = ServiceBus`, `EvidenceRef.EntityPath` = entity path, `MessageId` null           |
| Entity tab active, message selected            | Seed `EvidenceRef.MessageId` = message ID (never body), `CorrelationId` from message if set            |
| Scheduled tab active                           | "Investigate" button is disabled (scheduled tabs have no incident relevance)                           |
| DLQ tab active                                 | "Investigate" button is disabled (DLQ context is handled via entity-path seed, not enabled separately) |
| No tab open                                    | "Investigate" button hidden or disabled                                                                |

### Pipelines area

| Case                             | Assertion                                                                                                                                 |
| -------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| Pipeline selected, project known | Seed has `SourceArea = Pipelines`, `EvidenceRef.PipelineId` = pipeline ID, `ProjectName` = project name, `RunDisplayName` = pipeline name |
| No pipeline selected             | "Investigate" button is disabled                                                                                                          |

---

## bUnit tests — Investigate button state

Location: `tests/SwebKit.App.Tests/ComponentTests/`

| Page              | Condition            | Expected state            |
| ----------------- | -------------------- | ------------------------- |
| ObservabilityPage | Provider not loaded  | Button disabled           |
| ObservabilityPage | Provider loaded      | Button enabled            |
| ServiceBusPage    | No active tab        | Button hidden or disabled |
| ServiceBusPage    | Entity tab active    | Button enabled            |
| ServiceBusPage    | Scheduled tab active | Button disabled           |
| PipelinesPage     | No pipeline selected | Button disabled           |
| PipelinesPage     | Pipeline selected    | Button enabled            |

---

## Manual validation checklist

- [ ] From Observability with a real App Insights resource loaded, click Investigate → workbench opens with seed banner showing resource name and time range.
- [ ] From Service Bus with an entity tab open, click Investigate → workbench opens with seed banner showing entity path.
- [ ] From Service Bus with a message selected, click Investigate → seed banner shows entity path and message ID.
- [ ] From Pipelines with a pipeline selected, click Investigate → workbench opens with seed banner showing pipeline name and project.
- [ ] Confirm that seed banner's "Confirm and load" triggers evidence fetch.
- [ ] Confirm that seed banner's "Dismiss" clears the banner without loading.
- [ ] No message bodies, payloads, or connection strings appear anywhere in the seed banner.
