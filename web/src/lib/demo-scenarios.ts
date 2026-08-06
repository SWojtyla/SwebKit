import type { ChatMessage } from "./types";

export const CROSS_FEATURE_SCENARIO_ID = "cross-feature";

export const CROSS_FEATURE_USER_PROMPT = `My order-api is failing in AKS. Can you investigate across AKS logs, App Insights dependencies, and Service Bus to find the root cause and show me a timeline and topology of the incident?`;

const CROSS_FEATURE_ASSISTANT_CONTENT = `<!-- scenario:cross-feature -->
I correlated AKS pod state, App Insights dependency telemetry, and Service Bus queue depth for the \`order-api\` service. Here is what I found:

- **AKS**: 5 of 10 \`order-api\` pods are unavailable; the remaining pods are crash-looping after liveness failures.
- **App Insights**: SQL dependency p95 latency spiked to **2.3 s** starting ~30 minutes ago, right before the queue backup began.
- **Service Bus**: the \`orders\` queue depth has grown to **4,200** messages because order-api cannot keep up, creating a retry/re-publish loop.

Root cause hypothesis: the downstream SQL dependency became slow, causing order-api request handling to time out, which backed up the Service Bus publisher and eventually triggered pod restarts.

### Interaction diagram
\`\`\`mermaid
sequenceDiagram
    autonumber
    participant U as User
    participant AKS as order-api pod
    participant AI as App Insights
    participant DB as SQL DB
    participant SB as Service Bus
    U->>AKS: GET /orders/123
    AKS->>DB: SELECT order rows
    DB-->>AKS: slow (2.3s p95)
    AKS->>AI: trackDependency SQL
    AI-->>AKS: alert threshold exceeded
    AKS->>SB: publish OrderPlaced
    SB-->>AKS: queue depth 4,200
    AKS-->>U: 503 timeout
\`\`\`

### Topology of the impacted system
\`\`\`json
{
  "type": "topology",
  "nodes": [
    { "id": "order-api", "label": "order-api (AKS)", "area": "Aks" },
    { "id": "app-insights", "label": "App Insights", "area": "Aks" },
    { "id": "sql-db", "label": "SQL DB", "area": "Aks" },
    { "id": "orders-queue", "label": "orders queue (Service Bus)", "area": "ServiceBus" }
  ],
  "edges": [
    { "from": "order-api", "to": "sql-db", "label": "queries" },
    { "from": "order-api", "to": "app-insights", "label": "telemetry" },
    { "from": "order-api", "to": "orders-queue", "label": "publishes" }
  ]
}
\`\`\`

### Incident timeline
\`\`\`json
{
  "type": "timeline",
  "events": [
    { "time": "T-30m", "title": "DB latency spikes", "description": "App Insights dependency duration p95 jumps to 2.3s" },
    { "time": "T-20m", "title": "Request backlog grows", "description": "order-api latency percentiles climb, first 503s appear" },
    { "time": "T-12m", "title": "Queue depth grows", "description": "Service Bus orders queue reaches 4,200 messages" },
    { "time": "T-5m", "title": "Pod restarts", "description": "AKS liveness probe fails, order-api pods restart" },
    { "time": "T-0m", "title": "Current state", "description": "5/10 pods unavailable, /orders returns 503" }
  ]
}
\`\`\``;

export function getCrossFeatureScenarioMessages(): ChatMessage[] {
  return [
    { id: "demo-scenario-user", role: "user", content: CROSS_FEATURE_USER_PROMPT },
    {
      id: "demo-scenario-assistant",
      role: "assistant",
      content: CROSS_FEATURE_ASSISTANT_CONTENT,
      elapsedMs: 1243,
    },
  ];
}

export interface DemoScenario {
  id: string;
  label: string;
  description: string;
  messages: ChatMessage[];
}

export function getCrossFeatureScenario(): DemoScenario {
  return {
    id: CROSS_FEATURE_SCENARIO_ID,
    label: "Cross-feature incident investigation",
    description: "order-api failure correlated across AKS, App Insights, and Service Bus",
    messages: getCrossFeatureScenarioMessages(),
  };
}
