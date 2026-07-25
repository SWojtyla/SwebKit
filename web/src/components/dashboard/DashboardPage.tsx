import { Link } from "react-router-dom";
import {
  MessageSquare,
  Ship,
  Code2,
  Database,
  FolderOpen,
  Bot,
  Settings,
  CheckCircle2,
  XCircle,
  FlaskConical,
} from "lucide-react";
import { useHealth, useProfile, useDemoMode, useToggleDemoMode } from "@/lib/hooks";

export function DashboardPage() {
  const { data: health } = useHealth();
  const { data: profile } = useProfile();
  const { data: demoMode } = useDemoMode();
  const toggleDemo = useToggleDemoMode();

  const sidecarOk = health?.status === "ok";
  const isDemo = demoMode?.isDemoMode ?? false;

  const services = [
    {
      name: "Service Bus",
      icon: MessageSquare,
      to: "/service-bus",
      count: profile?.serviceBusNamespaces.length ?? 0,
      unit: "namespaces",
    },
    {
      name: "AKS",
      icon: Ship,
      to: "/aks",
      configured: !!profile?.config.aksConfig,
    },
    {
      name: "API Client",
      icon: Code2,
      to: "/api-client",
    },
    {
      name: "Redis",
      icon: Database,
      to: "/redis",
      count: profile?.config.redisConfig?.caches.length ?? 0,
      unit: "caches",
    },
    {
      name: "Storage",
      icon: FolderOpen,
      to: "/storage",
      count: profile?.config.storageAccounts.length ?? 0,
      unit: "accounts",
    },
    {
      name: "AI Agent",
      icon: Bot,
      to: "/agent",
      enabled: profile?.config ? true : false,
    },
  ];

  return (
    <div className="p-6" data-testid="dashboard-page">
      <h1 className="text-2xl font-bold" data-testid="dashboard-title">Dashboard</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        Developer Swiss army knife for Azure
      </p>

      {/* Sidecar status + demo mode toggle */}
      <div className="mt-6 flex items-center gap-4 rounded-lg border p-3" data-testid="sidecar-status-bar">
        <div className="flex items-center gap-2">
          {sidecarOk ? (
            <CheckCircle2 className="h-5 w-5 text-green-500" />
          ) : (
            <XCircle className="h-5 w-5 text-destructive" />
          )}
          <span className="text-sm font-medium" data-testid="sidecar-status-text">
            Backend sidecar: {sidecarOk ? "Connected" : "Disconnected"}
          </span>
          {health?.version && (
            <span className="text-xs text-muted-foreground">v{health.version}</span>
          )}
        </div>
        <div className="ml-auto flex items-center gap-2">
          <FlaskConical className={`h-4 w-4 ${isDemo ? "text-primary" : "text-muted-foreground"}`} />
          <button
            data-testid="demo-mode-toggle"
            onClick={() => toggleDemo.mutate(!isDemo)}
            disabled={toggleDemo.isPending}
            className={`rounded-md border px-3 py-1.5 text-sm font-medium transition-colors ${
              isDemo
                ? "border-primary bg-primary text-primary-foreground"
                : "hover:bg-accent"
            }`}
          >
            {isDemo ? "Demo Mode ON" : "Enable Demo Mode"}
          </button>
        </div>
      </div>

      {/* Service cards */}
      <div className="mt-6 grid grid-cols-2 gap-4 lg:grid-cols-3" data-testid="service-cards">
        {services.map((service) => {
          const Icon = service.icon;
          return (
            <Link
              key={service.name}
              to={service.to}
              data-testid={`service-card-${service.name.toLowerCase().replace(/\s+/g, "-")}`}
              className="group rounded-lg border bg-card p-4 transition-colors hover:border-primary hover:bg-accent"
            >
              <div className="flex items-center gap-3">
                <Icon className="h-6 w-6 text-muted-foreground group-hover:text-foreground" />
                <div>
                  <h3 className="font-semibold">{service.name}</h3>
                  {"count" in service && service.count !== undefined && (
                    <p className="text-xs text-muted-foreground">
                      {service.count} {service.unit}
                    </p>
                  )}
                  {"configured" in service && (
                    <p className="text-xs text-muted-foreground">
                      {service.configured ? "Configured" : "Not configured"}
                    </p>
                  )}
                  {"enabled" in service && (
                    <p className="text-xs text-muted-foreground">
                      {service.enabled ? "Enabled" : "Disabled"}
                    </p>
                  )}
                </div>
              </div>
            </Link>
          );
        })}
      </div>

      {/* Quick link to settings */}
      <Link
        to="/settings"
        data-testid="settings-quick-link"
        className="mt-6 inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground"
      >
        <Settings className="h-4 w-4" />
        Configure connections in Settings
      </Link>
    </div>
  );
}
