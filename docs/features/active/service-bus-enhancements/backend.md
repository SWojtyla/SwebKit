---
title: 'Backend Plan - Service Bus Enhancements'
owner: ''
status: 'Proposed'
created: '2026-03-08'
updated: '2026-03-08'
---

# Backend Plan — Service Bus Enhancements

## Overview

This document lists backend changes required to support the feature bundle: quick wins persistence & exports, Edit & Resubmit, Scheduled Message Manager, and Replay-to-Other-Namespace.

## API additions / surface

- `IServiceBusClient.SendMessageAsync(SbMessage message, string targetEntity = null)` — allow explicit target override for resubmit/replay.
- `IServiceBusClient.ScheduleMessageAsync(SbMessage message, DateTimeOffset scheduledEnqueueTime)` — schedules and returns sequence number.
- `IServiceBusClient.CancelScheduledMessageAsync(long sequenceNumber)` — cancels a scheduled message.
- `IServiceBusClient.ReplayMessageAsync(SbMessage source, string targetNamespaceId, string targetEntity, RemapRules rules)` — replay support.

## Persistence & repositories

- `ScheduledMessageRepository` — lightweight store for scheduled message metadata (sequenceNumber, namespaceId, targetEntity, scheduledEnqueueTime, createdBy). Persist in `profiles.json` or a separate local file depending on data sensitivity.
- Saved filters persist in `UiStateRepository` keyed by entity path.

## Implementation notes

- Scheduling: Azure Service Bus returns a sequence number when you schedule a message; Service Bus does not provide a namespace-level list API for scheduled messages. Store the returned sequence number and metadata locally to present a list and allow cancellation.
- Replay: instantiate target `AzureServiceBusClient` using stored namespace connection data (from `ProfileData.ServiceBusNamespaces`) and call `SendMessageAsync` with remapped message.
- Audit: write minimal audit entries for mutative operations to an append-only local audit store (in `profiles.json` under `audit/servicebus` or a small file) to support traceability.

## Files to update

- `src/SwebKit.Core/Services/IServiceBusClient.cs`
- `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`
- `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- `src/SwebKit.Core/Services/ScheduledMessageRepository.cs` (new)

## Acceptance checks

- `SendMessageAsync` supports target override and tests validate property preservation.
- Scheduled messages return sequence numbers and appear in `ScheduledMessageRepository`.
- CancelScheduledMessageAsync cancels scheduled message and repository is updated.
- Replay flows send to target namespace and remap rules are applied.
