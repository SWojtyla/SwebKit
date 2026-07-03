# Phase 3: Automation

## 🎯 Purpose

**Transform the agent from a reactive assistant to a proactive, autonomous system** that monitors, analyzes, and can take safe actions to prevent and resolve issues automatically.

Phase 3 is about **making the agent work for you, not just with you**. This phase focuses on automation, proactive monitoring, and intelligent workflows that can run continuously in the background, catching issues before they impact users and automatically performing routine tasks.

---

## 🧠 Abstract Analysis

### What Phase 3 Is

While Phase 1 was "Can we make it work?" and Phase 2 was "Can we make it intelligent?", Phase 3 asks:

- **Can we make it proactive?** (monitor and alert before users notice)
- **Can we make it autonomous?** (perform safe actions without user intervention)
- **Can we make it scalable?** (handle enterprise workloads efficiently)
- **Can we make it reliable?** (operate continuously with minimal human oversight)

### Key Questions This Phase Answers

1. **Proactive Monitoring**: Can the agent effectively monitor infrastructure and detect issues early?
2. **Safe Automation**: Can the agent perform actions autonomously without causing harm?
3. **Workflow Automation**: Can complex, multi-step workflows be automated effectively?
4. **Enterprise Readiness**: Is the system reliable, scalable, and secure enough for production use?
5. **ROI Validation**: Does the automation provide sufficient value to justify the complexity?

### Phase 3 vs. Phase 2

| Aspect         | Phase 2                 | Phase 3                         |
| -------------- | ----------------------- | ------------------------------- |
| **Agent Role** | Assistant               | Autonomous agent                |
| **Initiation** | User-triggered          | Event-triggered + Scheduled     |
| **Execution**  | User-guided             | Autonomous (with limits)        |
| **Scope**      | Interactive sessions    | Continuous background operation |
| **Actions**    | Read-only + suggestions | Safe write operations           |
| **Complexity** | Medium                  | High                            |

---

## 🎯 Goals

### Primary Goals

1. **Continuous Monitoring**: Agent proactively monitors infrastructure health
2. **Proactive Alerting**: Agent detects and alerts on potential issues before they escalate
3. **Safe Automation**: Agent can perform approved remediation actions automatically
4. **Workflow Engine**: Support for multi-step, conditional workflows
5. **Enterprise Reliability**: Production-ready reliability, scalability, and security

### Secondary Goals

1. **Advanced Analytics**: Anomaly detection, trend analysis, capacity planning
2. **Multi-Environment Support**: Monitor and manage across multiple clusters/environments
3. **Comprehensive Observability**: Full visibility into agent operations and decisions
4. **Governance Framework**: Policies, approvals, and audit trails for automated actions

---

## 📋 Scope

### ✅ Already Implemented

**Agent Tools for Read-Only Operations**
- ✅ `GetPodLogsTool` - Fetches pod logs with tail lines, container selection, and filtering
- ✅ `GetPodStatusTool` - Retrieves pod status, phase, readiness, and container states
- ✅ `GetPodEventsTool` - Gets Kubernetes events for pods with sorting and filtering
- ✅ `InvestigatePodIssueTool` - Composite tool combining status, logs (50 lines), and events in parallel
- ✅ `ListNamespacesTool` - Lists available Kubernetes namespaces
- ✅ `ListPodsTool` - Lists pods with filtering by namespace and labels
- ✅ `QueryLogsTool` - Executes KQL queries against Application Insights with time range and row limits
- ✅ `GetQueueStatsTool` - Retrieves Service Bus queue statistics and metrics
- ✅ `GetQueueMessagesTool` - Fetches messages from Service Bus queues
- ✅ `GetMetricsTool` - Retrieves observability metrics
- ✅ `AnalyzeQueueHealthTool` - Advanced Service Bus queue health analysis with anomaly detection

**Monitoring Infrastructure**
- ✅ `PodHealthMonitorService` - Continuous pod health monitoring with configurable polling
- ✅ AKS client with `StreamPodLogsAsync()` supporting follow mode, tail lines, since seconds, text filtering, and previous container access
- ✅ Multi-pod log aggregation with timestamp merging and cross-pod correlation
- ✅ `LogStreamOptions` class with full configuration (TailLines, Follow, SinceSeconds, TextFilter, PreviousContainer)

