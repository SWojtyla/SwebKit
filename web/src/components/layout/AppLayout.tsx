import { useState, useEffect, useCallback, useRef } from "react";
import { useQueryClient } from "@tanstack/react-query";
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
  Waves,
} from "lucide-react";
import { CommandPalette } from "./CommandPalette";
import { KeyboardShortcutsPanel } from "./KeyboardShortcutsPanel";
import { GlobalAgentPanel } from "@/components/agent/GlobalAgentPanel";
import {
  useAksTestConnection,
  useDemoMode,
  useHealth,
  useProfile,
  useRedisServerInfo,
  useSbTestConnection,
  useStorageContainers,
  useToggleDemoMode,
  useUserSettings,
  useUpdateUserSettings,
} from "@/lib/hooks";
import { FATHOM_UNLOCK_THRESHOLD } from "@/lib/types";
import { useSettingsStore, isTheme } from "@/lib/stores/settings";
import { onSidecarLifecycleEvent, restartSidecar } from "@/lib/tauri-bridge";
import { initSidecarBaseUrl } from "@/lib/api";
import { useNotification } from "./NotificationSystem";

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
  const [agentPanelOpen, setAgentPanelOpen] = useState(false);
  const navigate = useNavigate();
  const location = useLocation();
  const onAgentPage = location.pathname === "/agent";

  // The full /agent page and this panel are two views onto the identical global conversation
  // (see useGlobalAgentConversation) — showing both at once would just be the same messages
  // twice, so the panel auto-closes whenever the user navigates to the dedicated page instead.
  useEffect(() => {
    if (onAgentPage) setAgentPanelOpen(false);
  }, [onAgentPage]);
  const { data: health } = useHealth();
  const { data: profile } = useProfile();
  const { data: demoData } = useDemoMode();
  const sbHealth = useSbTestConnection(profile?.serviceBusNamespaces[0]?.id ?? null);
  const aksHealth = useAksTestConnection();
  const redisHealth = useRedisServerInfo(profile?.config.redisConfig?.caches[0]?.id ?? null);
  const storageHealth = useStorageContainers(profile?.config.storageAccounts[0]?.id ?? null);
  const toggleDemoMode = useToggleDemoMode();
  const { theme, toggleTheme, setTheme } = useSettingsStore();
  const { data: userSettings } = useUserSettings();
  const updateUserSettings = useUpdateUserSettings();

  // Theme lives in the sidecar's user-settings.json, not just this session's Zustand store —
  // without this, a restart always came back to the "dark" default no matter what was picked.
  // Applied once per app launch, on the first settings load; later local changes push the other
  // direction (see the theme toggle button and AppearanceSettings' card clicks).
  const themeHydratedRef = useRef(false);
  useEffect(() => {
    if (themeHydratedRef.current || !userSettings) return;
    themeHydratedRef.current = true;
    if (isTheme(userSettings.theme) && userSettings.theme !== theme) {
      setTheme(userSettings.theme);
    }
  }, [userSettings, theme, setTheme]);

  // Apply font-size and density from user settings. The root font-size is a simple rem scaler;
  // density is surfaced as a data attribute for future CSS and for select controls to reflect.
  useEffect(() => {
    if (!userSettings) return;
    const fontSizeMap: Record<string, string> = { small: "14px", medium: "16px", large: "18px" };
    document.documentElement.style.fontSize = fontSizeMap[userSettings.fontSize] ?? "16px";
    document.documentElement.dataset.density = userSettings.density ?? "comfortable";
  }, [userSettings]);
  const sidecarOk = health?.status === "ok";
  const isDemoMode = demoData?.isDemoMode ?? false;
  const [reconnecting, setReconnecting] = useState(false);
  const queryClient = useQueryClient();
  const { notify } = useNotification();

  // Fathom's "thank you" moment: sessionCount lands on the threshold exactly once (it only ever
  // increments), so this fires on the one launch that crosses it and never again — no separate
  // "already celebrated" flag needed server-side.
  const firedFathomToastRef = useRef(false);
  useEffect(() => {
    if (firedFathomToastRef.current || !userSettings) return;
    if (userSettings.sessionCount === FATHOM_UNLOCK_THRESHOLD && userSettings.fathomUnlocked) {
      firedFathomToastRef.current = true;
      notify("success", "New depth reached", "Fathom is unlocked in Settings → Appearance — thanks for taking SwebKit this deep.");
    }
  }, [userSettings, notify]);

  // Hidden six-click gesture on the status bar version number: sets a developer-only override
  // that skips the session-count gate on this machine, without any visible UI for it elsewhere.
  const versionClicksRef = useRef(0);
  const versionClickTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const handleVersionClick = useCallback(() => {
    versionClicksRef.current += 1;
    if (versionClickTimerRef.current) clearTimeout(versionClickTimerRef.current);
    versionClickTimerRef.current = setTimeout(() => {
      versionClicksRef.current = 0;
    }, 1500);

    if (versionClicksRef.current >= 6) {
      versionClicksRef.current = 0;
      if (userSettings && !userSettings.fathomDeveloperOverride) {
        updateUserSettings.mutate(
          { ...userSettings, fathomDeveloperOverride: true },
          { onSuccess: () => notify("success", "Developer override armed", "Fathom is unlocked for this profile only.") },
        );
      }
    }
  }, [userSettings, updateUserSettings, notify]);

  // The sidecar previously had no recovery path if it crashed mid-session: `restart_sidecar`
  // existed as a Tauri command but nothing ever called it, so a crash silently broke the app
  // until the user manually relaunched. `useHealth`'s 10s poll already detects the outage
  // (`sidecarOk` goes false); this adds the missing recovery action.
  const handleReconnect = useCallback(async () => {
    setReconnecting(true);
    try {
      const port = await restartSidecar();
      if (port !== null) {
        await initSidecarBaseUrl();
      }
      await queryClient.invalidateQueries();
      notify("success", "Reconnected to the sidecar");
    } catch (err) {
      notify("error", "Reconnect failed", err instanceof Error ? err.message : String(err));
    } finally {
      setReconnecting(false);
    }
  }, [queryClient, notify]);

  // Complements handleReconnect (manual, user-triggered) with the automatic side: the Rust side
  // now supervises the sidecar child process itself and respawns it on an unexpected exit without
  // waiting for the frontend's health poll to notice (see sidecar.rs's watch_for_crash) — this
  // just reflects that in the UI instead of requiring the user to notice "Disconnected" and click
  // Reconnect themselves, which was the whole gap: a crash used to be invisible here until someone
  // manually relaunched the entire app.
  useEffect(() => {
    let disposed = false;
    let unlisten: (() => void) | undefined;

    onSidecarLifecycleEvent({
      onCrashed: () => {
        notify("info", "Sidecar disconnected", "Attempting automatic recovery…");
      },
      onRestarted: async (port) => {
        void port; // the Rust side already updated its own state; re-resolving here just syncs ours
        await initSidecarBaseUrl();
        await queryClient.invalidateQueries();
        notify("success", "Sidecar recovered automatically");
      },
      onRecoveryFailed: () => {
        notify("error", "Sidecar recovery failed", "Use the Reconnect button below, or restart the app.");
      },
    }).then((dispose) => {
      if (disposed) dispose();
      else unlisten = dispose;
    });

    return () => {
      disposed = true;
      unlisten?.();
    };
  }, [queryClient, notify]);

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
    } else if ((e.ctrlKey || e.metaKey) && e.shiftKey && (e.key === "l" || e.key === "L")) {
      // Not Ctrl+Shift+A: Chrome's built-in "Search tabs" shortcut swallows that combo before it
      // ever reaches page JS (confirmed empirically — the keydown event never fires at all), and
      // the packaged app's WebView2 shell is the same Chromium engine, so it would hit the same
      // wall for real users, not just in tests.
      e.preventDefault();
      setAgentPanelOpen((prev) => (onAgentPage ? prev : !prev));
    } else if (((e.key === "?" && e.shiftKey) || (e.key === "/" && e.shiftKey)) && (e.target === document.body || e.target === document.documentElement)) {
      e.preventDefault();
      setShortcutsOpen(true);
    }
  }, [navigate, onAgentPage]);

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
              onClick={() => {
                toggleTheme();
                if (userSettings) {
                  updateUserSettings.mutate({ ...userSettings, theme: useSettingsStore.getState().theme });
                }
              }}
              className="rounded-lg border p-2 text-muted-foreground transition-all hover:bg-accent hover:text-foreground"
              data-testid="theme-toggle"
              title="Toggle theme"
            >
              {theme === "dark" ? (
                <Sun className="h-4 w-4" />
              ) : theme === "fancy" ? (
                <Sparkles className="h-4 w-4" />
              ) : theme === "fathom-dark" || theme === "fathom-light" ? (
                <Waves className="h-4 w-4" />
              ) : (
                <Moon className="h-4 w-4" />
              )}
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
            {!onAgentPage && (
              <button
                onClick={() => setAgentPanelOpen((prev) => !prev)}
                className={`rounded-lg border p-2 transition-all hover:bg-accent hover:text-foreground ${agentPanelOpen ? "border-primary text-primary" : "text-muted-foreground"}`}
                data-testid="global-agent-panel-toggle"
                title="AI Agent (Ctrl+Shift+L)"
              >
                <Bot className="h-4 w-4" />
              </button>
            )}
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
            {!sidecarOk && (
              <button
                onClick={handleReconnect}
                disabled={reconnecting}
                className="ml-1 rounded border px-1.5 py-0.5 text-[11px] hover:bg-accent disabled:opacity-50"
                data-testid="status-bar-reconnect"
              >
                {reconnecting ? "Reconnecting…" : "Reconnect"}
              </button>
            )}
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
            <span onClick={handleVersionClick}>v{health.version}</span>
          )}
          {isDemoMode && (
            <span className="text-warning" data-testid="status-bar-demo">Demo Mode</span>
          )}
          <span className="ml-auto">
            {theme === "dark"
              ? "Dark"
              : theme === "fancy"
                ? "✨ Fancy ✨"
                : theme === "fathom-dark"
                  ? "Fathom · Abyss"
                  : theme === "fathom-light"
                    ? "Fathom · Shallows"
                    : "Light"}{" "}
            theme
          </span>
          <span>SwebKit</span>
        </footer>
      </div>

      <GlobalAgentPanel open={agentPanelOpen} onClose={() => setAgentPanelOpen(false)} />

      <CommandPalette open={paletteOpen} onClose={() => setPaletteOpen(false)} />
      <KeyboardShortcutsPanel open={shortcutsOpen} onClose={() => setShortcutsOpen(false)} />
    </div>
  );
}
