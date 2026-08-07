import { create } from "zustand";

export interface DemoTourStep {
  route: string;
  title: string;
  description: string;
  target?: string;
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
