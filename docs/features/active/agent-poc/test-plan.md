# Test Plan — Agent PoC

Phase 0 uses manual validation only. The goal is to evaluate Mistral's domain understanding and integration feasibility — not to build production test infrastructure.

## Test Queries

### Kubernetes Domain Understanding

| Query                                               | What We're Testing            | Pass Condition                                      |
| --------------------------------------------------- | ----------------------------- | --------------------------------------------------- |
| "What is the status of pod my-app-123?"             | Basic pod data interpretation | Correct status, restarts, container states reported |
| "Why is this pod in CrashLoopBackOff?"              | Root cause reasoning          | Actionable explanation, no hallucination            |
| "Which pods are unhealthy in namespace production?" | Filtering + reasoning         | Correct subset identified                           |
| "What does the OOMKilled status mean for my pod?"   | Domain knowledge              | Accurate explanation of out-of-memory kill          |

### Data Integration Checks

| Check                                                           | Pass Condition                               |
| --------------------------------------------------------------- | -------------------------------------------- |
| `GetPodStatusTool` executes without error against real AKS data | No exception, valid JSON returned to Mistral |
| Mistral processes the tool result and produces a response       | No prompt/parsing errors                     |
| Response content is grounded in the tool output, not fabricated | Cross-check against actual pod data          |

## Measurements

| Metric                 | Target                        | How to Measure                 |
| ---------------------- | ----------------------------- | ------------------------------ |
| End-to-end latency P50 | < 5 s                         | Log timestamps across 10 runs  |
| End-to-end latency P95 | < 10 s                        | Worst case from 20 runs        |
| Cost per query         | < $0.05                       | Mistral usage dashboard        |
| Domain accuracy        | ≥ 80% of test queries correct | Manual evaluation checklist    |
| Hallucination observed | Zero in first 10 runs         | Manual review of all responses |

## Security Checks

- [ ] API key does not appear in any log line
- [ ] API key is not hard-coded in `SwebKit.Agents`
- [ ] Pod names / namespaces from AKS are not written to disk

## Decision

| Outcome                     | Criteria                                                                               |
| --------------------------- | -------------------------------------------------------------------------------------- |
| **Go** — proceed to Phase 1 | All latency targets met, accuracy ≥ 80%, no showstoppers                               |
| **Iterate**                 | 1–2 targets missed but addressable with prompt tuning or model swap                    |
| **No-Go**                   | Hallucinations on core domain data, cost prohibitive, or fundamental technical blocker |