**UI Components**
- ✅ `PodLogView.razor` - Single pod log viewer with range selection, live streaming, and container switching
- ✅ `MultiPodLogView.razor` - Multi-pod log aggregation with color-coded streams and merged timestamps
- ✅ Integration in `AksDetailPanels.razor` for both single and multi-pod log viewing

**Agent Integration**
- ✅ All tools registered in `MauiProgram.cs` with proper dependency injection
- ✅ Demo mode support for all tools via `DemoAksClient`
- ✅ Tool schema definitions for agent consumption
- ✅ Error handling and graceful degradation

### ✅ In Scope

**Continuous Monitoring**

- Health checks for Kubernetes resources
- Service Bus queue monitoring
- Storage account monitoring
- Redis cache monitoring
- Observability data analysis

**Proactive Analysis**

- Anomaly detection using statistical analysis
- Trend analysis and forecasting
- Capacity planning insights
- Predictive failure analysis
- Dependency impact analysis

**Safe Automation**

- Read-only health checks (always safe)
- Configurable automated responses
- User-approved action catalog
- Step-by-step workflow execution
- Rollback capabilities

**Workflow Engine**

- Multi-step investigation workflows
- Conditional logic in workflows
- Tool chaining and orchestration
- Workflow scheduling and triggering
- Workflow versioning and management

**Enterprise Features**

- Multi-environment support
- Team collaboration features
- Comprehensive audit logging
- Usage analytics and reporting
- Role-based access control

**Reliability & Scalability**

- Distributed processing for high-volume workloads
- Queue-based job processing
- Retry and circuit breaker patterns
- Rate limiting and throttling
- Health monitoring for agent services

### ❌ Out of Scope

**High-Risk Automation**

- Actions that could cause data loss
- Actions that could cause service outages
- Actions without human oversight capability
- Actions without rollback capability

**Complex AI Features**

- Custom model fine-tuning
- Model serving infrastructure
- Advanced machine learning
- Custom training data management

**External System Integration**

- Integration with third-party monitoring tools
- Integration with external ticketing systems (basic is ok, deep integration is out)
- Integration with other AI services

---

## 🏗️ Architecture Overview

### Proactive Monitoring Architecture

