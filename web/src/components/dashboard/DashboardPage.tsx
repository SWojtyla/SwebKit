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
  Activity,
  TrendingUp,
  AlertCircle,
  Pin,
  PinOff,
} from "lucide-react";
import { useHealth, useProfile, useDemoMode, useToggleDemoMode, useAksNamespaces, useAksDeployments, useAksPods, useRedisServerInfo, useStorageContainers, usePendingApprovals, useTogglePinnedResource } from "@/lib/hooks";
import type { FavoriteResource } from "@/lib/types";

export function DashboardPage() {
  const { data: health } = useHealth();
  const { data: profile } = useProfile();
  const { data: demoMode } = useDemoMode();
  const toggleDemo = useToggleDemoMode();

  const sidecarOk = health?.status === "ok";
  const isDemo = demoMode?.isDemoMode ?? false;

  // Demo mode doesn't synthesize a fake `aksConfig` into the profile the way it
  // populates Service Bus/Redis/Storage lists, so without this the AKS tile
  // would read "Not configured" while the AKS page itself works fully on demo data.
  const aksConfigured = isDemo || !!profile?.config.aksConfig;
  const redisCaches = profile?.config.redisConfig?.caches ?? [];
  const storageAccounts = profile?.config.storageAccounts ?? [];
  const sbNamespaces = profile?.serviceBusNamespaces ?? [];

  // Prefer the namespace the user actually configured. The old fallback was a
  // hardcoded "default", which most users have no RBAC on — those requests take
  // ~37s to come back 403 and, because the dashboard mounts first, they hold the
  // browser's per-host connections and stall every other page's queries.
  // Skip the slow cluster-scoped namespace listing when we already know which
  // namespace to use. This has to wait for the profile: firing while it is still
  // loading would issue the very request we are trying to avoid, and only cancel
  // it afterwards. When no namespace is configured we still list them, since demo
  // mode has no aksConfig but does serve namespaces.
  const configuredAksNs = profile?.config.aksConfig?.defaultNamespace?.trim() || null;
  const aksNamespaces = useAksNamespaces(!!profile && !configuredAksNs);
  const activeAksNs = configuredAksNs ?? aksNamespaces.data?.[0] ?? null;
  const aksDeployments = useAksDeployments(activeAksNs ?? null);
  const aksPods = useAksPods(activeAksNs ?? null);
  const redisInfo = useRedisServerInfo(redisCaches[0]?.id ?? null);
  const storageContainers = useStorageContainers(storageAccounts[0]?.id ?? null);
  const pendingApprovals = usePendingApprovals();
  const togglePinned = useTogglePinnedResource();

  const deploymentCount = aksDeployments.data?.length ?? 0;
  const podCount = aksPods.data?.length ?? 0;
  const cacheHitRate = redisInfo.data ? (redisInfo.data.keyspaceHitRatio * 100).toFixed(1) : null;
  const containerCount = storageContainers.data?.length ?? 0;

  const services = [
    {
      name: "Service Bus",
      icon: MessageSquare,
      to: "/service-bus",
      count: sbNamespaces.length,
      unit: "namespaces",
      configured: sbNamespaces.length > 0,
    },
    {
      name: "AKS",
      icon: Ship,
      to: "/aks",
      configured: aksConfigured,
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
      count: redisCaches.length,
      unit: "caches",
      configured: redisCaches.length > 0,
    },
    {
      name: "Storage",
      icon: FolderOpen,
      to: "/storage",
      count: storageAccounts.length,
      unit: "accounts",
      configured: storageAccounts.length > 0,
    },
    {
      name: "AI Agent",
      icon: Bot,
      to: "/agent",
      enabled: profile?.config ? true : false,
    },
  ];

  const healthTiles = [
    { name: "Service Bus", ok: sbNamespaces.length > 0, to: "/service-bus" },
    { name: "AKS", ok: aksConfigured, to: "/aks" },
    { name: "Redis", ok: redisCaches.length > 0, to: "/redis" },
    { name: "Storage", ok: storageAccounts.length > 0, to: "/storage" },
  ];

  const watchTiles = [
    { label: "Deployments", value: deploymentCount, to: "/aks", icon: Ship },
    { label: "Pods", value: podCount, to: "/aks", icon: Activity },
    { label: "Containers", value: containerCount, to: "/storage", icon: FolderOpen },
    { label: "Cache Hit Rate", value: cacheHitRate ? `${cacheHitRate}%` : "-", to: "/redis", icon: TrendingUp },
  ];

  const pendingCount = pendingApprovals.data?.count ?? 0;

  const resourceRows = [
    {
      key: "aks",
      name: "AKS",
      summary: aksConfigured ? "Cluster configured" : "Configure a cluster connection",
      to: "/aks",
      icon: Ship,
      ready: aksConfigured,
    },
    {
      key: "service-bus",
      name: "Service Bus",
      summary: sbNamespaces.length ? `${sbNamespaces.length} namespace${sbNamespaces.length === 1 ? "" : "s"}` : "Configure a namespace",
      to: "/service-bus",
      icon: MessageSquare,
      ready: sbNamespaces.length > 0,
    },
    {
      key: "redis",
      name: "Redis",
      summary: redisCaches.length ? `${redisCaches.length} cache${redisCaches.length === 1 ? "" : "s"}` : "Configure a cache",
      to: "/redis",
      icon: Database,
      ready: redisCaches.length > 0,
    },
    {
      key: "storage",
      name: "Storage",
      summary: storageAccounts.length ? `${storageAccounts.length} account${storageAccounts.length === 1 ? "" : "s"}` : "Configure an account",
      to: "/storage",
      icon: FolderOpen,
      ready: storageAccounts.length > 0,
    },
  ];

  const pinnedResources = profile?.config.favoriteResources ?? [];
  const isPinned = (key: string) =>
    pinnedResources.some((favorite) => favorite.snapshot.resource.key === key);
  const makeFavorite = (resource: (typeof resourceRows)[number]): FavoriteResource => ({
    name: resource.name,
    pinnedAt: new Date().toISOString(),
    snapshot: {
      resource: {
        key: resource.key,
        area: resource.key,
        kind: "service",
        displayName: resource.name,
        displayPath: resource.to,
        summary: resource.summary,
        icon: resource.key,
        metadata: {},
      },
      restoreState: {},
      capturedAt: new Date().toISOString(),
    },
  });

  const handlePin = (resource: (typeof resourceRows)[number]) => {
    if (!profile) return;
    togglePinned.mutate({
      profile,
      resource: makeFavorite(resource),
      pinned: !isPinned(resource.key),
    });
  };

  return (
    <div className="animate-fade-in-up p-6" data-testid="dashboard-page">
      <h1 className="gradient-text text-2xl font-bold" data-testid="dashboard-title">Dashboard</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        Developer Swiss army knife for Azure
      </p>

      {/* Sidecar status + demo mode toggle */}
      <div className="mt-6 flex items-center gap-4 glass-card rounded-xl p-4" data-testid="sidecar-status-bar">
        <div className="flex items-center gap-2">
          {sidecarOk ? (
            <CheckCircle2 className="h-5 w-5 text-success" />
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
            data-testid="dashboard-demo-mode-toggle"
            onClick={() => toggleDemo.mutate(!isDemo)}
            disabled={toggleDemo.isPending}
            className={`rounded-lg border px-3 py-1.5 text-sm font-medium transition-all ${
              isDemo
                ? "border-primary bg-primary text-primary-foreground shadow-sm"
                : "hover:bg-accent"
            }`}
          >
            {isDemo ? "Demo Mode ON" : "Enable Demo Mode"}
          </button>
        </div>
      </div>

      {/* Pending approvals banner */}
      {pendingCount > 0 && (
        <Link
          to="/agent"
          className="mt-4 flex items-center gap-3 rounded-xl border border-warning/30 bg-warning/10 p-3 transition-all hover:bg-warning/20"
          data-testid="pending-approvals-banner"
        >
          <AlertCircle className="h-5 w-5 text-warning" />
          <span className="text-sm font-medium">
            {pendingCount} pending approval{pendingCount > 1 ? "s" : ""} awaiting your review
          </span>
          <span className="ml-auto text-xs text-muted-foreground">Review now →</span>
        </Link>
      )}

      {/* Health tiles */}
      <div className="mt-6">
        <h2 className="mb-3 text-sm font-semibold text-muted-foreground">Service Health</h2>
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4" data-testid="health-tiles">
          {healthTiles.map((tile) => (
            <Link
              key={tile.name}
              to={tile.to}
              data-testid={`health-tile-${tile.name.toLowerCase().replace(/\s+/g, "-")}`}
              className="flex items-center gap-2 glass-card rounded-xl p-3 transition-all hover:border-primary hover:shadow-md"
            >
              {tile.ok ? (
                <CheckCircle2 className="h-4 w-4 text-success" />
              ) : (
                <AlertCircle className="h-4 w-4 text-muted-foreground" />
              )}
              <span className="text-sm font-medium">{tile.name}</span>
              <span className={`ml-auto text-xs ${tile.ok ? "text-success" : "text-muted-foreground"}`}>
                {tile.ok ? "Ready" : "Not configured"}
              </span>
            </Link>
          ))}
        </div>
      </div>

      {/* Watch tiles */}
      <div className="mt-6">
        <h2 className="mb-3 text-sm font-semibold text-muted-foreground">Watch</h2>
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4" data-testid="watch-tiles">
          {watchTiles.map((tile) => {
            const Icon = tile.icon;
            return (
              <Link
                key={tile.label}
                to={tile.to}
                data-testid={`watch-tile-${tile.label.toLowerCase().replace(/\s+/g, "-")}`}
                className="glass-card rounded-xl p-3 transition-all hover:border-primary hover:shadow-md"
              >
                <div className="flex items-center gap-2">
                  <Icon className="h-4 w-4 text-primary" />
                  <span className="text-xs text-muted-foreground">{tile.label}</span>
                </div>
                <div className="mt-1 text-xl font-semibold">{tile.value}</div>
              </Link>
            );
          })}
        </div>
      </div>

      <div className="mt-6">
        <h2 className="mb-3 text-sm font-semibold text-muted-foreground">Resources</h2>
        <div className="space-y-2" data-testid="dashboard-resource-rows">
          {resourceRows.map((resource) => {
            const Icon = resource.icon;
            const pinned = isPinned(resource.key);
            return (
              <div key={resource.key} className="flex items-center gap-3 rounded-xl border bg-card/50 p-3" data-testid={`dashboard-resource-row-${resource.key}`}>
                <Icon className="h-5 w-5 text-primary" />
                <Link to={resource.to} className="min-w-0 flex-1 hover:text-primary">
                  <div className="font-medium">{resource.name}</div>
                  <div className="text-xs text-muted-foreground">{resource.summary}</div>
                </Link>
                <button
                  type="button"
                  aria-label={pinned ? `Unpin ${resource.name}` : `Pin ${resource.name} to dashboard`}
                  title={pinned ? "Unpin from dashboard" : "Pin to dashboard"}
                  data-testid={`pin-resource-${resource.key}`}
                  onClick={() => handlePin(resource)}
                  disabled={togglePinned.isPending}
                  className={`rounded-md p-2 transition-colors hover:bg-accent ${pinned ? "text-primary" : "text-muted-foreground"}`}
                >
                  {pinned ? <PinOff className="h-4 w-4" /> : <Pin className="h-4 w-4" />}
                </button>
              </div>
            );
          })}
        </div>
      </div>

      <div className="mt-6" data-testid="pinned-resources">
        <h2 className="mb-3 text-sm font-semibold text-muted-foreground">Pinned to dashboard</h2>
        {pinnedResources.length === 0 ? (
          <p className="rounded-xl border border-dashed p-4 text-sm text-muted-foreground">
            Pin a resource above for quick access.
          </p>
        ) : (
          <div className="flex flex-wrap gap-2">
            {pinnedResources.map((favorite) => (
              <Link
                key={favorite.snapshot.resource.key}
                to={favorite.snapshot.resource.displayPath ?? "/"}
                className="inline-flex items-center gap-2 rounded-lg border bg-card/50 px-3 py-2 text-sm hover:border-primary"
                data-testid={`pinned-resource-${favorite.snapshot.resource.key}`}
              >
                <Pin className="h-3.5 w-3.5 text-primary" />
                {favorite.name}
              </Link>
            ))}
          </div>
        )}
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
              className="group glass-card rounded-xl p-4 transition-all hover:border-primary hover:shadow-md"
            >
              <div className="flex items-center gap-3">
                <Icon className="h-6 w-6 text-primary group-hover:text-primary" />
                <div>
                  <h3 className="font-semibold">{service.name}</h3>
                  {"count" in service && service.count !== undefined && (
                    <p className="text-xs text-muted-foreground">
                      {service.count} {service.count === 1 && service.unit?.endsWith("s") ? service.unit.slice(0, -1) : service.unit}
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
