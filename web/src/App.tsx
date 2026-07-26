import { Routes, Route } from "react-router-dom";
import { AppLayout } from "./components/layout/AppLayout";
import { DashboardPage } from "./components/dashboard/DashboardPage";
import { ServiceBusPage } from "./components/service-bus/ServiceBusPage";
import { AksPage } from "./components/aks/AksPage";
import { ApiClientPage } from "./components/api-client/ApiClientPage";
import { RedisPage } from "./components/redis/RedisPage";
import { StoragePage } from "./components/storage/StoragePage";
import { AgentPage } from "./components/agent/AgentPage";
import { SettingsPage } from "./components/settings/SettingsPage";
import { MonitoringPage } from "./components/monitoring/MonitoringPage";

export default function App() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
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