```
┌─────────────────────────────────────────────────────────────┐
│               Proactive Monitoring System                      │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  ┌──────────────────┐  ┌──────────────────┐  ┌────────────┐ │
│  │   Schedule        │  │   Event Trigger   │  │   On-Demand  │ │
│  │   Manager         │  │   Handler         │  │   Execution   │ │
│  │                  │  │                  │  │              │ │
│  │  • Cron-based    │  │  • Alert events  │  │  • User      │ │
│  │  • Interval-based │  │  • Metric thresholds││  │    requests   │ │
│  │  • Calendar-based │  │  • Log patterns  │  │  • API calls  │ │
│  └────────┬─────────┘  └────────┬─────────┘  └──────┬─────┘ │
│            │                   │                    │       │
│            └───────────────────┼────────────────────┘       │
│                                │                              │
│                                ▼                              ▼
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                    Workflow Engine                          │ │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐    │ │
│  │  │  Workflow   │  │   Step      │  │  Condition   │    │ │
│  │  │  Definition │──▶│  Execution  │──▶│   Evaluation │    │ │
│  │  │             │  │             │  │             │    │ │
│  │  │ • Steps    │  │ • Tool calls│  │ • If/Then    │    │ │
│  │  │ • Triggers │  │ • Delays    │  │ • Loops      │    │ │
│  │  │ • Variables│  │ • Retries   │  │ • Branching │    │ │
│  │  └─────────────┘  └─────────────┘  └─────────────┘    │ │
│  └─────────────────────────────────────────────────────────┘ │
│                                                               │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                    Action Governance                       │ │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐    │ │
│  │  │  Policy      │  │   Approval   │  │   Audit      │    │ │
│  │  │  Engine      │  │   Workflow   │  │   Log        │    │ │
│  │  │             │  │             │  │             │    │ │
│  │  │ • Safe      │  │ • Manual    │  │ • All       │    │ │
│  │  │   actions   │  │   approval   │  │   actions   │    │ │
│  │  │ • Restricted│  │ • Auto-     │  │ • Decisions │    │ │
│  │  │   actions   │  │   approval   │  │ • State     │    │ │
│  │  │ • Blocked   │  │ • Time-based │  │   changes   │    │ │
│  │  │   actions   │  │   approval   │  │             │    │ │
│  │  └─────────────┘  └─────────────┘  └─────────────┘    │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### Monitoring and Analysis Architecture

```
┌─────────────────────────────────────────────────────────────┐
│               Monitoring & Analysis System                    │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                    Monitoring Targets                     │  │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐ │  │
│  │  │  AKS     │  │Service Bus│  │ Storage  │  │ Redis   │ │  │
│  │  │  Clusters│  │  Queues   │  │ Accounts │  │ Caches  │ │  │
│  │  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘ │  │
│  │       │             │             │             │        │  │
│  └───────┼─────────────┼─────────────┼─────────────┼────────┘  │
│          │             │             │             │           │
│          ▼             ▼             ▼             ▼           │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                    Health Check Agents                     │  │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐ │  │
│  │  │  Pod     │  │  Queue   │  │  Blob    │  │  Cache   │ │  │
│  │  │  Health  │  │  Health  │  │  Health  │  │  Health  │ │  │
│  │  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘ │  │
│  │       │             │             │             │        │  │
│  └───────┼─────────────┼─────────────┼─────────────┼────────┘  │
│          │             │             │             │           │
│          ▼             ▼             ▼             ▼           │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                    Analysis Engine                         │  │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐    │  │
│  │  │  Pattern    │  │  Trend       │  │  Anomaly    │    │  │
│  │  │  Detection  │  │  Analysis    │  │  Detection  │    │  │
│  │  └─────────────┘  └─────────────┘  └─────────────┘    │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                               │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                    Notification System                    │  │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐ │  │
│  │  │  In-App  │  │  Email   │  │  Teams   │  │  Webhook │ │  │
│  │  │  Alert   │  │  Alert   │  │  Alert   │  │  Alert   │ │  │
│  │  └──────────┘  └──────────┘  └──────────┘  └──────────┘ │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

---

## 📦 Deliverables

### 1. Monitoring System

**Health Check Framework**

- [x] Generic health check interface - `IAksClient` with `StreamPodLogsAsync()`, `GetPodsAsync()`, etc.
- [x] AKS resource health checks (pods, deployments, nodes, namespaces) - `GetPodStatusTool`, `GetPodLogsTool`, `GetPodEventsTool`, `ListPodsTool`
- [x] Service Bus health checks (queues, topics, subscriptions) - `GetQueueStatsTool`, `GetQueueMessagesTool`, `AnalyzeQueueHealthTool`
- [ ] Storage health checks (blob containers, file shares)
- [ ] Redis health checks (cache instances, memory usage)

**Scheduling System**

- Cron-based scheduling
- Interval-based scheduling
- Calendar-based scheduling
- Event-triggered execution
- On-demand execution

**Health Check Types**

- **Simple Checks**: Single metric threshold (e.g., pod restart count > 5)
- **Composite Checks**: Multiple metrics combined (e.g., high CPU + high memory)
- **Trend Checks**: Metric trends over time (e.g., memory increasing rapidly)
- **Pattern Checks**: Log pattern detection (e.g., repeated error messages)
- **Anomaly Checks**: Statistical anomaly detection (e.g., traffic spike)

### 2. Proactive Analysis

**Anomaly Detection**

- [x] Basic anomaly detection - `AnalyzeQueueHealthTool` includes anomaly detection for Service Bus queues
- [ ] Statistical anomaly detection for metrics
- [ ] Machine learning-based anomaly detection (using Mistral or simple models)
- [ ] Historical pattern matching
- [ ] Seasonality-aware detection

**Trend Analysis**

- Resource usage trends (CPU, memory, storage, network)
- Performance degradation detection
- Capacity exhaustion prediction
- Cost trend analysis and forecasting

**Predictive Analysis**

- Failure prediction based on historical patterns
- Impact analysis for planned changes
- Dependency analysis (what breaks if X fails)
- Risk assessment for deployments

