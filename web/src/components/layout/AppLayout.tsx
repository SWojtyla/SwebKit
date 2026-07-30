import { useState, useEffect, useCallback } from "react";
import { NavLink, Outlet, useNavigate, useLocation } from "react-router-dom";
import {
  LayoutDashboard,
  MessageSquare,
  Ship,
  Code2,
  Database,
  FolderOpen,
  Bot,
  Settings,
  Search,
  Circle,
  Activity,
  PanelLeftClose,
  PanelLeftOpen,
  Sun,
  Moon,
  Sparkles,
  Beaker,
  Keyboard,
} from "lucide-react";
import { CommandPalette } from "./CommandPalette";
import { KeyboardShortcutsPanel } from "./KeyboardShortcutsPanel";
import {
  useAksTestConnection,
  useDemoMode,
  useHealth,
  useProfile,
  useRedisServerInfo,
  useSbTestConnection,
  useStorageContainers,
  useToggleDemoMode,
} from "@/lib/hooks";
import { useSettingsStore } from "@/lib/stores/settings";

const navItems = [
  { to: "/", label: "Dashboard", icon: LayoutDashboard, end: true },
  { to: "/service-bus", label: "Service Bus", icon: MessageSquare },
  { to: "/aks", label: "AKS", icon: Ship },
  { to: "/api-client", label: "API Client", icon: Code2 },
  { to: "/redis", label: "Redis", icon: Database },
  { to: "/storage", label: "Storage", icon: FolderOpen },
  { to: "/agent", label: "AI Agent", icon: Bot },
  { to: "/monitoring", label: "Monitoring", icon: Activity },
  { to: "/settings", label: "Settings", icon: Settings },
];

