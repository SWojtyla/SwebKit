import { create } from "zustand";

export interface DemoTourStep {
  route: string;
  title: string;
  description: string;
  target?: string;
  /**
   * Optional scenario identifier grouping steps into a story (e.g. "prd-day" for the
   * locked-down-production walkthrough) — lets tests/docs pick the PRD-day stops out of the
   * full tour without hard-coding their indices.
   */
  scenarioId?: string;
  /** Demo connection id the step's story depends on (e.g. the restricted `demo-sql-prd`). */
  connection?: string;
}

export const DEMO_TOUR_STEPS: DemoTourStep[] = [
  {
    route: "/",
    title: "AI Cockpit",
    description: "This is your command center. Health tiles, live watch metrics, workspace topology, and proactive insights are all visible from one place.",
    target: "[data-testid='dashboard-title']",
  },
  {
    route: "/aks",
    title: "Kubernetes",
    description: "Inspect deployments, pods, services, logs, and HTTP routes. In demo mode the data is synthetic but representative.",
    target: "[data-testid='aks-page']",
  },
  {
    route: "/aks",
    title: "Rows: click vs. right-click",
    description: "Left-click any row to open its details or YAML — every resource tab (Pods, Deployments, Services, Secrets, and the rest) works this way. Right-click a row for the full action menu: scale, delete, restart, port-forward, and more.",
    target: "[data-testid='aks-content']",
  },
  {
    route: "/service-bus",
    title: "Service Bus",
    description: "Browse namespaces, queues, topics, and subscriptions. Peek, send, and dead-letter messages without affecting a real namespace.",
    target: "[data-testid='service-bus-page']",
  },
  {
    route: "/redis",
    title: "Redis",
    description: "Explore keys, hashes, sorted sets, and server metrics. Demo caches come pre-seeded with sample data.",
    target: "[data-testid='redis-title']",
  },
  {
    route: "/storage",
    title: "Storage",
    description: "Navigate blob containers, upload files, compare versions, and restore deleted blobs in a safe sandbox.",
    target: "[data-testid='storage-title']",
  },
  {
    route: "/api-client",
    title: "API Client",
    description: "Build, save, and run HTTP, GraphQL, and WebSocket requests. Variables and environments work exactly as they do against a live backend.",
    target: "[data-testid='api-client-page']",
  },
  {
    route: "/agent",
    title: "AI Agent",
    description: "Ask the agent to investigate an issue, compare resources, or generate a diagram. Open Visualize to see maps, timelines, and Mermaid charts.",
    target: "[data-testid='agent-title']",
  },
  {
    route: "/monitoring",
    title: "Monitoring",
    description: "Define alert rules and watch the AI proactively investigate fired signals. Insights feed straight back into the cockpit.",
    target: "[data-testid='monitoring-title']",
  },
  {
    route: "/sql?connection=demo-sql-prd",
    title: "PRD day: read-only production",
    description:
      "Now imagine it's PRD day. This is orders-prd-sql — queries still run (SELECT/EXECUTE are granted), but catalog browsing is denied: no VIEW DEFINITION grant, so the tree shows only the objects your team declared by name. The prod Key Vault behaves the same way: its secrets simply won't resolve. Nothing here is faked — the restrictions are real demo restrictions.",
    target: "[data-testid='sql-schema-partial']",
    scenarioId: "prd-day",
    connection: "demo-sql-prd",
  },
  {
    route: "/settings?tab=access",
    title: "Access gaps report",
    description:
      "The Access tab probes every connection and tells you exactly what's denied — for this environment that's sql.metadata on orders-prd-sql — with the grant or role to ask your admin for, and a copyable access request on each denied row.",
    target: "[data-testid='access-report']",
    scenarioId: "prd-day",
  },
];

interface DemoTourState {
  isRunning: boolean;
  stepIndex: number;
  start: () => void;
  stop: () => void;
  next: () => void;
  previous: () => void;
}

export const useDemoTourStore = create<DemoTourState>((set) => ({
  isRunning: false,
  stepIndex: 0,
  start: () => set({ isRunning: true, stepIndex: 0 }),
  stop: () => set({ isRunning: false, stepIndex: 0 }),
  next: () =>
    set((state) => {
      const nextIndex = state.stepIndex + 1;
      if (nextIndex >= DEMO_TOUR_STEPS.length) return { isRunning: false, stepIndex: 0 };
      return { stepIndex: nextIndex };
    }),
  previous: () =>
    set((state) => {
      const prevIndex = state.stepIndex - 1;
      if (prevIndex < 0) return { stepIndex: 0 };
      return { stepIndex: prevIndex };
    }),
}));