**Insight Generation**

- Automated insight reports (daily, weekly)
- Health score calculations
- Recommendation engine
- Best practice compliance checking

### 3. Safe Automation Framework

**Action Catalog**

- **Read-Only Actions** (always safe)
  - [x] Health checks - `GetPodStatusTool`, `GetPodEventsTool`
  - [x] Log queries - `GetPodLogsTool`, `QueryLogsTool` (Application Insights)
  - [x] Metric retrieval - `GetMetricsTool`
  - [x] Resource listing - `ListNamespacesTool`, `ListPodsTool`
  - [x] Configuration inspection - All read-only tools support configuration inspection

- **Safe Write Actions** (configurable, approved)
  - Pod restart (with confirmation)
  - Scale deployment (within limits)
  - Clear dead-letter queue messages
  - Acknowledge alerts
  - Update annotation/labels

- **Restricted Actions** (manual approval required)
  - Deployment rollback
  - Configuration changes
  - Resource deletion (with safeguards)
  - Access control modifications

**Governance System**

- **Policy Engine**: Rules for what actions are allowed when
- **Approval Workflows**: Manual approval for sensitive actions
- **Audit Trail**: Complete logging of all automated actions
- **Rollback Mechanisms**: Ability to undo automated changes
- **Rate Limiting**: Prevent action flooding

### 4. Workflow Engine

**Workflow Definition**

- Visual workflow designer (future)
- YAML/JSON workflow definitions
- Version control for workflows
- Workflow testing framework

**Workflow Components**

- **Steps**: Individual actions or tool calls
- **Conditions**: Branching logic based on step results
- **Variables**: Data passing between steps
- **Triggers**: What starts the workflow
- **Schedules**: When the workflow runs
- **Error Handling**: What happens if a step fails

**Workflow Types**

- **Investigation Workflows**: Multi-step issue diagnosis
- **Remediation Workflows**: Automated fix sequences
- **Monitoring Workflows**: Continuous health checks
- **Reporting Workflows**: Scheduled insight generation
- **Maintenance Workflows**: Routine operational tasks

**Example Workflows**

- [x] `PodFailureInvestigation`: Implemented as `InvestigatePodIssueTool` - Check status → Check logs → Check events in parallel
- [ ] `QueueBacklogAlert`: Check depth → Check processing rate → Check errors → Notify team → Suggest scaling
- [ ] `DailyHealthReport`: Check all pods → Check all queues → Check metrics → Generate summary → Send to team
- [ ] `DeploymentVerification`: Check pod status → Check readiness probes → Check logs → Verify rollout → Notify completion

### 5. Enterprise Features

**Multi-Environment Support**

- Environment-aware workflows
- Cross-environment correlation
- Environment-specific configurations
- Global vs. environment-specific monitoring

**Team Collaboration**

- Workflow sharing between team members
- Team-level access controls
- Shared workflow catalog
- Collaboration on workflow design

**Observability & Analytics**

- Comprehensive audit logging
- Workflow execution metrics
- Automation success/failure rates
- Performance analytics
- Usage reporting

**Security & Compliance**

- Role-based access control (RBAC)
- Action approval policies
- Audit trail retention policies
- Compliance reporting
- Data privacy controls

### 6. Reliability & Scalability

**Processing Infrastructure**

- Job queue for workflow execution
- Worker pool for parallel processing
- Distributed processing capabilities
- Retry and circuit breaker patterns

**Resilience Features**

- Workflow checkpointing (resume after failure)
- Dead letter queue for failed jobs
- Health monitoring for agent services
- Automatic scaling of workers
- Graceful degradation under load

---

## ✅ Success Criteria

### Technical Success

- [x] **Read-only monitoring foundation** - Core monitoring tools for pods, queues, and metrics are implemented
- [x] **Health check infrastructure** - Pod health monitoring service with configurable polling
- [x] **Agent tool integration** - All monitoring tools properly registered and functional
- [x] **Error handling and recovery** - Graceful degradation and error handling in all tools
- [x] **Demo mode support** - All tools work in demo mode via DemoAksClient
- [ ] Workflow engine executes workflows correctly
- [ ] Automation framework is secure and governed
- [ ] System scales to enterprise workloads

