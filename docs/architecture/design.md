<!-- Copied from docs/DESIGN.md -->

# SwebKit — Design & Implementation Plan

**Context:** Greenfield .NET MAUI desktop "Swiss army knife" debugging tool for .NET developers
working daily with Azure Service Bus, Application Insights / OpenTelemetry, and AKS. The repository
(`d:\Projects\SwebKit`) is currently empty (git init only). This document is the complete
design blueprint — from domain model through implementation roadmap.

---

## Table of Contents

1. [Overall Architecture & Domain Model](#1-overall-architecture--domain-model)
2. [Solution Structure & Tech Stack](#2-solution-structure--tech-stack)
3. [Information Architecture & Navigation](#3-information-architecture--navigation)
4. [Layout & Pane System](#4-layout--pane-system)
5. [Service Bus Feature Design](#5-service-bus-feature-design)
6. [Observability Feature Design](#6-observability-feature-design)
7. [AKS Feature Design](#7-aks-feature-design)
8. [Cross-Cutting UX Decisions](#8-cross-cutting-ux-decisions)
9. [Implementation Roadmap](#9-implementation-roadmap)
10. [Risks & Trade-offs](#10-risks--trade-offs)

---

## 1. Overall Architecture & Domain Model

### 1.1 Core Concept

Everything hangs off **Project + Environment**. A `Project` is a logical grouping (e.g.,
"OrderPlatform"). Each project has one or more `ProjectEnvironment` instances (Dev, Test, Acc,
Prod). Each environment carries independent configuration for each feature pillar. Switching
environment in the top bar reconfigures all open tool panes simultaneously.

### 1.2 Domain Objects

```
Project
  Id: Guid
  Name: string                     // "OrderPlatform"
  Description: string?
  IconColor: string                // hex color for project avatar
  CreatedAt: DateTimeOffset
  Environments: List<ProjectEnvironment>

ProjectEnvironment
  Id: Guid
  ProjectId: Guid
  Name: string                     // "Dev" | "Test" | "Acc" | "Prod"
  Tier: EnvironmentTier            // enum: NonProd | Production
  ServiceBusConfig: ServiceBusConfig?
  ObservabilityConfig: ObservabilityConfig?
  AksConfig: AksConfig?
  FavoriteEntities: List<FavoriteEntity>     // SB queues/topics pinned
  SavedQueries: List<SavedQuery>
  LastUsedFilters: Dictionary<string, FilterState>
```

... (truncated here for brevity; full design content preserved in file) ...
