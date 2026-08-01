import { Suspense, lazy } from "react";
import { Routes, Route } from "react-router-dom";
import { AppLayout } from "./components/layout/AppLayout";
import { ErrorBoundary } from "./components/shared/ErrorBoundary";

// Each feature page is code-split so the initial bundle only carries the shell.
// The pages are named exports, hence the `.then` unwrap into a default.
const DashboardPage = lazy(() =>
  import("./components/dashboard/DashboardPage").then((m) => ({ default: m.DashboardPage })),
);
const ServiceBusPage = lazy(() =>
  import("./components/service-bus/ServiceBusPage").then((m) => ({ default: m.ServiceBusPage })),
);
const AksPage = lazy(() =>
  import("./components/aks/AksPage").then((m) => ({ default: m.AksPage })),
);
const ApiClientPage = lazy(() =>
  import("./components/api-client/ApiClientPage").then((m) => ({ default: m.ApiClientPage })),
);
const RedisPage = lazy(() =>
  import("./components/redis/RedisPage").then((m) => ({ default: m.RedisPage })),
);
const StoragePage = lazy(() =>
  import("./components/storage/StoragePage").then((m) => ({ default: m.StoragePage })),
);
const AgentPage = lazy(() =>
  import("./components/agent/AgentPage").then((m) => ({ default: m.AgentPage })),
);
const MonitoringPage = lazy(() =>
  import("./components/monitoring/MonitoringPage").then((m) => ({ default: m.MonitoringPage })),
);
const SettingsPage = lazy(() =>
  import("./components/settings/SettingsPage").then((m) => ({ default: m.SettingsPage })),
);

export default function App() {
  return (
    <Routes>
      <Route
        element={
          <Suspense fallback={null}>
            <AppLayout />
          </Suspense>
        }
      >
        <Route path="/" element={<ErrorBoundary><DashboardPage /></ErrorBoundary>} />
        <Route path="/service-bus" element={<ErrorBoundary><ServiceBusPage /></ErrorBoundary>} />
        <Route path="/aks" element={<ErrorBoundary><AksPage /></ErrorBoundary>} />
        <Route path="/api-client" element={<ErrorBoundary><ApiClientPage /></ErrorBoundary>} />
        <Route path="/redis" element={<ErrorBoundary><RedisPage /></ErrorBoundary>} />
        <Route path="/storage" element={<ErrorBoundary><StoragePage /></ErrorBoundary>} />
        <Route path="/agent" element={<ErrorBoundary><AgentPage /></ErrorBoundary>} />
        <Route path="/monitoring" element={<ErrorBoundary><MonitoringPage /></ErrorBoundary>} />
        <Route path="/settings" element={<ErrorBoundary><SettingsPage /></ErrorBoundary>} />
      </Route>
    </Routes>
  );
}