### User Success

- [x] **Users can access live pod data** - All agent tools fetch real-time data from Kubernetes and Azure services
- [x] **Multi-source investigation** - Composite tools like `InvestigatePodIssueTool` provide comprehensive diagnostics
- [x] **Flexible log access** - Users can access logs with various filters, time ranges, and container selection
- [ ] Users receive proactive alerts before issues escalate
- [ ] Automated remediation reduces manual intervention
- [ ] Workflows complete successfully and provide value
- [ ] Users can easily create and modify workflows

### Business Success

- [x] **Foundation for MTTD reduction** - Real-time monitoring tools enable faster issue detection
- [x] **Foundation for operational efficiency** - Composite tools reduce investigation time by fetching multiple data sources in parallel
- [ ] Mean Time to Detection (MTTD) is reduced
- [ ] Mean Time to Resolution (MTTR) is reduced
- [ ] System reliability is increased
- [ ] Cost savings from automation justify investment

---

## 📊 Metrics to Track

### Technical Metrics

- **Monitoring Coverage**: % of critical resources covered by health checks
- **Detection Latency**: Time from issue onset to detection
- **False Positive Rate**: % of alerts that are not real issues
- **False Negative Rate**: % of real issues not detected
- **Workflow Success Rate**: % of workflows that complete successfully
- **Automation Rate**: % of issues resolved automatically

### Operational Metrics

- **MTTD (Mean Time to Detection)**: Average time to detect issues
- **MTTR (Mean Time to Resolution)**: Average time to resolve issues
- **Incident Reduction**: % reduction in incident count or severity
- **Manual Intervention**: Reduction in manual operational tasks
- **Resource Efficiency**: Optimal resource utilization from automation

### Business Metrics

- **Cost Savings**: Monetary value of time saved
- **Uptime Improvement**: Increase in service availability
- **Team Productivity**: Increase in operational efficiency
- **User Satisfaction**: Feedback on automated system
- **ROI**: Return on investment for automation

---

## 🔄 Transition to Production

### Production Readiness Criteria

- [ ] All critical resources are monitored
- [ ] Health checks have <1% false positive rate
- [ ] Automated actions have 100% rollback capability
- [ ] System has comprehensive audit logging
- [ ] Performance meets SLA requirements
- [ ] Security review is complete
- [ ] Disaster recovery plan is in place
- [ ] Documentation is complete

### Go-Live Strategy

1. **Pilot Program**: Limited deployment to early adopters
2. **Gradual Rollout**: Expand to more teams gradually
3. **Monitoring Intensive**: Close monitoring during initial rollout
4. **Feedback Collection**: Aggressive feedback gathering
5. **Iterative Improvement**: Continuous improvement based on feedback

---

## 📈 Risk Assessment

### High-Risk Areas

| Risk                                       | Probability | Impact   | Mitigation                                               |
| ------------------------------------------ | ----------- | -------- | -------------------------------------------------------- |
| False positives causing alert fatigue      | High        | High     | Tuning, confidence scoring, suppression                  |
| Automated action causing outage            | Low         | Critical | Strict governance, testing, rollback                     |
| Performance degradation at scale           | Medium      | High     | Performance benchmarking, circuit breakers, optimization |
| Workflow errors causing cascading failures | Medium      | High     | Isolation, circuit breakers, timeouts                    |
| Security vulnerabilities in automation     | Medium      | Critical | Security review, least privilege, audit                  |
| Complexity overwhelming users              | Medium      | Medium   | Gradual rollout, training, documentation                 |

### Mitigation Strategies

**False Positives**

- Implement confidence scoring for alerts
- Add suppression mechanisms for known issues
- Tune thresholds based on historical data
- Provide easy feedback mechanism for false positives

**Safe Automation**

- Start with read-only actions only
- Implement comprehensive approval workflows
- Test all automated actions thoroughly
- Ensure all actions have rollback capability
- Start with non-production environments

**Performance & Reliability**

- Benchmark with realistic production data volumes
- Implement circuit breakers and rate limiting
- Use queue-based processing to handle spikes
- Monitor system health continuously
- Implement automatic scaling

