import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
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
