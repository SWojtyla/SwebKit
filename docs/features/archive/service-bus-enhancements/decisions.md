---
title: 'Decisions - Service Bus Enhancements'
owner: ''
status: 'Proposed'
created: '2026-03-08'
updated: '2026-03-08'
---

# Decisions — Service Bus Enhancements

## Decision 001 — Scheduled message metadata storage

We will store scheduled message metadata locally (sequenceNumber, namespaceId, targetEntity, scheduledEnqueueTime) because Azure Service Bus does not provide a list-scheduled API. This is acceptable for developer tooling; note in docs that metadata is local only.

## Decision 002 — Simple remap rules first

Start with simple property remapping plus optional body passthrough/template placeholder support. Defer complex transformations (JSONPath/transforms) to future work.

## Decision 003 — Production guard & audit

All mutative flows (resubmit, replay, cancel scheduled) require explicit confirmation in production and write a minimal audit entry to the local audit store.