**Security**

- Principle of least privilege for all automated actions
- Comprehensive audit logging
- Regular security reviews
- Role-based access control
- Action approval policies

**User Adoption**

- Start with simple, well-understood workflows
- Provide comprehensive documentation
- Offer training and support
- Collect and act on feedback quickly
- Make automation opt-in initially

---

## 🎯 Implementation Approach

### Development Strategy

**Phased Automation**

1. Start with monitoring and alerting only (no automation)
2. Add read-only automated investigations
3. Add safe, user-approved automated actions
4. Add more complex automation with strong governance
5. Continuously monitor and improve

**Safety-First Approach**

- All automated actions must have manual equivalent
- All automated actions must have rollback
- All automated actions must be auditable
- All automated actions must be approvable
- Start with least destructive actions first

### Quality Assurance

**Testing Requirements**

- Unit tests for all new components
- Integration tests for workflows
- End-to-end tests for automation scenarios
- Performance and reliability tests
- Security tests for all automated actions
- Failure and recovery tests

**Validation**

- User acceptance testing for all workflows
- Production-like environment testing
- Disaster recovery testing
- Performance benchmarking

### Team Coordination

**Key Stakeholders**

- **Operations Team**: Define monitoring requirements and thresholds
- **Security Team**: Review all automated actions and access controls
- **Platform Team**: Ensure system scalability and reliability
- **DevOps Team**: Coordinate deployment and CI/CD
- **Legal/Compliance**: Review automation policies and audit requirements

**Collaboration Points**

- Work with operations to define health check requirements
- Coordinate with security on access control policies
- Consult with platform team on scalability requirements
- Align with DevOps on deployment strategies

---

## 📝 Implementation Priority

### Phase 3A: Monitoring & Alerting (Week 1-2)

1. **Health Check Framework**
   - [x] Generic health check interface - `IAksClient` with comprehensive streaming methods
   - [x] AKS health checks - `GetPodStatusTool`, `GetPodLogsTool`, `GetPodEventsTool`
   - [x] Service Bus health checks - `GetQueueStatsTool`, `GetQueueMessagesTool`, `AnalyzeQueueHealthTool`
   - [ ] Basic alerting

2. **Scheduling System**
   - [x] Continuous monitoring infrastructure - `PodHealthMonitorService` with configurable polling
   - [ ] Cron-based scheduler
   - [ ] Simple event triggers
   - [ ] Basic notification system

### Phase 3B: Safe Automation (Week 3-4)

1. **Action Governance**
   - [ ] Policy engine
   - [ ] Approval workflows
   - [ ] Audit logging

2. **Simple Workflows**
   - [x] Investigation workflows - `InvestigatePodIssueTool` combines status, logs, and events in parallel
   - [ ] Basic remediation workflows
   - [ ] Workflow testing framework

### Phase 3C: Advanced Features (Week 5-6)

1. **Advanced Analysis**
   - Anomaly detection
   - Trend analysis
   - Predictive analysis

2. **Enterprise Features**
   - Multi-environment support
   - Team collaboration
   - Comprehensive observability

### Phase 3D: Reliability & Scale (Week 7-8)

1. **Scalability**
   - Job queue system
   - Worker pool
   - Distributed processing

2. **Reliability**
   - Checkpointing
   - Dead letter queue
   - Health monitoring
   - Automatic scaling

---

## 🔗 Related Documents

### Phase Documents

- [README - Overview](../README.md)
- [Phase 0: Proof of Concept - Previous phase](phase-0-poc.md)
- [Phase 1: Foundation - Previous phase](phase-1-foundation.md)
- [Phase 2: Intelligence - Previous phase](phase-2-intelligence.md)

### Supporting Documents

- [Architecture](../architecture.md) - Workflow engine and automation architecture
- [Security Considerations](../security-considerations.md) - Safe automation and governance
- [Testing Strategy](../testing-strategy.md) - Safety and reliability testing for Phase 3
- [Performance Optimization](../performance-optimization.md) - Scalability and reliability optimization
- [Metrics and Monitoring](../metrics-and-monitoring.md) - Enterprise monitoring and alerting

---

_Document created: 2026-06-29_
_Last updated: 2026-07-01_
