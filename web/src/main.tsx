import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router";
import App from "./App";
import { NotificationProvider } from "./components/layout/NotificationSystem";
import { initSidecarBaseUrl } from "./lib/api";
import "./styles/globals.css";

// Initialize theme class on document element
document.documentElement.classList.add("dark");

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
      // This is a desktop app that people alt-tab out of constantly. The default (`true`) re-fires
      // every active query on every window focus, which for AKS or Service Bus means replaying a
      // whole fan-out — a full namespace list, or one request per topic — just because the window
      // regained focus. Every page has an explicit Refresh, and the volatile queries carry their
      // own short `staleTime`, so nothing here depends on focus to stay current.
      refetchOnWindowFocus: false,
    },
  },
});

// The sidecar's real port is only known after asking Tauri for it (production
// uses an OS-assigned port, not a fixed one) — every API call would hit the
// wrong port if we rendered before this resolves.
initSidecarBaseUrl().then(() => {
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <NotificationProvider>
            <App />
          </NotificationProvider>
        </BrowserRouter>
      </QueryClientProvider>
    </StrictMode>,
  );
});
