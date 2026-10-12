import { create } from "zustand";

export interface DemoTourStep {
    route: string;
    title: string;
    description: string;
    target?: string;
    /** Optional hands-on suggestion rendered as a "Try it" hint. Never gated —
     *  Next stays available whether or not the user does it. */
    tryIt?: string;
}

export const DEMO_TOUR_STEPS: DemoTourStep[] = [
    {
        route: "/",
        title: "Welcome to the cockpit",
        description:
            "This is your command center: health tiles, live watch metrics, workspace topology, and proactive AI insights in one place. Everything you're about to see runs on demo data — real client code, synthetic services, nothing touches real infrastructure.",
        target: "[data-testid='dashboard-title']",
    },
    {
        route: "/",
        title: "Command palette",
        description:
            "The fastest way around: the palette jumps to any page, entity, or action and remembers your most-used items. It's always one shortcut away.",
        target: "[data-testid='command-palette-trigger']",
        tryIt: "Press Ctrl+K and type a resource name — e.g. 'pods' or 'orders'.",
    },
    {
        route: "/aks",
        title: "AKS: pick your clusters",
        description:
            "The context picker drives the whole page. One cluster is primary — actions and the agent run against it — but you can attach more clusters and browse them merged, with a Context column telling you which row lives where.",
        target: "[data-testid='aks-context-select']",
        tryIt: "Open the context picker and tick a second cluster, then watch the tables merge.",
    },
    {
        route: "/aks",
        title: "Namespaces, grouped per cluster",
        description:
            "With several clusters attached, the namespace picker groups by cluster so 'all of dev' and 'just payments on prod' are one click apart. Your selection is a shareable deep link — the URL carries it.",
        target: "[data-testid='aks-namespace-select']",
        tryIt: "Open the namespace picker and select one namespace per cluster.",
    },
    {
        route: "/aks",
        title: "Rows: click vs. right-click",
        description:
            "Left-click any row to open its details or YAML — every resource tab (Pods, Deployments, Services, Secrets, and the rest) works this way. Right-click a row for the full action menu: scale, delete, restart, port-forward, and more.",
        target: "[data-testid='aks-content']",
    },
    {
        route: "/aks",
        title: "Logs, shell, port-forward",
        description:
            "Every pod row opens live-tailed logs with search and timestamps, a shell into the container, or a port-forward — and each one is routed to the cluster that row came from. Confirmations always name the cluster they act on.",
        target: "[data-testid='aks-content']",
        tryIt: "Right-click a pod row and open its logs.",
    },
    {
        route: "/service-bus",
        title: "Service Bus explorer",
        description:
            "Browse namespaces, queues, topics, and subscriptions. Peek, send, complete, or dead-letter messages — all safe in the sandbox, and the demos come pre-loaded.",
        target: "[data-testid='service-bus-page']",
        tryIt: "Click a queue in the tree and peek at its messages.",
    },
    {
        route: "/service-bus",
        title: "Filters that understand values",
        description:
            "Text search covers ids, subjects, and bodies. Advanced rules go further: application properties compare as text, numbers (>, >=, <, <=), and dates (before/after) — including NServiceBus-style 'TimeSent' timestamps.",
        target: "[data-testid='service-bus-page']",
        tryIt: "Open Advanced, add a rule on NServiceBus.TimeSent 'After (date)', and watch the list narrow.",
    },
    {
        route: "/service-bus",
        title: "Dead-letter triage",
        description:
            "Flip to the DLQ view on any queue: inspect the dead-letter reason, edit-and-resend, or push messages back to the live queue. Triage tools group failures so a flood of the same error stays readable.",
        target: "[data-testid='sb-view-selector']",
        tryIt: "Switch to the DLQ view on a queue and open a dead-lettered message.",
    },
    {
        route: "/redis",
        title: "Redis explorer",
        description:
            "Explore keys, hashes, sorted sets, and TTLs, plus server metrics. The demo cache is pre-seeded with a realistic keyspace.",
        target: "[data-testid='redis-title']",
    },
    {
        route: "/storage",
        title: "Blob storage",
        description:
            "Navigate containers and blobs, upload files, compare versions, and restore soft-deleted blobs in a safe sandbox.",
        target: "[data-testid='storage-title']",
    },
    {
        route: "/sql",
        title: "SQL workbench",
        description:
            "Browse the schema tree, run queries, and keep a saved-query library per connection. Read-only connections are clearly badged so there's no guessing what's allowed.",
        target: "[data-testid='sql-title']",
        tryIt: "Run one of the saved demo queries.",
    },
    {
        route: "/api-client",
        title: "API Client",
        description:
            "A Bruno-grade HTTP client: collections, folders, environments with variables, plus GraphQL and WebSocket. The demo collections are ready to send against stub endpoints.",
        target: "[data-testid='api-client-page']",
        tryIt: "Pick an environment and send a request from a demo collection.",
    },
    {
        route: "/agent",
        title: "AI Agent",
        description:
            "Ask the agent to investigate an issue, explain a resource, or compare environments — it has tools for every surface you just toured. Open Visualize for maps, timelines, and Mermaid diagrams.",
        target: "[data-testid='agent-title']",
        tryIt: "Ask it 'what looks unhealthy right now?'.",
    },
    {
        route: "/monitoring",
        title: "Monitoring & AI reports",
        description:
            "Alert rules evaluate your workspace continuously; rules in AI-investigation mode get a real agent investigation when they fire. Reports land on a kanban — Queued, Ready, Done — click any card for the full-width report.",
        target: "[data-testid='monitoring-title']",
        tryIt: "Open the AI Reports tab and click a card.",
    },
    {
        route: "/",
        title: "Make it yours",
        description:
            "Pin the resources you watch daily, share deep links that open the exact same view, and flip between live and demo profiles from the header. That's the tour — everything else is yours to poke.",
        target: "[data-testid='dashboard-title']",
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
            if (nextIndex >= DEMO_TOUR_STEPS.length)
                return { isRunning: false, stepIndex: 0 };
            return { stepIndex: nextIndex };
        }),
    previous: () =>
        set((state) => {
            const prevIndex = state.stepIndex - 1;
            if (prevIndex < 0) return { stepIndex: 0 };
            return { stepIndex: prevIndex };
        }),
}));
