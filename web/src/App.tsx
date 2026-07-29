import { Suspense, lazy } from "react";
import { Routes, Route } from "react-router-dom";
import { AppLayout } from "./components/layout/AppLayout";

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
        <Route path="/" element={<DashboardPage />} />
        <Route path="/service-bus" element={<ServiceBusPage />} />
        <Route path="/aks" element={<AksPage />} />
        <Route path="/api-client" element={<ApiClientPage />} />
        <Route path="/redis" element={<RedisPage />} />
        <Route path="/storage" element={<StoragePage />} />
        <Route path="/agent" element={<AgentPage />} />
        <Route path="/monitoring" element={<MonitoringPage />} />
        <Route path="/settings" element={<SettingsPage />} />
      </Route>
    </Routes>
  );
}