export function AppLayout() {
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [shortcutsOpen, setShortcutsOpen] = useState(false);
  const [navCollapsed, setNavCollapsed] = useState(false);
  const navigate = useNavigate();
  const location = useLocation();
  const { data: health } = useHealth();
  const { data: profile } = useProfile();
  const { data: demoData } = useDemoMode();
  const sbHealth = useSbTestConnection(profile?.serviceBusNamespaces[0]?.id ?? null);
  const aksHealth = useAksTestConnection();
  const redisHealth = useRedisServerInfo(profile?.config.redisConfig?.caches[0]?.id ?? null);
  const storageHealth = useStorageContainers(profile?.config.storageAccounts[0]?.id ?? null);
  const toggleDemoMode = useToggleDemoMode();
  const { theme, toggleTheme } = useSettingsStore();
  const sidecarOk = health?.status === "ok";
  const isDemoMode = demoData?.isDemoMode ?? false;

  const contextTitle = navItems.find((n) => n.to === location.pathname)?.label ?? "SwebKit";
  const areaHealth = [
    {
      id: "service-bus",
      label: "Service Bus",
      configured: isDemoMode || (profile?.serviceBusNamespaces.length ?? 0) > 0,
      query: sbHealth,
      connected: sbHealth.data?.connected ?? false,
    },
    {
      id: "aks",
      label: "AKS",
      configured: isDemoMode || profile?.config.aksConfig != null,
      query: aksHealth,
      connected: aksHealth.data?.connected ?? false,
    },
    {
      id: "redis",
      label: "Redis",
      configured: isDemoMode || (profile?.config.redisConfig?.caches.length ?? 0) > 0,
      query: redisHealth,
      connected: redisHealth.data != null,
    },
    {
      id: "storage",
      label: "Storage",
      configured: isDemoMode || (profile?.config.storageAccounts.length ?? 0) > 0,
      query: storageHealth,
      connected: storageHealth.data != null,
    },
  ];

  const handleKeyDown = useCallback((e: KeyboardEvent) => {
    if ((e.ctrlKey || e.metaKey) && (e.key === "k" || e.key === "K")) {
      e.preventDefault();
      setPaletteOpen((prev) => !prev);
    } else if ((e.ctrlKey || e.metaKey) && (e.key === "g" || e.key === "G")) {
      e.preventDefault();
      navigate("/settings");
    } else if ((e.ctrlKey || e.metaKey) && (e.key === "b" || e.key === "B")) {
      e.preventDefault();
      setNavCollapsed((prev) => !prev);
    } else if (((e.key === "?" && e.shiftKey) || (e.key === "/" && e.shiftKey)) && (e.target === document.body || e.target === document.documentElement)) {
      e.preventDefault();
      setShortcutsOpen(true);
    }
  }, [navigate]);

  useEffect(() => {
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [handleKeyDown]);

  return (
    <div className="aurora-bg theme-transition flex h-screen w-screen overflow-hidden bg-background text-foreground">
      <aside className={`flex flex-col border-r bg-sidebar transition-all duration-200 ${navCollapsed ? "w-14" : "w-56"}`}>
        <div className="flex h-14 items-center gap-2 border-b px-4">
          {!navCollapsed && <span className="gradient-text text-lg font-bold tracking-tight">SwebKit</span>}
          <button
            onClick={() => setNavCollapsed(!navCollapsed)}
            className="ml-auto rounded p-1 text-muted-foreground hover:bg-accent"
            data-testid="nav-collapse-toggle"
          >
            {navCollapsed ? <PanelLeftOpen className="h-4 w-4" /> : <PanelLeftClose className="h-4 w-4" />}
          </button>
        </div>
        <nav className="flex-1 space-y-1 p-2">
          {navItems.map(({ to, label, icon: Icon, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              data-testid={`nav-${label.toLowerCase().replace(/\s+/g, "-")}`}
              className={({ isActive }) =>
                `flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-all duration-200 ${
                  isActive
                    ? "bg-primary text-primary-foreground shadow-sm"
                    : "text-sidebar-foreground hover:bg-sidebar-active hover:text-primary"
                }`
              }
            >
              <Icon className="h-4 w-4" />
              {!navCollapsed && label}
            </NavLink>
          ))}
        </nav>
      </aside>

      <div className="flex flex-1 flex-col overflow-hidden">
        {/* Top bar */}
        <header className="glass flex min-h-14 flex-wrap items-center gap-2 border-b px-3 py-2" data-testid="top-bar">
          <span className="text-sm font-semibold" data-testid="context-title">{contextTitle}</span>
          <button
            onClick={() => setPaletteOpen(true)}
            className="flex items-center gap-2 rounded-lg border bg-muted/50 px-3 py-1.5 text-sm text-muted-foreground transition-all hover:border-primary/30 hover:bg-accent"
            data-testid="command-palette-trigger"
          >
            <Search className="h-4 w-4" />
            <span className="hidden sm:inline">Search...</span>
            <kbd className="ml-4 rounded border bg-card px-1.5 py-0.5 text-xs text-muted-foreground hidden sm:inline">Ctrl+K</kbd>
          </button>
          <div className="ml-auto flex flex-wrap items-center gap-2">
            <button
              onClick={() => toggleTheme()}
              className="rounded-lg border p-2 text-muted-foreground transition-all hover:bg-accent hover:text-foreground"
              data-testid="theme-toggle"
              title="Toggle theme"
            >
              {theme === "dark" ? <Sun className="h-4 w-4" /> : theme === "fancy" ? <Sparkles className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
            </button>
            <button
              onClick={() => toggleDemoMode.mutate(!isDemoMode)}
              disabled={toggleDemoMode.isPending}
              className={`flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs ${isDemoMode ? "border-primary text-primary" : "text-muted-foreground hover:bg-accent"} disabled:opacity-50`}
              data-testid="demo-mode-toggle"
              title="Toggle demo mode"
            >
              <Beaker className="h-3.5 w-3.5" />
              {toggleDemoMode.isPending ? "..." : isDemoMode ? "Demo" : "Live"}
            </button>
            <button
              onClick={() => setShortcutsOpen(true)}
              className="rounded-lg border p-2 text-muted-foreground transition-all hover:bg-accent hover:text-foreground"
              data-testid="keyboard-shortcuts-btn"
              title="Keyboard shortcuts (?)"
            >
              <Keyboard className="h-4 w-4" />
            </button>
          </div>
        </header>

        <main className="flex-1 overflow-auto">
          <Outlet />
        </main>

        {/* Status bar */}
        <footer className="glass flex h-7 items-center gap-4 border-t px-4 text-xs text-muted-foreground" data-testid="status-bar">
          <div className="flex items-center gap-1.5">
            <Circle className={`h-2 w-2 ${sidecarOk ? "fill-success text-success" : "fill-destructive text-destructive"}`} />
            <span data-testid="status-bar-connection">{sidecarOk ? "Connected" : "Disconnected"}</span>
          </div>
          <div className="flex min-w-0 items-center gap-3" data-testid="status-bar-area-health">
            {areaHealth.map(({ id, label, configured, query, connected }) => {
              const state = !configured
                ? "Not configured"
                : query.isPending
                  ? "Checking"
                  : query.isError || !connected
                    ? "Unavailable"
                    : "Connected";
              const stateClass = state === "Connected"
                ? "fill-success text-success"
                : state === "Checking"
                  ? "fill-warning text-warning"
                  : state === "Not configured"
                    ? "fill-muted-foreground text-muted-foreground"
                    : "fill-destructive text-destructive";

              return (
                <div
                  key={id}
                  className="flex items-center gap-1"
                  data-testid={`status-bar-health-${id}`}
                  aria-label={`${label}: ${state}`}
                  title={`${label}: ${state}`}
                >
                  <Circle className={`h-1.5 w-1.5 ${stateClass}`} />
                  <span>{label}</span>
                </div>
              );
            })}
          </div>
          {health?.version && (
            <span>v{health.version}</span>
          )}
          {isDemoMode && (
            <span className="text-warning" data-testid="status-bar-demo">Demo Mode</span>
          )}
          <span className="ml-auto">{theme === "dark" ? "Dark" : theme === "fancy" ? "✨ Fancy ✨" : "Light"} theme</span>
          <span>SwebKit</span>
        </footer>
      </div>

      <CommandPalette open={paletteOpen} onClose={() => setPaletteOpen(false)} />
      <KeyboardShortcutsPanel open={shortcutsOpen} onClose={() => setShortcutsOpen(false)} />
    </div>
  );
}
