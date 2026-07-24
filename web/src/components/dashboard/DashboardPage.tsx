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
} from "lucide-react";
import { useHealth, useProfile } from "@/lib/hooks";

export function DashboardPage() {
  const { data: health } = useHealth();
  const { data: profile } = useProfile();

  const sidecarOk = health?.status === "ok";

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
    <div className="p-6">
      <h1 className="text-2xl font-bold">Dashboard</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        Developer Swiss army knife for Azure
      </p>

      {/* Sidecar status */}
      <div className="mt-6 flex items-center gap-2 rounded-lg border p-3">
        {sidecarOk ? (
          <CheckCircle2 className="h-5 w-5 text-green-500" />
        ) : (
          <XCircle className="h-5 w-5 text-destructive" />
        )}
        <span className="text-sm font-medium">
          Backend sidecar: {sidecarOk ? "Connected" : "Disconnected"}
        </span>
        {health?.version && (
          <span className="text-xs text-muted-foreground">v{health.version}</span>
        )}
      </div>

      {/* Service cards */}
      <div className="mt-6 grid grid-cols-2 gap-4 lg:grid-cols-3">
        {services.map((service) => {
          const Icon = service.icon;
          return (
            <Link
              key={service.name}
              to={service.to}
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
        className="mt-6 inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground"
      >
        <Settings className="h-4 w-4" />
        Configure connections in Settings
      </Link>
    </div>
  );
}
